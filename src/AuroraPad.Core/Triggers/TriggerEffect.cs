namespace AuroraPad.Core.Triggers;

/// <summary>
/// Raw 11-byte adaptive trigger effect block (mode byte + 10 parameter bytes)
/// as written into the DualSense output report.
/// Effect encodings follow the community-documented DualSense trigger
/// effect formats (zones 0-9 across the trigger travel).
/// </summary>
public sealed class TriggerEffect
{
    public const int Size = 11;
    private readonly byte[] _data = new byte[Size];

    private TriggerEffect(byte mode) => _data[0] = mode;

    public ReadOnlySpan<byte> Data => _data;

    public static TriggerEffect Raw(ReadOnlySpan<byte> bytes)
    {
        var e = new TriggerEffect(0);
        bytes[..Math.Min(bytes.Length, Size)].CopyTo(e._data);
        return e;
    }

    /// <summary>No resistance.</summary>
    public static TriggerEffect Off() => new(0x05);

    /// <summary>Constant resistance from <paramref name="position"/> (0-9) to the end, strength 1-8.</summary>
    public static TriggerEffect Feedback(int position, int strength)
    {
        position = Math.Clamp(position, 0, 9);
        strength = Math.Clamp(strength, 0, 8);
        if (strength == 0) return Off();

        uint forceValue = (uint)((strength - 1) & 7);
        uint forceZones = 0;
        ushort activeZones = 0;
        for (int i = position; i < 10; i++)
        {
            forceZones |= forceValue << (3 * i);
            activeZones |= (ushort)(1 << i);
        }
        var e = new TriggerEffect(0x21);
        e._data[1] = (byte)(activeZones & 0xFF);
        e._data[2] = (byte)(activeZones >> 8);
        WriteUInt32(e._data, 3, forceZones);
        return e;
    }

    /// <summary>Per-zone resistance; <paramref name="strengths"/> holds 10 values 0-8.</summary>
    public static TriggerEffect MultiplePositionFeedback(ReadOnlySpan<int> strengths)
    {
        uint forceZones = 0;
        ushort activeZones = 0;
        for (int i = 0; i < 10 && i < strengths.Length; i++)
        {
            int s = Math.Clamp(strengths[i], 0, 8);
            if (s == 0) continue;
            forceZones |= (uint)((s - 1) & 7) << (3 * i);
            activeZones |= (ushort)(1 << i);
        }
        if (activeZones == 0) return Off();
        var e = new TriggerEffect(0x21);
        e._data[1] = (byte)(activeZones & 0xFF);
        e._data[2] = (byte)(activeZones >> 8);
        WriteUInt32(e._data, 3, forceZones);
        return e;
    }

    /// <summary>Linear ramp of resistance between two zones.</summary>
    public static TriggerEffect SlopeFeedback(int start, int end, int startStrength, int endStrength)
    {
        start = Math.Clamp(start, 0, 8);
        end = Math.Clamp(end, start + 1, 9);
        Span<int> s = stackalloc int[10];
        for (int i = 0; i < 10; i++)
        {
            if (i < start) s[i] = 0;
            else if (i >= end) s[i] = endStrength;
            else s[i] = (int)Math.Round(startStrength + (endStrength - startStrength) * (double)(i - start) / (end - start));
        }
        return MultiplePositionFeedback(s);
    }

    /// <summary>Resistance between start (2-7) and end (start+1..8) that "breaks" like a gun trigger.</summary>
    public static TriggerEffect Weapon(int start, int end, int strength)
    {
        start = Math.Clamp(start, 2, 7);
        end = Math.Clamp(end, start + 1, 8);
        strength = Math.Clamp(strength, 0, 8);
        if (strength == 0) return Off();
        ushort zones = (ushort)((1 << start) | (1 << end));
        var e = new TriggerEffect(0x25);
        e._data[1] = (byte)(zones & 0xFF);
        e._data[2] = (byte)(zones >> 8);
        e._data[3] = (byte)(strength - 1);
        return e;
    }

    /// <summary>Vibration from <paramref name="position"/> (0-9), amplitude 1-8, frequency Hz 1-255.</summary>
    public static TriggerEffect Vibration(int position, int amplitude, int frequency)
    {
        position = Math.Clamp(position, 0, 9);
        amplitude = Math.Clamp(amplitude, 0, 8);
        frequency = Math.Clamp(frequency, 0, 255);
        if (amplitude == 0 || frequency == 0) return Off();
        uint strength = (uint)((amplitude - 1) & 7);
        uint ampZones = 0;
        ushort activeZones = 0;
        for (int i = position; i < 10; i++)
        {
            ampZones |= strength << (3 * i);
            activeZones |= (ushort)(1 << i);
        }
        var e = new TriggerEffect(0x26);
        e._data[1] = (byte)(activeZones & 0xFF);
        e._data[2] = (byte)(activeZones >> 8);
        WriteUInt32(e._data, 3, ampZones);
        e._data[9] = (byte)frequency;
        return e;
    }

