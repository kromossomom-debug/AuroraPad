namespace AuroraPad.Core.Triggers;

/// <summary>
/// Trigger modes exposed in the UI and accepted over the UDP mod API.
/// Numeric values match the mode numbers used by existing DSX mods so those
/// mods keep working against this app.
/// </summary>
public enum TriggerMode
{
    Normal = 0,
    GameCube = 1,
    VerySoft = 2,
    Soft = 3,
    Hard = 4,
    VeryHard = 5,
    Hardest = 6,
    Rigid = 7,
    VibrateTrigger = 8,
    Choppy = 9,
    Medium = 10,
    VibrateTriggerPulse = 11,
    CustomTriggerValue = 12,
    Resistance = 13,
    Bow = 14,
    Galloping = 15,
    SemiAutomaticGun = 16,
    AutomaticGun = 17,
    Machine = 18,
    VibrateTrigger10Hz = 19,
    Off = 20,
    Feedback = 21,
    Weapon = 22,
    Vibration = 23,
    SlopeFeedback = 24,
    MultiplePositionFeedback = 25,
}

/// <summary>User-editable trigger configuration; converted to a <see cref="TriggerEffect"/>.</summary>
public sealed class TriggerSettings
{
    public TriggerMode Mode { get; set; } = TriggerMode.Normal;
    public int Start { get; set; } = 2;
    public int End { get; set; } = 6;
    public int Strength { get; set; } = 5;
    public int StrengthB { get; set; } = 3;
    public int SnapForce { get; set; } = 4;
    public int Frequency { get; set; } = 20;
    public int Period { get; set; } = 10;
    public int FirstFoot { get; set; } = 2;
    public int SecondFoot { get; set; } = 6;
    public int[] Zones { get; set; } = new int[10];
    public byte[] RawBytes { get; set; } = new byte[TriggerEffect.Size];

    /// <summary>Analog value below which the trigger reads 0 in emulation (0-255).</summary>
    public int Threshold { get; set; }

    public TriggerSettings Clone()
    {
        var c = (TriggerSettings)MemberwiseClone();
        c.Zones = (int[])Zones.Clone();
        c.RawBytes = (byte[])RawBytes.Clone();
        return c;
    }

    public TriggerEffect ToEffect() => Mode switch
    {
        TriggerMode.Normal or TriggerMode.Off => TriggerEffect.Off(),
        TriggerMode.GameCube => TriggerEffect.Weapon(6, 7, 8),
        TriggerMode.VerySoft => TriggerEffect.Feedback(0, 1),
        TriggerMode.Soft => TriggerEffect.Feedback(0, 3),
        TriggerMode.Medium => TriggerEffect.Feedback(0, 4),
        TriggerMode.Hard => TriggerEffect.Feedback(0, 6),
        TriggerMode.VeryHard => TriggerEffect.Feedback(0, 7),
        TriggerMode.Hardest => TriggerEffect.Feedback(0, 8),
        TriggerMode.Rigid => TriggerEffect.Feedback(0, 8),
        TriggerMode.VibrateTrigger => TriggerEffect.Vibration(0, 8, Math.Max(1, Frequency)),
        TriggerMode.VibrateTrigger10Hz => TriggerEffect.Vibration(0, 8, 10),
        TriggerMode.VibrateTriggerPulse => TriggerEffect.Machine(0, 9, 7, 2, Math.Max(1, Frequency), 20),
        TriggerMode.Choppy => TriggerEffect.MultiplePositionFeedback([8, 0, 8, 0, 8, 0, 8, 0, 8, 0]),
        TriggerMode.CustomTriggerValue => TriggerEffect.Raw(RawBytes),
        TriggerMode.Resistance or TriggerMode.Feedback => TriggerEffect.Feedback(Start, Strength),
        TriggerMode.Bow => TriggerEffect.Bow(Start, End, Strength, SnapForce),
        TriggerMode.Galloping => TriggerEffect.Galloping(Start, End, FirstFoot, SecondFoot, Frequency),
        TriggerMode.SemiAutomaticGun or TriggerMode.Weapon => TriggerEffect.Weapon(Start, End, Strength),
        TriggerMode.AutomaticGun or TriggerMode.Vibration => TriggerEffect.Vibration(Start, Strength, Frequency),
        TriggerMode.Machine => TriggerEffect.Machine(Start, End, Strength, StrengthB, Frequency, Period),
        TriggerMode.SlopeFeedback => TriggerEffect.SlopeFeedback(Start, End, Strength, StrengthB),
        TriggerMode.MultiplePositionFeedback => TriggerEffect.MultiplePositionFeedback(Zones),
        _ => TriggerEffect.Off(),
    };
}
