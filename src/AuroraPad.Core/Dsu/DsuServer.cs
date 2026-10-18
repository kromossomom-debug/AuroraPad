using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using AuroraPad.Core.Devices;
using AuroraPad.Core.Protocol;

namespace AuroraPad.Core.Dsu;

/// <summary>
/// Cemuhook / DSU motion server (default UDP 26760). Emulators such as Cemu, Yuzu forks,
/// Dolphin and Citra connect to it to receive buttons, sticks, touch and gyro/accelerometer data.
/// Implements the publicly documented DSU protocol version 1001.
/// </summary>
public sealed class DsuServer : IDisposable
{
    private const ushort ProtocolVersion = 1001;
    private const uint MsgVersion = 0x100000;
    private const uint MsgPorts = 0x100001;
    private const uint MsgPadData = 0x100002;
    private static readonly TimeSpan ClientTimeout = TimeSpan.FromSeconds(5);

    private sealed class Client
    {
        public required IPEndPoint EndPoint;
        public DateTime LastSeen;
        public bool AllSlots;
        public readonly HashSet<int> Slots = [];
    }

    private readonly Func<IReadOnlyList<ControllerSession>> _sessions;
    private readonly Dictionary<IPEndPoint, Client> _clients = [];
    private readonly object _lock = new();
    private readonly uint _serverId = (uint)Random.Shared.Next();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private UdpClient? _udp;
    private CancellationTokenSource? _cts;
    private uint _packetCounter;

    public bool IsRunning => _udp != null;
    public string? LastError { get; private set; }
    public int ClientCount { get { lock (_lock) { Prune(); return _clients.Count; } } }

    public DsuServer(Func<IReadOnlyList<ControllerSession>> sessions) => _sessions = sessions;

