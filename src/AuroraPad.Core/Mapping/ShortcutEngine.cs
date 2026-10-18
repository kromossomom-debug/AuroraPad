using System.Diagnostics;
using AuroraPad.Core.Protocol;

namespace AuroraPad.Core.Mapping;

/// <summary>
/// PS-button shortcuts. While PS is held it is kept away from the virtual controller; if it is
/// released without a combo, a short PS tap is forwarded so the game still sees the button.
/// </summary>
public sealed class ShortcutEngine
{
    public static IReadOnlyDictionary<DualSenseButtons, ShortcutAction> Combos { get; } = new Dictionary<DualSenseButtons, ShortcutAction>
    {
        [DualSenseButtons.Options] = ShortcutAction.NextProfile,
        [DualSenseButtons.Create] = ShortcutAction.PreviousProfile,
        [DualSenseButtons.Touchpad] = ShortcutAction.ToggleKeyboardMouse,
        [DualSenseButtons.Triangle] = ShortcutAction.ToggleVirtualController,
        [DualSenseButtons.DPadUp] = ShortcutAction.BrightnessUp,
        [DualSenseButtons.DPadDown] = ShortcutAction.BrightnessDown,
        [DualSenseButtons.Mute] = ShortcutAction.ShowBattery,
    };

    private const double TapSeconds = 0.08;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private bool _psHeld, _comboUsed;
    private DualSenseButtons _previous;
    private double _tapUntil;

    public bool Enabled { get; set; } = true;

    public event Action<ShortcutAction>? Triggered;

    /// <summary>Returns buttons to hide from the controller and whether a PS tap must be injected.</summary>
    public (DualSenseButtons Suppressed, bool InjectPs) Process(DualSenseButtons buttons)
    {
        var now = _clock.Elapsed.TotalSeconds;
        if (!Enabled) { _previous = buttons; return (DualSenseButtons.None, false); }

        bool ps = (buttons & DualSenseButtons.PS) != 0;
        var suppressed = DualSenseButtons.None;
        if (ps)
        {
            if (!_psHeld) { _psHeld = true; _comboUsed = false; }
            suppressed |= DualSenseButtons.PS;
            var pressedNow = buttons & ~_previous;
            foreach (var (button, action) in Combos)
            {
                if ((buttons & button) != 0) suppressed |= button; // combo buttons never reach the game
                if ((pressedNow & button) != 0)
                {
                    _comboUsed = true;
                    Triggered?.Invoke(action);
                }
            }
        }
        else if (_psHeld)
        {
            _psHeld = false;
            if (!_comboUsed) _tapUntil = now + TapSeconds;
        }
        _previous = buttons;
        return (suppressed, now < _tapUntil);
    }
}
