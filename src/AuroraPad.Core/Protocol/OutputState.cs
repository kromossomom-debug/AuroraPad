using AuroraPad.Core.Triggers;

namespace AuroraPad.Core.Protocol;

public enum MicLedMode : byte { Off = 0, On = 1, Pulse = 2 }

public enum LedBrightness : byte { High = 0, Medium = 1, Low = 2 }

/// <summary>Everything the app wants the controller to display or do.</summary>
public sealed class OutputState
{
    public byte LightbarR { get; set; } = 0;
    public byte LightbarG { get; set; } = 0;
    public byte LightbarB { get; set; } = 255;
    public byte PlayerLeds { get; set; } = 0x04;
    public LedBrightness PlayerLedBrightness { get; set; } = LedBrightness.High;
    public MicLedMode MicLed { get; set; } = MicLedMode.Off;
    public byte RumbleLeft { get; set; }
    public byte RumbleRight { get; set; }
    public TriggerEffect LeftTrigger { get; set; } = TriggerEffect.Off();
    public TriggerEffect RightTrigger { get; set; } = TriggerEffect.Off();

    /// <summary>Bluetooth audio: route the controller's audio path to the speaker (or headset jack).</summary>
    public bool SpeakerEnabled { get; set; }
    public bool SpeakerToHeadset { get; set; }
    /// <summary>0-100.</summary>
    public int SpeakerVolume { get; set; } = 80;

    /// <summary>Player LED bitmask presets (5 LEDs, bit 0 is leftmost).</summary>
    public static byte PlayerPattern(int player) => player switch
    {
        1 => 0x04,
        2 => 0x0A,
        3 => 0x15,
        4 => 0x1B,
        5 => 0x1F,
        _ => 0x00,
    };
}

/// <summary>Builds USB (0x02) and Bluetooth (0x31) output reports.</summary>
public static class OutputReportBuilder
{
    public const int UsbLength = 48;
    public const int BluetoothLength = 78;

    // valid_flag0
    private const byte CompatibleVibration = 0x01;
    private const byte HapticsSelect = 0x02;
    private const byte RightTriggerEffect = 0x04;
    private const byte LeftTriggerEffect = 0x08;
    // valid_flag1
    private const byte MicMuteLedControl = 0x01;
    private const byte LightbarControl = 0x04;
    private const byte PlayerIndicatorControl = 0x10;
    // valid_flag2
    private const byte LightbarSetupControl = 0x02;
    private const byte CompatibleVibration2 = 0x04;

    public static byte[] Build(OutputState s, bool bluetooth, ref byte btSequence, bool releaseBootLeds)
    {
        byte[] report = new byte[bluetooth ? BluetoothLength : UsbLength];
        int c; // offset of the common block
        if (bluetooth)
        {
            report[0] = 0x31;
            report[1] = (byte)(btSequence << 4);
            btSequence = (byte)((btSequence + 1) & 0x0F);
            report[2] = 0x10; // output tag
            c = 3;
        }
        else
        {
            report[0] = 0x02;
            c = 1;
        }

        var common = report.AsSpan(c, 47);
        WriteCommon(s, common, releaseBootLeds, bluetooth);

        if (bluetooth)
        {
            uint crc = Crc32.Compute(0xA2, report.AsSpan(0, BluetoothLength - 4));
            report[74] = (byte)crc;
            report[75] = (byte)(crc >> 8);
            report[76] = (byte)(crc >> 16);
            report[77] = (byte)(crc >> 24);
        }
        return report;
    }

    /// <summary>The 47-byte state block (same layout in report 0x31 and in the Bluetooth audio report 0x36).</summary>
    public static void WriteCommon(OutputState s, Span<byte> common, bool releaseBootLeds, bool bluetooth)
    {
        common[0] = (byte)(CompatibleVibration | HapticsSelect | RightTriggerEffect | LeftTriggerEffect);
        common[1] = (byte)(MicMuteLedControl | LightbarControl | PlayerIndicatorControl);
        common[2] = s.RumbleRight; // high-frequency (small) motor
        common[3] = s.RumbleLeft;  // low-frequency (large) motor
        common[8] = (byte)s.MicLed;
        s.RightTrigger.Data.CopyTo(common[10..]);
        s.LeftTrigger.Data.CopyTo(common[21..]);
        common[38] = (byte)(CompatibleVibration2 | (releaseBootLeds ? LightbarSetupControl : 0));
        common[41] = releaseBootLeds ? (byte)0x02 : (byte)0x00; // fade out boot light
        common[42] = (byte)s.PlayerLedBrightness;
        common[43] = (byte)(s.PlayerLeds | 0x20); // 0x20 = no fade animation
        common[44] = s.LightbarR;
        common[45] = s.LightbarG;
        common[46] = s.LightbarB;

        // Bluetooth speaker: enable the audio path (valid_flag0 0x20 speaker volume | 0x80 audio control,
        // valid_flag1 0x80 audio control 2), speaker volume, path select and preamp gain.
        if (bluetooth && s.SpeakerEnabled)
        {
            common[0] |= 0xA0;
            common[1] |= 0x80;
            common[5] = (byte)Math.Clamp(s.SpeakerVolume, 0, 100);
            common[7] = s.SpeakerToHeadset ? (byte)0x00 : (byte)0x30;
            common[37] = 0x02;
        }
    }
}
