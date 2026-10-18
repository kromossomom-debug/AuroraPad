namespace AuroraPad.Core.Protocol;

/// <summary>Standard reflected CRC-32 (poly 0xEDB88320) used by Bluetooth output reports.</summary>
internal static class Crc32
{
    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var t = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[i] = c;
        }
        return t;
    }

    public static uint Compute(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (var b in data)
            crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return ~crc;
    }

    public static uint Compute(byte seed, ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFFu;
        crc = Table[(crc ^ seed) & 0xFF] ^ (crc >> 8);
        foreach (var b in data)
            crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return ~crc;
    }
}
