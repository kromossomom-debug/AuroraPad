using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using AuroraPad.Core.Protocol;

namespace AuroraPad.Core.Osc;

/// <summary>
/// Streams controller state as OSC 1.0 messages over UDP, e.g. to VRChat, TouchDesigner or
/// Resolume. Addresses: /dsx/{n}/lx ly rx ry l2 r2 (floats), /dsx/{n}/button/{name} (int 0/1),
/// /dsx/{n}/gyro and /dsx/{n}/accel (3 floats), /dsx/{n}/touch1 /touch2 (int active, float x, y),
/// /dsx/{n}/battery (int).
/// </summary>
public sealed class OscSender : IDisposable
{
    private static readonly (DualSenseButtons Button, string Name)[] ButtonNames =
    [
        (DualSenseButtons.Cross, "cross"), (DualSenseButtons.Circle, "circle"), (DualSenseButtons.Square, "square"),
        (DualSenseButtons.Triangle, "triangle"), (DualSenseButtons.L1, "l1"), (DualSenseButtons.R1, "r1"),
        (DualSenseButtons.L3, "l3"), (DualSenseButtons.R3, "r3"), (DualSenseButtons.Create, "create"),
        (DualSenseButtons.Options, "options"), (DualSenseButtons.PS, "ps"), (DualSenseButtons.Touchpad, "touchpad"),
        (DualSenseButtons.Mute, "mute"), (DualSenseButtons.DPadUp, "up"), (DualSenseButtons.DPadDown, "down"),
        (DualSenseButtons.DPadLeft, "left"), (DualSenseButtons.DPadRight, "right"),
    ];

    private readonly UdpClient _udp = new();
    private readonly Dictionary<int, DualSenseButtons> _lastButtons = [];
    private readonly Dictionary<int, long> _lastSend = [];
    private IPEndPoint? _target;

    public bool IsRunning => _target != null;
    public long MessagesSent { get; private set; }

    /// <summary>Maximum analog/motion update rate per controller.</summary>
    public int RateHz { get; set; } = 60;

    public void Start(string address, int port) =>
        _target = new IPEndPoint(IPAddress.TryParse(address, out var ip) ? ip : IPAddress.Loopback, port);

    public void Stop() => _target = null;

    public void Attach(ControllerSession session) =>
        session.Device.InputReceived += (_, input) => Publish(session.Index, input);

    private void Publish(int index, DualSenseInput i)
    {
        var target = _target;
        if (target == null) return;
        var prefix = $"/dsx/{index}";

        // Buttons are sent on change so no press is lost to rate limiting.
        lock (_lastButtons)
        {
            _lastButtons.TryGetValue(index, out var prev);
            var changed = prev ^ i.Buttons;
            foreach (var (b, name) in ButtonNames)
                if ((changed & b) != 0) Send(target, $"{prefix}/button/{name}", (i.Buttons & b) != 0 ? 1 : 0);
            _lastButtons[index] = i.Buttons;

            long now = Environment.TickCount64;
            if (_lastSend.TryGetValue(index, out var last) && now - last < 1000 / Math.Max(1, RateHz)) return;
            _lastSend[index] = now;
        }

        Send(target, $"{prefix}/lx", (i.LeftX - 127.5f) / 127.5f);
        Send(target, $"{prefix}/ly", -(i.LeftY - 127.5f) / 127.5f);
        Send(target, $"{prefix}/rx", (i.RightX - 127.5f) / 127.5f);
        Send(target, $"{prefix}/ry", -(i.RightY - 127.5f) / 127.5f);
        Send(target, $"{prefix}/l2", i.L2 / 255f);
        Send(target, $"{prefix}/r2", i.R2 / 255f);
        Send(target, $"{prefix}/gyro", i.GyroX / 16f, i.GyroY / 16f, i.GyroZ / 16f);
        Send(target, $"{prefix}/accel", i.AccelX / 8192f, i.AccelY / 8192f, i.AccelZ / 8192f);
        SendTouch(target, $"{prefix}/touch1", i.Touch1);
        SendTouch(target, $"{prefix}/touch2", i.Touch2);
        Send(target, $"{prefix}/battery", i.BatteryPercent);
    }

    private void SendTouch(IPEndPoint t, string address, TouchPoint p) =>
        SendMessage(t, Encode(address, [p.Active ? 1 : 0, p.X / 1919f, p.Y / 1079f]));

    private void Send(IPEndPoint t, string address, params object[] args) => SendMessage(t, Encode(address, args));

    private void SendMessage(IPEndPoint t, byte[] msg)
    {
        try { _udp.Send(msg, msg.Length, t); MessagesSent++; } catch { }
    }

    /// <summary>Encodes one OSC message with int32 ('i') and float32 ('f') arguments.</summary>
    public static byte[] Encode(string address, object[] args)
    {
        var tags = new StringBuilder(",");
        foreach (var a in args) tags.Append(a is int ? 'i' : 'f');
        var ms = new MemoryStream();
        WritePadded(ms, address);
        WritePadded(ms, tags.ToString());
        Span<byte> b = stackalloc byte[4];
        foreach (var a in args)
        {
            if (a is int iv) BinaryPrimitives.WriteInt32BigEndian(b, iv);
            else BinaryPrimitives.WriteSingleBigEndian(b, Convert.ToSingle(a));
            ms.Write(b);
        }
        return ms.ToArray();
    }

    private static void WritePadded(Stream s, string text)
    {
        var bytes = Encoding.ASCII.GetBytes(text);
        s.Write(bytes);
        int pad = 4 - bytes.Length % 4; // always at least one null terminator
        for (int i = 0; i < pad; i++) s.WriteByte(0);
    }

    public void Dispose() => _udp.Dispose();
}
