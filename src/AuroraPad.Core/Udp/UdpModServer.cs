using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using AuroraPad.Core.Devices;
using AuroraPad.Core.Protocol;
using AuroraPad.Core.Triggers;

namespace AuroraPad.Core.Udp;

/// <summary>
/// UDP listener compatible with the JSON packets sent by existing DSX game mods:
/// <c>{"instructions":[{"type":1,"parameters":[0,2,13,0,8]}]}</c>.
/// Each packet gets a JSON status reply sent back to the sender.
/// </summary>
public sealed class UdpModServer : IDisposable
{
    public enum InstructionType
    {
        Invalid = 0,
        TriggerUpdate = 1,
        RGBUpdate = 2,
        PlayerLED = 3,
        TriggerThreshold = 4,
        MicLED = 5,
        PlayerLEDNewRevision = 6,
        ResetToUserSettings = 7,
    }

    private readonly Func<IReadOnlyList<ControllerSession>> _sessions;
    private UdpClient? _udp;
    private CancellationTokenSource? _cts;

    public bool IsRunning => _udp != null;
    public int PacketsReceived { get; private set; }
    public DateTime? LastPacket { get; private set; }
    public string? LastError { get; private set; }

    public event Action<string>? Log;

    public UdpModServer(Func<IReadOnlyList<ControllerSession>> sessions) => _sessions = sessions;

