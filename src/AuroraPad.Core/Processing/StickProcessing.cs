namespace AuroraPad.Core.Processing;

public enum ResponseCurve { Linear, Smooth, Aggressive, Precise }

public sealed class StickSettings
{
    /// <summary>Radial inner deadzone, 0-1.</summary>
    public double Deadzone { get; set; } = 0.08;
    /// <summary>Outer saturation point, 0-1.</summary>
    public double OuterDeadzone { get; set; } = 1.0;
    /// <summary>Minimum output once outside the deadzone, 0-1 (helps games with big deadzones).</summary>
    public double AntiDeadzone { get; set; } = 0.0;
    public ResponseCurve Curve { get; set; } = ResponseCurve.Linear;
    public bool InvertX { get; set; }
    public bool InvertY { get; set; }
}

public static class StickProcessing
{
    /// <summary>
    /// Converts raw 0-255 axes (128 centre, Y down) into a processed vector in -1..1 (Y up).
    /// </summary>
    public static (double X, double Y) Process(byte rawX, byte rawY, StickSettings s)
    {
        double x = (rawX - 127.5) / 127.5;
        double y = -(rawY - 127.5) / 127.5;
        if (s.InvertX) x = -x;
        if (s.InvertY) y = -y;

        double mag = Math.Sqrt(x * x + y * y);
        if (mag < 1e-6) return (0, 0);
        double outer = Math.Clamp(s.OuterDeadzone, s.Deadzone + 0.01, 1.0);
        if (mag <= s.Deadzone) return (0, 0);

        double t = Math.Clamp((mag - s.Deadzone) / (outer - s.Deadzone), 0, 1);
        t = ApplyCurve(t, s.Curve);
        t = s.AntiDeadzone + (1 - s.AntiDeadzone) * t;
        double scale = t / mag;
        return (Math.Clamp(x * scale, -1, 1), Math.Clamp(y * scale, -1, 1));
    }

    public static double ApplyCurve(double t, ResponseCurve c) => c switch
    {
        ResponseCurve.Smooth => t * t * (3 - 2 * t),
        ResponseCurve.Aggressive => Math.Sqrt(t),
        ResponseCurve.Precise => t * t,
        _ => t,
    };

    /// <summary>Applies an inner deadzone/threshold to a 0-255 trigger.</summary>
    public static byte ProcessTrigger(byte raw, int threshold, double deadzone)
    {
        int dz = Math.Max(threshold, (int)(deadzone * 255));
        if (raw <= dz) return 0;
        return (byte)Math.Clamp((raw - dz) * 255 / Math.Max(1, 255 - dz), 0, 255);
    }

    public static short ToXboxAxis(double v) => (short)Math.Clamp(Math.Round(v * 32767), -32768, 32767);
}