    public bool Start(int port)
    {
        Stop();
        try
        {
            _udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
            const int SioUdpConnReset = -1744830452;
            try { _udp.Client.IOControl(SioUdpConnReset, [0, 0, 0, 0], null); } catch { }
            _cts = new CancellationTokenSource();
            _ = Task.Run(() => ReceiveLoop(_cts.Token));
            LastError = null;
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            _udp = null;
            return false;
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
        _udp?.Dispose();
        _udp = null;
        lock (_lock) _clients.Clear();
    }

    /// <summary>Hook a controller so every input report is streamed to subscribed clients.</summary>
    public void Attach(ControllerSession session) =>
        session.Device.InputReceived += (_, input) => Publish(session, input);

    private async Task ReceiveLoop(CancellationToken ct)
    {
        var udp = _udp!;
        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult r;
            try { r = await udp.ReceiveAsync(ct); }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) { continue; }
            try { HandleRequest(r.Buffer, r.RemoteEndPoint); } catch { /* malformed packet */ }
        }
    }

    private void HandleRequest(byte[] p, IPEndPoint from)
    {
        if (p.Length < 20 || p[0] != 'D' || p[1] != 'S' || p[2] != 'U' || p[3] != 'C') return;
        int length = BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(6));
        if (p.Length < 16 + length) return;

        uint crc = BinaryPrimitives.ReadUInt32LittleEndian(p.AsSpan(8));
        var check = (byte[])p.Clone();
        check[8] = check[9] = check[10] = check[11] = 0;
        if (Crc32.Compute(check.AsSpan(0, 16 + length)) != crc) return;

        uint type = BinaryPrimitives.ReadUInt32LittleEndian(p.AsSpan(16));
        switch (type)
        {
            case MsgVersion:
            {
                var payload = new byte[6];
                BinaryPrimitives.WriteUInt32LittleEndian(payload, MsgVersion);
                BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(4), ProtocolVersion);
                Send(payload, from);
                break;
            }
            case MsgPorts:
            {
                int count = BinaryPrimitives.ReadInt32LittleEndian(p.AsSpan(20));
                for (int i = 0; i < count && 24 + i < p.Length; i++)
                {
                    int slot = p[24 + i];
                    if (slot > 3) continue;
                    var payload = new byte[16];
                    BinaryPrimitives.WriteUInt32LittleEndian(payload, MsgPorts);
                    WriteSharedHeader(payload.AsSpan(4), slot, FindSession(slot));
                    payload[15] = 0;
                    Send(payload, from);
                }
                break;
            }
            case MsgPadData:
            {
                byte flags = p.Length > 20 ? p[20] : (byte)0;
                byte slot = p.Length > 21 ? p[21] : (byte)0;
                lock (_lock)
                {
                    if (!_clients.TryGetValue(from, out var c))
                        _clients[from] = c = new Client { EndPoint = from };
                    c.LastSeen = DateTime.UtcNow;
                    // flags 0 = all controllers, bit 0 = by slot, bit 1 = by MAC (treated as all).
                    if (flags == 0 || (flags & 2) != 0) c.AllSlots = true;
                    else c.Slots.Add(slot);
                }
                break;
            }
        }
    }

    private ControllerSession? FindSession(int slot) => _sessions().FirstOrDefault(s => s.Index == slot);

    private static void WriteSharedHeader(Span<byte> h, int slot, ControllerSession? s)
    {
        h[0] = (byte)slot;
        if (s == null)
        {
            h[1..11].Clear();
            return;
        }
        h[1] = 2; // connected
        h[2] = 2; // full gyro
        h[3] = s.Device.Connection == ConnectionType.Bluetooth ? (byte)2 : (byte)1;
        WriteMac(h[4..10], s.Device.Serial);
        var input = s.Device.Input;
        h[10] = input.Charging switch
        {
            ChargingState.Charging => 0xEE,
            ChargingState.Full => 0xEF,
            _ => input.BatteryPercent switch { < 10 => 0x01, < 30 => 0x02, < 60 => 0x03, < 90 => 0x04, _ => 0x05 },
        };
    }

    private static void WriteMac(Span<byte> mac, string serial)
    {
        // Our serial is a hex id; pad/trim it into 6 bytes so each controller gets a stable "MAC".
        var hex = new string(serial.Where(Uri.IsHexDigit).ToArray()).PadLeft(12, '0');
        for (int i = 0; i < 6; i++) mac[i] = Convert.ToByte(hex.Substring(hex.Length - 12 + i * 2, 2), 16);
    }

    private void Publish(ControllerSession session, DualSenseInput i)
    {
        if (_udp == null) return;
        List<IPEndPoint> targets;
        lock (_lock)
        {
            if (_clients.Count == 0) return;
            Prune();
            targets = _clients.Values.Where(c => c.AllSlots || c.Slots.Contains(session.Index)).Select(c => c.EndPoint).ToList();
        }
        if (targets.Count == 0 || session.Index > 3) return;

        var payload = new byte[84];
        var s = payload.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(s, MsgPadData);
        WriteSharedHeader(s[4..15], session.Index, session);
        s[15] = 1; // connected
        BinaryPrimitives.WriteUInt32LittleEndian(s[16..], Interlocked.Increment(ref _packetCounter));

        byte b1 = 0, b2 = 0;
        if (i.IsPressed(DualSenseButtons.Create)) b1 |= 0x01;
        if (i.IsPressed(DualSenseButtons.L3)) b1 |= 0x02;
        if (i.IsPressed(DualSenseButtons.R3)) b1 |= 0x04;
        if (i.IsPressed(DualSenseButtons.Options)) b1 |= 0x08;
        if (i.DPadUp) b1 |= 0x10;
        if (i.DPadRight) b1 |= 0x20;
        if (i.DPadDown) b1 |= 0x40;
        if (i.DPadLeft) b1 |= 0x80;
        if (i.IsPressed(DualSenseButtons.L2)) b2 |= 0x01;
        if (i.IsPressed(DualSenseButtons.R2)) b2 |= 0x02;
        if (i.IsPressed(DualSenseButtons.L1)) b2 |= 0x04;
        if (i.IsPressed(DualSenseButtons.R1)) b2 |= 0x08;
        if (i.IsPressed(DualSenseButtons.Triangle)) b2 |= 0x10;
        if (i.IsPressed(DualSenseButtons.Circle)) b2 |= 0x20;
        if (i.IsPressed(DualSenseButtons.Cross)) b2 |= 0x40;
        if (i.IsPressed(DualSenseButtons.Square)) b2 |= 0x80;
        s[20] = b1;
        s[21] = b2;
        s[22] = i.IsPressed(DualSenseButtons.PS) ? (byte)1 : (byte)0;
        s[23] = i.IsPressed(DualSenseButtons.Touchpad) ? (byte)1 : (byte)0;
        s[24] = i.LeftX;
        s[25] = (byte)(255 - i.LeftY);  // DSU: plus is up
        s[26] = i.RightX;
        s[27] = (byte)(255 - i.RightY);
        s[28] = i.DPadLeft ? (byte)255 : (byte)0;
        s[29] = i.DPadDown ? (byte)255 : (byte)0;
        s[30] = i.DPadRight ? (byte)255 : (byte)0;
        s[31] = i.DPadUp ? (byte)255 : (byte)0;
        s[32] = i.IsPressed(DualSenseButtons.Triangle) ? (byte)255 : (byte)0;
        s[33] = i.IsPressed(DualSenseButtons.Circle) ? (byte)255 : (byte)0;
        s[34] = i.IsPressed(DualSenseButtons.Cross) ? (byte)255 : (byte)0;
        s[35] = i.IsPressed(DualSenseButtons.Square) ? (byte)255 : (byte)0;
        s[36] = i.IsPressed(DualSenseButtons.R1) ? (byte)255 : (byte)0;
        s[37] = i.IsPressed(DualSenseButtons.L1) ? (byte)255 : (byte)0;
        s[38] = i.R2;
        s[39] = i.L2;
        WriteTouch(s[40..46], i.Touch1);
        WriteTouch(s[46..52], i.Touch2);
        BinaryPrimitives.WriteUInt64LittleEndian(s[52..], (ulong)(_clock.Elapsed.Ticks / 10)); // microseconds

        // Units: accelerometer in g (~8192 per g), gyro in degrees/second (~16 per dps).
        // Axis signs follow the DualShock 4 convention emulators expect.
        WriteFloat(s[60..], -i.AccelX / 8192f);
        WriteFloat(s[64..], -i.AccelY / 8192f);
        WriteFloat(s[68..], i.AccelZ / 8192f);
        WriteFloat(s[72..], i.GyroX / 16f);   // pitch
        WriteFloat(s[76..], -i.GyroY / 16f);  // yaw
        WriteFloat(s[80..], -i.GyroZ / 16f);  // roll

        foreach (var t in targets) Send(payload, t);
    }

    private static void WriteTouch(Span<byte> t, TouchPoint p)
    {
        t[0] = p.Active ? (byte)1 : (byte)0;
        t[1] = p.Id;
        BinaryPrimitives.WriteUInt16LittleEndian(t[2..], p.X);
        BinaryPrimitives.WriteUInt16LittleEndian(t[4..], p.Y);
    }

    private static void WriteFloat(Span<byte> d, float v) => BinaryPrimitives.WriteSingleLittleEndian(d, v);

    /// <summary>Wraps a payload (starting with the message type) in a DSUS header and sends it.</summary>
    private void Send(byte[] payload, IPEndPoint to)
    {
        var udp = _udp;
        if (udp == null) return;
        var packet = BuildPacket(payload, _serverId);
        try { udp.Send(packet, packet.Length, to); } catch { }
    }

    public static byte[] BuildPacket(ReadOnlySpan<byte> payload, uint serverId, bool fromServer = true)
    {
        var packet = new byte[16 + payload.Length];
        packet[0] = (byte)'D'; packet[1] = (byte)'S'; packet[2] = (byte)'U'; packet[3] = fromServer ? (byte)'S' : (byte)'C';
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4), ProtocolVersion);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(6), (ushort)payload.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(12), serverId);
        payload.CopyTo(packet.AsSpan(16));
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(8), Crc32.Compute(packet));
        return packet;
    }

    private void Prune()
    {
        var now = DateTime.UtcNow;
        foreach (var key in _clients.Where(kv => now - kv.Value.LastSeen > ClientTimeout).Select(kv => kv.Key).ToList())
            _clients.Remove(key);
    }

    public void Dispose() => Stop();
}
