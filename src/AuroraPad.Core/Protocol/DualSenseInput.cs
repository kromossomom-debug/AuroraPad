namespace AuroraPad.Core.Protocol;

public enum DPad : byte
{
    Up = 0, UpRight = 1, Right = 2, DownRight = 3,
    Down = 4, DownLeft = 5, Left = 6, UpLeft = 7, None = 8,
}

[Flags]
public enum DualSenseButtons : uint
{
    None = 0,
    Square = 1 << 0,
    Cross = 1 << 1,
    Circle = 1 << 2,
    Triangle = 1 << 3,
    L1 = 1 << 4,
    R1 = 1 << 5,
    L2 = 1 << 6,
    R2 = 1 << 7,
    Create = 1 << 8,
    Options = 1 << 9,
    L3 = 1 << 10,
    R3 = 1 << 11,
    PS = 1 << 12,
    Touchpad = 1 << 13,
    Mute = 1 << 14,
    // DualSense Edge only
    LeftFn = 1 << 15,
    RightFn = 1 << 16,
    LeftPaddle = 1 << 17,
    RightPaddle = 1 << 18,
    // D-pad directions as flags, so they can be remapped like buttons.
    DPadUp = 1 << 19,
    DPadDown = 1 << 20,
    DPadLeft = 1 << 21,
    DPadRight = 1 << 22,
}

public enum ChargingState : byte { Discharging = 0, Charging = 1, Full = 2, Unknown = 0xFF }

public readonly record struct TouchPoint(bool Active, byte Id, ushort X, ushort Y);

/// <summary>Snapshot of one DualSense input report, already decoded.</summary>
public sealed class DualSenseInput
{
    public byte LeftX { get; init; } = 128;
    public byte LeftY { get; init; } = 128;
    public byte RightX { get; init; } = 128;
    public byte RightY { get; init; } = 128;
    public byte L2 { get; init; }
    public byte R2 { get; init; }
    public DPad DPad { get; init; } = DPad.None;
    public DualSenseButtons Buttons { get; init; }

    public short GyroX { get; init; }
    public short GyroY { get; init; }
    public short GyroZ { get; init; }
    public short AccelX { get; init; }
    public short AccelY { get; init; }
    public short AccelZ { get; init; }
    public uint SensorTimestamp { get; init; }

    public TouchPoint Touch1 { get; init; }
    public TouchPoint Touch2 { get; init; }

    /// <summary>Battery level 0-100.</summary>
    public int BatteryPercent { get; init; }
    public ChargingState Charging { get; init; } = ChargingState.Unknown;

    public bool IsPressed(DualSenseButtons b) => (Buttons & b) != 0;

    public bool DPadUp => DPad is DPad.Up or DPad.UpLeft or DPad.UpRight;
    public bool DPadDown => DPad is DPad.Down or DPad.DownLeft or DPad.DownRight;
    public bool DPadLeft => DPad is DPad.Left or DPad.UpLeft or DPad.DownLeft;
    public bool DPadRight => DPad is DPad.Right or DPad.UpRight or DPad.DownRight;

    public static DualSenseInput Neutral { get; } = new();
}