    public bool Start(string address, int port)
    {
        Stop();
        try
        {
            var ip = IPAddress.TryParse(address, out var a) ? a : IPAddress.Loopback;
            _udp = new UdpClient(new IPEndPoint(ip, port));
            // Ignore ICMP "port unreachable" resets when a mod closes before we reply.
            const int SioUdpConnReset = -1744830452;
            try { _udp.Client.IOControl(SioUdpConnReset, [0, 0, 0, 0], null); } catch { }
            _cts = new CancellationTokenSource();
            _ = Task.Run(() => Loop(_cts.Token));
            LastError = null;
            Log?.Invoke($"UDP escutando em {ip}:{port}");
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            _udp = null;
            Log?.Invoke($"Falha ao abrir UDP {address}:{port}: {ex.Message}");
            return false;
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
        _udp?.Dispose();
        _udp = null;
    }

    private async Task Loop(CancellationToken ct)
    {
        var udp = _udp!;
        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult r;
            try { r = await udp.ReceiveAsync(ct); }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) { continue; }

            PacketsReceived++;
            LastPacket = DateTime.Now;
            try
            {
                Handle(Encoding.UTF8.GetString(r.Buffer));
            }
            catch (Exception ex)
            {
                Log?.Invoke($"Pacote inválido: {ex.Message}");
            }

            try
            {
                var reply = Encoding.UTF8.GetBytes(BuildResponse());
                await udp.SendAsync(reply, r.RemoteEndPoint, ct);
            }
            catch { }
        }
    }

    public void Handle(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!TryGetProperty(doc.RootElement, "instructions", out var instructions)) return;

        foreach (var ins in instructions.EnumerateArray())
        {
            if (!TryGetProperty(ins, "type", out var typeEl)) continue;
            var type = (InstructionType)ReadInt(typeEl);
            var args = TryGetProperty(ins, "parameters", out var pe) ? pe.EnumerateArray().ToArray() : [];
            Execute(type, args);
        }
    }

    private void Execute(InstructionType type, JsonElement[] a)
    {
        if (a.Length == 0 && type != InstructionType.Invalid) return;
        var targets = SelectTargets(ReadInt(a[0]));

        switch (type)
        {
            case InstructionType.TriggerUpdate when a.Length >= 3:
            {
                bool left = ReadInt(a[1]) == 1;
                var mode = (TriggerMode)ReadInt(a[2]);
                var extra = a.Skip(3).Select(ReadInt).ToArray();
                var effect = BuildModEffect(mode, extra);
                foreach (var s in targets) s.ModSetTrigger(left, effect);
                break;
            }
            case InstructionType.TriggerThreshold when a.Length >= 3:
            {
                bool left = ReadInt(a[1]) == 1;
                foreach (var s in targets) s.ModSetThreshold(left, Math.Clamp(ReadInt(a[2]), 0, 255));
                break;
            }
            case InstructionType.RGBUpdate when a.Length >= 4:
            {
                byte r = (byte)Math.Clamp(ReadInt(a[1]), 0, 255);
                byte g = (byte)Math.Clamp(ReadInt(a[2]), 0, 255);
                byte b = (byte)Math.Clamp(ReadInt(a[3]), 0, 255);
                if (a.Length >= 5)
                {
                    double k = Math.Clamp(ReadInt(a[4]), 0, 255) / 255.0;
                    r = (byte)(r * k); g = (byte)(g * k); b = (byte)(b * k);
                }
                foreach (var s in targets) s.ModSetColor(r, g, b);
                break;
            }
            case InstructionType.PlayerLED when a.Length >= 6:
            {
                byte mask = 0;
                for (int i = 0; i < 5; i++) if (ReadBool(a[1 + i])) mask |= (byte)(1 << i);
                foreach (var s in targets) s.ModSetPlayerLeds(mask);
                break;
            }
            case InstructionType.PlayerLEDNewRevision when a.Length >= 2:
            {
                int v = ReadInt(a[1]); // One=0 .. Five=4, AllOff=5
                byte mask = v is >= 0 and <= 4 ? OutputState.PlayerPattern(v + 1) : (byte)0;
                foreach (var s in targets) s.ModSetPlayerLeds(mask);
                break;
            }
            case InstructionType.MicLED when a.Length >= 2:
            {
                var mode = ReadInt(a[1]) switch { 0 => MicLedMode.On, 1 => MicLedMode.Pulse, _ => MicLedMode.Off };
                foreach (var s in targets) s.ModSetMicLed(mode);
                break;
            }
            case InstructionType.ResetToUserSettings:
                foreach (var s in targets) s.ResetModOverrides();
                break;
        }
    }

    /// <summary>Maps a mod trigger request (mode + extra ints) to an effect.</summary>
    public static TriggerEffect BuildModEffect(TriggerMode mode, int[] x)
    {
        int Arg(int i, int def) => i < x.Length ? x[i] : def;
        var s = new TriggerSettings { Mode = mode };
        switch (mode)
        {
            case TriggerMode.CustomTriggerValue:
                return TriggerEffect.Raw(CustomRaw(Arg(0, 0), x.Skip(1).Take(7).ToArray()));
            case TriggerMode.Resistance:
            case TriggerMode.Feedback:
                s.Start = Arg(0, 0); s.Strength = Arg(1, 8); break;
            case TriggerMode.Bow:
                s.Start = Arg(0, 0); s.End = Arg(1, 8); s.Strength = Arg(2, 8); s.SnapForce = Arg(3, 8); break;
            case TriggerMode.Galloping:
                s.Start = Arg(0, 0); s.End = Arg(1, 9); s.FirstFoot = Arg(2, 2); s.SecondFoot = Arg(3, 6); s.Frequency = Arg(4, 10); break;
            case TriggerMode.SemiAutomaticGun:
            case TriggerMode.Weapon:
                s.Start = Arg(0, 2); s.End = Arg(1, 7); s.Strength = Arg(2, 8); break;
            case TriggerMode.AutomaticGun:
            case TriggerMode.Vibration:
                s.Start = Arg(0, 0); s.Strength = Arg(1, 8); s.Frequency = Arg(2, 10); break;
            case TriggerMode.Machine:
                s.Start = Arg(0, 0); s.End = Arg(1, 9); s.Strength = Arg(2, 7); s.StrengthB = Arg(3, 7); s.Frequency = Arg(4, 5); s.Period = Arg(5, 3); break;
            case TriggerMode.VibrateTrigger:
            case TriggerMode.VibrateTriggerPulse:
                s.Frequency = Arg(0, 20); break;
            case TriggerMode.SlopeFeedback:
                s.Start = Arg(0, 0); s.End = Arg(1, 9); s.Strength = Arg(2, 1); s.StrengthB = Arg(3, 8); break;
            case TriggerMode.MultiplePositionFeedback:
                s.Zones = Enumerable.Range(0, 10).Select(i => Arg(i, 0)).ToArray(); break;
        }
        return s.ToEffect();
    }

    private static byte[] CustomRaw(int customMode, int[] values)
    {
        // Legacy custom modes: 0 Off, 1 Rigid, 2 RigidA, 3 RigidB, 4 RigidAB,
        // 5 Pulse, 6 PulseA, 7 PulseB, 8 PulseAB.
        byte mode = customMode switch
        {
            1 => 0x01, 2 => 0x21, 3 => 0x05, 4 => 0x25,
            5 => 0x02, 6 => 0x22, 7 => 0x06, 8 => 0x26,
            _ => 0x00,
        };
        var raw = new byte[TriggerEffect.Size];
        raw[0] = mode;
        for (int i = 0; i < values.Length && i < 7; i++) raw[1 + i] = (byte)Math.Clamp(values[i], 0, 255);
        return raw;
    }

    private IReadOnlyList<ControllerSession> SelectTargets(int index)
    {
        var all = _sessions();
        var match = all.Where(s => s.Index == index).ToList();
        // Mods usually address controller 0; fall back to every controller if the index is unknown.
        return match.Count > 0 ? match : all;
    }

    public string BuildResponse()
    {
        var sessions = _sessions();
        var first = sessions.FirstOrDefault();
        var response = new
        {
            Status = "DSX Received UDP Instructions",
            TimeReceived = DateTime.Now.ToString("O"),
            isControllerConnected = sessions.Count > 0,
            BatteryLevel = first?.Device.Input.BatteryPercent ?? 0,
            Devices = sessions.Select(s => new
            {
                Index = s.Index,
                MacAddress = s.Device.Serial,
                DeviceType = s.Device.Model == ControllerModel.DualSenseEdge ? 1 : 0,
                ConnectionType = s.Device.Connection == ConnectionType.Bluetooth ? 1 : 0,
                BatteryLevel = s.Device.Input.BatteryPercent,
                IsSupportAT = true,
                IsSupportLightBar = true,
                IsSupportPlayerLED = true,
                IsSupportMicLED = true,
            }).ToArray(),
        };
        return JsonSerializer.Serialize(response);
    }

    private static bool TryGetProperty(JsonElement e, string name, out JsonElement value)
    {
        if (e.ValueKind == JsonValueKind.Object)
            foreach (var p in e.EnumerateObject())
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) { value = p.Value; return true; }
        value = default;
        return false;
    }

    private static int ReadInt(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Number => e.TryGetInt32(out var i) ? i : (int)e.GetDouble(),
        JsonValueKind.True => 1,
        JsonValueKind.False => 0,
        JsonValueKind.String => int.TryParse(e.GetString(), out var s) ? s : 0,
        _ => 0,
    };

    private static bool ReadBool(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.Number => e.GetDouble() != 0,
        JsonValueKind.String => bool.TryParse(e.GetString(), out var b) && b,
        _ => false,
    };

    public void Dispose() => Stop();
}
