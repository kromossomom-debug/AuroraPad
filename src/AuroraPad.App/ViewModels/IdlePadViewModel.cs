using System.Windows.Media;

namespace AuroraPad.App.ViewModels;

/// <summary>
/// Stand-in for <see cref="ControllerViewModel"/> while no controller is connected, so the
/// controller drawing is always on screen (sticks centred, nothing pressed, lightbar off).
/// Property names match the ones DualSenseView binds to.
/// </summary>
public sealed class IdlePadViewModel
{
    public static IdlePadViewModel Instance { get; } = new();

    public double LCapLeft => 228 - 21;
    public double LCapTop => 212 - 21;
    public double RCapLeft => 372 - 21;
    public double RCapTop => 212 - 21;
    public double L2Height => 0;
    public double R2Height => 0;
    public double T1Left => 0;
    public double T1Top => 0;
    public double T2Left => 0;
    public double T2Top => 0;
    public bool Touch1Active => false;
    public bool Touch2Active => false;
    public bool Led1On => false;
    public bool Led2On => false;
    public bool Led3On => false;
    public bool Led4On => false;
    public bool Led5On => false;
    public bool MicOn => false;
    public bool TouchpadMouseOn => false;
    public Brush LightbarBrush => Brushes.DimGray;
    public Color LightbarColor => Colors.Transparent;
    public bool IsDualSense => true;
    public bool IsDualShock4 => false;

    public bool Cross => false;
    public bool Circle => false;
    public bool Square => false;
    public bool Triangle => false;
    public bool L1 => false;
    public bool R1 => false;
    public bool L3 => false;
    public bool R3 => false;
    public bool Up => false;
    public bool Down => false;
    public bool Left => false;
    public bool Right => false;
    public bool Create => false;
    public bool Options => false;
    public bool Ps => false;
    public bool Touchpad => false;
    public bool Mute => false;
}