    /// <summary>Bow string: resistance that snaps back when released.</summary>
    public static TriggerEffect Bow(int start, int end, int strength, int snapForce)
    {
        start = Math.Clamp(start, 0, 8);
        end = Math.Clamp(end, start + 1, 8);
        strength = Math.Clamp(strength, 1, 8);
        snapForce = Math.Clamp(snapForce, 1, 8);
        ushort zones = (ushort)((1 << start) | (1 << end));
        int forcePair = ((strength - 1) & 7) | (((snapForce - 1) & 7) << 3);
        var e = new TriggerEffect(0x22);
        e._data[1] = (byte)(zones & 0xFF);
        e._data[2] = (byte)(zones >> 8);
        e._data[3] = (byte)(forcePair & 0xFF);
        e._data[4] = (byte)(forcePair >> 8);
        return e;
    }

    /// <summary>Galloping horse rhythm.</summary>
    public static TriggerEffect Galloping(int start, int end, int firstFoot, int secondFoot, int frequency)
    {
        start = Math.Clamp(start, 0, 8);
        end = Math.Clamp(end, start + 1, 9);
        firstFoot = Math.Clamp(firstFoot, 0, 6);
        secondFoot = Math.Clamp(secondFoot, firstFoot + 1, 7);
        frequency = Math.Clamp(frequency, 1, 255);
        ushort zones = (ushort)((1 << start) | (1 << end));
        int timeAndRatio = (secondFoot & 7) | ((firstFoot & 7) << 3);
        var e = new TriggerEffect(0x23);
        e._data[1] = (byte)(zones & 0xFF);
        e._data[2] = (byte)(zones >> 8);
        e._data[3] = (byte)timeAndRatio;
        e._data[4] = (byte)frequency;
        return e;
    }

    /// <summary>Alternating two-strength vibration ("machine").</summary>
    public static TriggerEffect Machine(int start, int end, int strengthA, int strengthB, int frequency, int period)
    {
        start = Math.Clamp(start, 0, 8);
        end = Math.Clamp(end, start + 1, 9);
        strengthA = Math.Clamp(strengthA, 0, 7);
        strengthB = Math.Clamp(strengthB, 0, 7);
        ushort zones = (ushort)((1 << start) | (1 << end));
        int pair = (strengthA & 7) | ((strengthB & 7) << 3);
        var e = new TriggerEffect(0x27);
        e._data[1] = (byte)(zones & 0xFF);
        e._data[2] = (byte)(zones >> 8);
        e._data[3] = (byte)pair;
        e._data[4] = (byte)Math.Clamp(frequency, 0, 255);
        e._data[5] = (byte)Math.Clamp(period, 0, 255);
        return e;
    }

    /// <summary>
    /// Decodes the effect into per-zone force (0-8, zone 0 = trigger at rest, 9 = fully pressed)
    /// and whether those zones vibrate. Used to draw the trigger profile in the UI.
    /// </summary>
    public (int[] Force, bool Vibrates) ZoneProfile()
    {
        var force = new int[10];
        ushort zones = (ushort)(_data[1] | (_data[2] << 8));
        uint packed = (uint)(_data[3] | (_data[4] << 8) | (_data[5] << 16) | (_data[6] << 24));
        (int First, int Last) Span()
        {
            int first = -1, last = -1;
            for (int i = 0; i < 10; i++) if ((zones & (1 << i)) != 0) { if (first < 0) first = i; last = i; }
            return (first, last);
        }
        switch (_data[0])
        {
            case 0x21: // feedback
            case 0x26: // vibration
                for (int i = 0; i < 10; i++)
                    if ((zones & (1 << i)) != 0) force[i] = (int)((packed >> (3 * i)) & 7) + 1;
                return (force, _data[0] == 0x26);
            case 0x25: // weapon: resistance between start and end, then it "breaks"
            {
                var (a, b) = Span();
                for (int i = Math.Max(a, 0); i < b && i < 10; i++) force[i] = _data[3] + 1;
                return (force, false);
            }
            case 0x22: // bow
            {
                var (a, b) = Span();
                for (int i = Math.Max(a, 0); i <= b && i < 10; i++) force[i] = (_data[3] & 7) + 1;
                return (force, false);
            }
            case 0x23: // galloping
            case 0x27: // machine
            {
                var (a, b) = Span();
                int s = _data[0] == 0x27 ? Math.Max(_data[3] & 7, (_data[3] >> 3) & 7) + 1 : 5;
                for (int i = Math.Max(a, 0); i <= b && i < 10; i++) force[i] = s;
                return (force, true);
            }
            default:
                return (force, false);
        }
    }

    private static void WriteUInt32(byte[] d, int offset, uint v)
    {
        d[offset] = (byte)(v & 0xFF);
        d[offset + 1] = (byte)((v >> 8) & 0xFF);
        d[offset + 2] = (byte)((v >> 16) & 0xFF);
        d[offset + 3] = (byte)((v >> 24) & 0xFF);
    }
}
