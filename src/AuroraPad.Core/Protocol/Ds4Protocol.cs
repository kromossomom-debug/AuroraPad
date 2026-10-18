using System.Buffers.Binary;

namespace AuroraPad.Core.Protocol;

/// <summary>
/// DualShock 4 input/output reports (publicly documented format).
///   USB input 0x01: payload at byte 1. BT input 0x11: payload at byte 3.
///   USB output 0x05 (32 bytes). BT output 0x11 (78 bytes, CRC-32 seeded with 0xA2).
/// The 63-byte payload layout is the same one ViGEm's extended DS4 report uses.
/// </summary>
public static class Ds4Protocol
{
    public const int TouchpadWidth = 1920;
    public const int TouchpadHeight = 942;

    public static DualSenseInput? ParseInput(ReadOnlySpan<byte> r, bool bluetooth)
    {
        int o;
        if (r.Length >= 64 && r[0] == 0x01 && !bluetooth) o = 1;
        else if (r.Length >= 66 && r[0] == 0x11) o = 3;
        else if (r.Length >= 10 && r[0] == 0x01) return ParseSimple(r); // BT before full mode
        else return null;

        var p = r[o..];
        var buttons = DecodeButtons(p[4], p[5], p[6], out var dpad);
        byte status = p[29];
        bool cable = (status & 0x10) != 0;
        int level = status & 0x0F;
        int percent = cable ? Math.Min(level, 10) * 10 : Math.Min(level, 8) * 100 / 8;
        var charging = cable ? (level >= 11 ? ChargingState.Full : ChargingState.Charging) : ChargingState.Discharging;

        return new DualSenseInput
        {
            LeftX = p[0], LeftY = p[1], RightX = p[2], RightY = p[3],
            Buttons = buttons, DPad = dpad,
            L2 = p[7], R2 = p[8],
            SensorTimestamp = BinaryPrimitives.ReadUInt16LittleEndian(p[9..]),
            GyroX = BinaryPrimitives.ReadInt16LittleEndian(p[12..]),
            GyroY = BinaryPrimitives.ReadInt16LittleEndian(p[14..]),
            GyroZ = BinaryPrimitives.ReadInt16LittleEndian(p[16..]),
            AccelX = BinaryPrimitives.ReadInt16LittleEndian(p[18..]),
            AccelY = BinaryPrimitives.ReadInt16LittleEndian(p[20..]),
            AccelZ = BinaryPrimitives.ReadInt16LittleEndian(p[22..]),
            // DS4 touchpad is 1920x942; scale Y to the DualSense range (1080) so downstream code is uniform.
            Touch1 = DecodeTouch(p[34..38]),
            Touch2 = DecodeTouch(p[38..42]),
            BatteryPercent = charging == ChargingState.Full ? 100 : Math.Clamp(percent, 0, 100),
            Charging = charging,
        };
    }

    private static DualSenseInput ParseSimple(ReadOnlySpan<byte> r)
    {
        var buttons = DecodeButtons(r[5], r[6], r[7], out var dpad);
        return new DualSenseInput
        {
            LeftX = r[1], LeftY = r[2], RightX = r[3], RightY = r[4],
            Buttons = buttons, DPad = dpad, L2 = r[8], R2 = r[9],
        };
    }

    private static DualSenseButtons DecodeButtons(byte b0, byte b1, byte b2, out DPad dpad)
    {
        var b = DualSenseButtons.None;
        dpad = (DPad)Math.Min(b0 & 0x0F, 8);
        if ((b0 & 0x10) != 0) b |= DualSenseButtons.Square;
        if ((b0 & 0x20) != 0) b |= DualSenseButtons.Cross;
        if ((b0 & 0x40) != 0) b |= DualSenseButtons.Circle;
        if ((b0 & 0x80) != 0) b |= DualSenseButtons.Triangle;
        if ((b1 & 0x01) != 0) b |= DualSenseButtons.L1;
        if ((b1 & 0x02) != 0) b |= DualSenseButtons.R1;
        if ((b1 & 0x04) != 0) b |= DualSenseButtons.L2;
        if ((b1 & 0x08) != 0) b |= DualSenseButtons.R2;
        if ((b1 & 0x10) != 0) b |= DualSenseButtons.Create; // Share
        if ((b1 & 0x20) != 0) b |= DualSenseButtons.Options;
        if ((b1 & 0x40) != 0) b |= DualSenseButtons.L3;
        if ((b1 & 0x80) != 0) b |= DualSenseButtons.R3;
        if ((b2 & 0x01) != 0) b |= DualSenseButtons.PS;
        if ((b2 & 0x02) != 0) b |= DualSenseButtons.Touchpad;
        if (dpad is DPad.Up or DPad.UpLeft or DPad.UpRight) b |= DualSenseButtons.DPadUp;
        if (dpad is DPad.Down or DPad.DownLeft or DPad.DownRight) b |= DualSenseButtons.DPadDown;
        if (dpad is DPad.Left or DPad.UpLeft or DPad.DownLeft) b |= DualSenseButtons.DPadLeft;
        if (dpad is DPad.Right or DPad.UpRight or DPad.DownRight) b |= DualSenseButtons.DPadRight;
        return b;
    }

    private static TouchPoint DecodeTouch(ReadOnlySpan<byte> t)
    {
        bool active = (t[0] & 0x80) == 0;
        ushort x = (ushort)(t[1] | ((t[2] & 0x0F) << 8));
        ushort y = (ushort)((t[2] >> 4) | (t[3] << 4));
        y = (ushort)Math.Min(1079, y * 1080 / TouchpadHeight);
        return new TouchPoint(active, (byte)(t[0] & 0x7F), x, y);
    }

