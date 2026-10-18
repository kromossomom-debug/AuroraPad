using AuroraPad.Core.Profiles;
using AuroraPad.Core.Protocol;

namespace AuroraPad.Core.Processing;

/// <summary>
/// Controller state after deadzones, curves, thresholds and button suppression;
/// this is what virtual controllers receive.
/// </summary>
public readonly record struct PadState(
    DualSenseButtons Buttons,
    bool Up, bool Down, bool Left, bool Right,
    double LeftX, double LeftY, double RightX, double RightY,
    byte L2, byte R2)
{
    public bool Has(DualSenseButtons b) => (Buttons & b) != 0;

    public static PadState From(DualSenseInput i, Profile p, DualSenseButtons suppressed, int l2Threshold, int r2Threshold)
    {
        var (lx, ly) = StickProcessing.Process(i.LeftX, i.LeftY, p.LeftStick);
        var (rx, ry) = StickProcessing.Process(i.RightX, i.RightY, p.RightStick);
        bool dpadSuppressed(DualSenseButtons b) => (suppressed & b) != 0;
        var buttons = i.Buttons & ~suppressed;
        if (p.SwapAB)
        {
            bool cross = (buttons & DualSenseButtons.Cross) != 0, circle = (buttons & DualSenseButtons.Circle) != 0;
            buttons &= ~(DualSenseButtons.Cross | DualSenseButtons.Circle);
            if (cross) buttons |= DualSenseButtons.Circle;
            if (circle) buttons |= DualSenseButtons.Cross;
        }
        return new PadState(
            buttons,
            i.DPadUp && !dpadSuppressed(DualSenseButtons.DPadUp),
            i.DPadDown && !dpadSuppressed(DualSenseButtons.DPadDown),
            i.DPadLeft && !dpadSuppressed(DualSenseButtons.DPadLeft),
            i.DPadRight && !dpadSuppressed(DualSenseButtons.DPadRight),
            lx, ly, rx, ry,
            (suppressed & DualSenseButtons.L2) != 0 ? (byte)0 : StickProcessing.ProcessTrigger(i.L2, l2Threshold, p.TriggerDeadzone),
            (suppressed & DualSenseButtons.R2) != 0 ? (byte)0 : StickProcessing.ProcessTrigger(i.R2, r2Threshold, p.TriggerDeadzone));
    }
}
