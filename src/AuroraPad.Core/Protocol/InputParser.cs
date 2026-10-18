using System.Buffers.Binary;

namespace AuroraPad.Core.Protocol;

/// <summary>
/// Decodes DualSense input reports. Layout follows the publicly documented
/// format used by the Linux hid-playstation driver:
///   USB: report 0x01, payload starts at byte 1 (64 bytes total).
///   BT : report 0x31, payload starts at byte 2 (78 bytes total).
/// A BT report 0x01 is the reduced "simple" report sent before the controller
/// is switched to full mode; it only carries sticks, buttons and triggers.
/// </summary>
public static class InputParser
{
    public static DualSenseInput? Parse(ReadOnlySpan<byte> report, bool isBluetooth)
    {
        if (report.Length < 10) return null;

        if (isBluetooth && report[0] == 0x01)
            return ParseSimpleBluetooth(report);

        int o;
        if (report[0] == 0x01 && !isBluetooth) o = 1;
        else if (report[0] == 0x31 && report.Length >= 64) o = 2;
        else return null;

        if (report.Length < o + 53) return null;
        var p = report[o..];

        byte b0 = p[7], b1 = p[8], b2 = p[9];
        var buttons = DecodeFaceAndShoulder(b0, b1);
        if ((b2 & 0x01) != 0) buttons |= DualSenseButtons.PS;
        if ((b2 & 0x02) != 0) buttons |= DualSenseButtons.Touchpad;
        if ((b2 & 0x04) != 0) buttons |= DualSenseButtons.Mute;
        if ((b2 & 0x10) != 0) buttons |= DualSenseButtons.LeftFn;
        if ((b2 & 0x20) != 0) buttons |= DualSenseButtons.RightFn;
        if ((b2 & 0x40) != 0) buttons |= DualSenseButtons.LeftPaddle;
        if ((b2 & 0x80) != 0) buttons |= DualSenseButtons.RightPaddle;

        byte status = p[52];
        int capacity = status & 0x0F;
        var charging = (status >> 4) switch
        {
            0 => ChargingState.Discharging,
            1 => ChargingState.Charging,
            2 => ChargingState.Full,
            _ => ChargingState.Unknown,
        };
        int battery = charging == ChargingState.Full ? 100 : Math.Min(100, capacity * 10 + 5);

        return new DualSenseInput
        {
            LeftX = p[0], LeftY = p[1], RightX = p[2], RightY = p[3],
            L2 = p[4], R2 = p[5],
            DPad = (DPad)Math.Min(b0 & 0x0F, 8),
            Buttons = buttons | DPadFlags((DPad)Math.Min(b0 & 0x0F, 8)),
            GyroX = BinaryPrimitives.ReadInt16LittleEndian(p[15..]),
            GyroY = BinaryPrimitives.ReadInt16LittleEndian(p[17..]),
            GyroZ = BinaryPrimitives.ReadInt16LittleEndian(p[19..]),
            AccelX = BinaryPrimitives.ReadInt16LittleEndian(p[21..]),
            AccelY = BinaryPrimitives.ReadInt16LittleEndian(p[23..]),
            AccelZ = BinaryPrimitives.ReadInt16LittleEndian(p[25..]),
            SensorTimestamp = BinaryPrimitives.ReadUInt32LittleEndian(p[27..]),
            Touch1 = DecodeTouch(p[32..36]),
            Touch2 = DecodeTouch(p[36..40]),
            BatteryPercent = battery,
            Charging = charging,
        };
    }

    private static DualSenseInput ParseSimpleBluetooth(ReadOnlySpan<byte> r)
    {
        // Simple BT report: id, lx, ly, rx, ry, buttons0, buttons1, buttons2, l2, r2
        var buttons = DecodeFaceAndShoulder(r[5], r[6]);
        if ((r[7] & 0x01) != 0) buttons |= DualSenseButtons.PS;
        if ((r[7] & 0x02) != 0) buttons |= DualSenseButtons.Touchpad;
        return new DualSenseInput
        {
            LeftX = r[1], LeftY = r[2], RightX = r[3], RightY = r[4],
            DPad = (DPad)Math.Min(r[5] & 0x0F, 8),
            Buttons = buttons | DPadFlags((DPad)Math.Min(r[5] & 0x0F, 8)),
            L2 = r[8], R2 = r[9],
        };
    }

    private static DualSenseButtons DPadFlags(DPad d)
    {
        var b = DualSenseButtons.None;
        if (d is DPad.Up or DPad.UpLeft or DPad.UpRight) b |= DualSenseButtons.DPadUp;
        if (d is DPad.Down or DPad.DownLeft or DPad.DownRight) b |= DualSenseButtons.DPadDown;
        if (d is DPad.Left or DPad.UpLeft or DPad.DownLeft) b |= DualSenseButtons.DPadLeft;
        if (d is DPad.Right or DPad.UpRight or DPad.DownRight) b |= DualSenseButtons.DPadRight;
        return b;
    }

    private static DualSenseButtons DecodeFaceAndShoulder(byte b0, byte b1)
    {
        var b = DualSenseButtons.None;
        if ((b0 & 0x10) != 0) b |= DualSenseButtons.Square;
        if ((b0 & 0x20) != 0) b |= DualSenseButtons.Cross;
        if ((b0 & 0x40) != 0) b |= DualSenseButtons.Circle;
        if ((b0 & 0x80) != 0) b |= DualSenseButtons.Triangle;
        if ((b1 & 0x01) != 0) b |= DualSenseButtons.L1;
        if ((b1 & 0x02) != 0) b |= DualSenseButtons.R1;
        if ((b1 & 0x04) != 0) b |= DualSenseButtons.L2;
        if ((b1 & 0x08) != 0) b |= DualSenseButtons.R2;
        if ((b1 & 0x10) != 0) b |= DualSenseButtons.Create;
        if ((b1 & 0x20) != 0) b |= DualSenseButtons.Options;
        if ((b1 & 0x40) != 0) b |= DualSenseButtons.L3;
        if ((b1 & 0x80) != 0) b |= DualSenseButtons.R3;
        return b;
    }

    private static TouchPoint DecodeTouch(ReadOnlySpan<byte> t)
    {
        bool active = (t[0] & 0x80) == 0;
        byte id = (byte)(t[0] & 0x7F);
        ushort x = (ushort)(t[1] | ((t[2] & 0x0F) << 8));
        ushort y = (ushort)((t[2] >> 4) | (t[3] << 4));
        return new TouchPoint(active, id, x, y);
    }
}