    public static byte[] BuildOutput(OutputState s, bool bluetooth)
    {
        if (!bluetooth)
        {
            var r = new byte[32];
            r[0] = 0x05;
            r[1] = 0x07; // rumble + lightbar + flash
            r[2] = 0x04;
            r[4] = s.RumbleRight; // small motor
            r[5] = s.RumbleLeft;  // large motor
            r[6] = s.LightbarR; r[7] = s.LightbarG; r[8] = s.LightbarB;
            return r;
        }
        var b = new byte[78];
        b[0] = 0x11;
        b[1] = 0xC0; // HID + CRC, max poll rate
        b[2] = 0x20;
        b[3] = 0x07;
        b[4] = 0x04;
        b[6] = s.RumbleRight;
        b[7] = s.RumbleLeft;
        b[8] = s.LightbarR; b[9] = s.LightbarG; b[10] = s.LightbarB;
        uint crc = Crc32.Compute(0xA2, b.AsSpan(0, 74));
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(74), crc);
        return b;
    }

    /// <summary>
    /// Builds the 63-byte DS4 input payload (USB report without the id byte) used for ViGEm's
    /// raw/extended DS4 report: buttons, sticks, triggers, motion and touch.
    /// </summary>
    public static byte[] BuildVirtualReport(in Processing.PadState pad, DualSenseInput raw, byte counter, ushort timestamp)
    {
        var p = new byte[63];
        static byte Axis(double v, bool invert) => (byte)Math.Clamp(Math.Round(128 + (invert ? -v : v) * 127.5), 0, 255);
        p[0] = Axis(pad.LeftX, false); p[1] = Axis(pad.LeftY, true);
        p[2] = Axis(pad.RightX, false); p[3] = Axis(pad.RightY, true);

        int dpad = (pad.Up, pad.Down, pad.Left, pad.Right) switch
        {
            (true, _, true, _) => 7, (true, _, _, true) => 1, (_, true, true, _) => 5, (_, true, _, true) => 3,
            (true, _, _, _) => 0, (_, true, _, _) => 4, (_, _, true, _) => 6, (_, _, _, true) => 2, _ => 8,
        };
        int w = dpad;
        if (pad.Has(DualSenseButtons.Square)) w |= 0x10;
        if (pad.Has(DualSenseButtons.Cross)) w |= 0x20;
        if (pad.Has(DualSenseButtons.Circle)) w |= 0x40;
        if (pad.Has(DualSenseButtons.Triangle)) w |= 0x80;
        if (pad.Has(DualSenseButtons.L1)) w |= 0x100;
        if (pad.Has(DualSenseButtons.R1)) w |= 0x200;
        if (pad.L2 > 0) w |= 0x400;
        if (pad.R2 > 0) w |= 0x800;
        if (pad.Has(DualSenseButtons.Create)) w |= 0x1000;
        if (pad.Has(DualSenseButtons.Options)) w |= 0x2000;
        if (pad.Has(DualSenseButtons.L3)) w |= 0x4000;
        if (pad.Has(DualSenseButtons.R3)) w |= 0x8000;
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(4), (ushort)w);
        p[6] = (byte)((pad.Has(DualSenseButtons.PS) ? 1 : 0) | (pad.Has(DualSenseButtons.Touchpad) ? 2 : 0) | ((counter & 0x3F) << 2));
        p[7] = pad.L2; p[8] = pad.R2;
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(9), timestamp);
        BinaryPrimitives.WriteInt16LittleEndian(p.AsSpan(12), raw.GyroX);
        BinaryPrimitives.WriteInt16LittleEndian(p.AsSpan(14), raw.GyroY);
        BinaryPrimitives.WriteInt16LittleEndian(p.AsSpan(16), raw.GyroZ);
        BinaryPrimitives.WriteInt16LittleEndian(p.AsSpan(18), raw.AccelX);
        BinaryPrimitives.WriteInt16LittleEndian(p.AsSpan(20), raw.AccelY);
        BinaryPrimitives.WriteInt16LittleEndian(p.AsSpan(22), raw.AccelZ);
        // Battery: cable flag + level so games show a sensible value.
        p[29] = raw.Charging is ChargingState.Charging or ChargingState.Full
            ? (byte)(0x10 | Math.Min(11, raw.BatteryPercent / 10))
            : (byte)Math.Min(8, raw.BatteryPercent * 8 / 100);
        p[32] = 1; // one touch packet
        p[33] = counter;
        WriteTouch(p.AsSpan(34, 4), raw.Touch1);
        WriteTouch(p.AsSpan(38, 4), raw.Touch2);
        return p;
    }

    private static void WriteTouch(Span<byte> t, TouchPoint tp)
    {
        ushort x = (ushort)Math.Min(TouchpadWidth - 1, (int)tp.X);
        ushort y = (ushort)Math.Min(TouchpadHeight - 1, tp.Y * TouchpadHeight / 1080);
        t[0] = (byte)((tp.Active ? 0 : 0x80) | (tp.Id & 0x7F));
        t[1] = (byte)(x & 0xFF);
        t[2] = (byte)(((x >> 8) & 0x0F) | ((y & 0x0F) << 4));
        t[3] = (byte)(y >> 4);
    }
}
