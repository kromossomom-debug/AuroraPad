namespace AuroraPad.Core.Haptics;

public enum RumblePattern { Pulse, Explosion, Engine, Heartbeat, Rain, Ramp, TickOn, TickOff }

/// <summary>Short test sequences of (large motor, small motor, duration) steps.</summary>
public static class RumblePatterns
{
    public static IEnumerable<(byte Large, byte Small, int Ms)> Steps(RumblePattern p)
    {
        switch (p)
        {
            case RumblePattern.Pulse:
                for (int i = 0; i < 3; i++) { yield return (200, 120, 90); yield return (0, 0, 110); }
                break;
            case RumblePattern.Explosion:
                yield return (255, 255, 120);
                for (int v = 230; v > 0; v -= 20) yield return ((byte)v, (byte)(v / 3), 60);
                break;
            case RumblePattern.Engine:
                for (int i = 0; i < 24; i++) yield return ((byte)(i % 2 == 0 ? 90 : 60), 30, 50);
                break;
            case RumblePattern.Heartbeat:
                for (int i = 0; i < 3; i++)
                {
                    yield return (220, 0, 80); yield return (0, 0, 90);
                    yield return (140, 0, 70); yield return (0, 0, 450);
                }
                break;
            case RumblePattern.Rain:
                var rnd = new Random(7);
                for (int i = 0; i < 30; i++) yield return (0, (byte)rnd.Next(60, 200), rnd.Next(20, 60));
                break;
            case RumblePattern.TickOn: // two short ticks: touchpad mouse on
                yield return (0, 140, 35); yield return (0, 0, 60); yield return (0, 140, 35);
                break;
            case RumblePattern.TickOff: // one short tick: touchpad mouse locked
                yield return (0, 100, 30);
                break;
            case RumblePattern.Ramp:
                for (int v = 0; v <= 255; v += 15) yield return ((byte)v, (byte)v, 60);
                break;
        }
        yield return (0, 0, 0);
    }
}
