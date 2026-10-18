using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using AuroraPad.Core.Dsu;

/// <summary>Acts like an emulator: talks to a running DSU server and validates its replies.</summary>
static class DsuClientTest
{
    public static int Run(int port)
    {
        using var udp = new UdpClient(0);
        udp.Client.ReceiveTimeout = 2000;
        var server = new IPEndPoint(IPAddress.Loopback, port);
        const uint clientId = 0x1234ABCD;

        byte[] Req(uint type, params byte[] body)
        {
            var payload = new byte[4 + body.Length];
            BinaryPrimitives.WriteUInt32LittleEndian(payload, type);
            body.CopyTo(payload, 4);
            return DsuServer.BuildPacket(payload, clientId, fromServer: false);
        }

        byte[] Receive()
        {
            var ep = new IPEndPoint(IPAddress.Any, 0);
            var r = udp.Receive(ref ep);
            if (r[0] != 'D' || r[3] != 'S') throw new Exception("bad magic");
            uint crc = BinaryPrimitives.ReadUInt32LittleEndian(r.AsSpan(8));
            var copy = (byte[])r.Clone();
            copy[8] = copy[9] = copy[10] = copy[11] = 0;
            var expected = BinaryPrimitives.ReadUInt32LittleEndian(DsuServer.BuildPacket(copy.AsSpan(16), BinaryPrimitives.ReadUInt32LittleEndian(r.AsSpan(12))).AsSpan(8));
            if (crc != expected) throw new Exception("bad CRC");
            return r;
        }

        // 1. protocol version
        udp.Send(Req(0x100000), server);
        var v = Receive();
        Console.WriteLine($"Version reply: type=0x{BinaryPrimitives.ReadUInt32LittleEndian(v.AsSpan(16)):X} protocol={BinaryPrimitives.ReadUInt16LittleEndian(v.AsSpan(20))}");

        // 2. controller info for slot 0 and 1
        udp.Send(Req(0x100001, 2, 0, 0, 0, 0, 1), server);
        for (int i = 0; i < 2; i++)
        {
            var info = Receive();
            Console.WriteLine($"Slot {info[20]}: state={info[21]} model={info[22]} conn={info[23]} mac={Convert.ToHexString(info, 24, 6)} battery=0x{info[30]:X2} len={info.Length}");
        }

        // 3. subscribe to all pads and collect for one second
        udp.Send(Req(0x100002, 0, 0, 0, 0, 0, 0, 0, 0), server);
        int packets = 0;
        byte[]? last = null;
        var until = DateTime.UtcNow.AddSeconds(1);
        while (DateTime.UtcNow < until)
        {
            try { last = Receive(); packets++; } catch (SocketException) { break; }
        }
        Console.WriteLine($"Pad packets in 1s: {packets}");
        if (last == null) return 1;
        var p = last.AsSpan(16);
        Console.WriteLine($"Pad packet length={last.Length} connected={p[15]} counter={BinaryPrimitives.ReadUInt32LittleEndian(p[16..])}");
        Console.WriteLine($"Sticks L=({p[24]},{p[25]}) R=({p[26]},{p[27]}) L2={p[39]} R2={p[38]}");
        Console.WriteLine($"Accel g=({BinaryPrimitives.ReadSingleLittleEndian(p[60..]):F2},{BinaryPrimitives.ReadSingleLittleEndian(p[64..]):F2},{BinaryPrimitives.ReadSingleLittleEndian(p[68..]):F2}) " +
                          $"Gyro dps=({BinaryPrimitives.ReadSingleLittleEndian(p[72..]):F1},{BinaryPrimitives.ReadSingleLittleEndian(p[76..]):F1},{BinaryPrimitives.ReadSingleLittleEndian(p[80..]):F1})");
        return packets > 0 && last.Length == 100 ? 0 : 1;
    }
}
