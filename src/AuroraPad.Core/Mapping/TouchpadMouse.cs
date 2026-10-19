using AuroraPad.Core.Protocol;

namespace AuroraPad.Core.Mapping;

/// <summary>How the touchpad-as-mouse gets switched on.</summary>
public enum TouchpadActivation
{
    /// <summary>Always works as a mouse.</summary>
    AlwaysOn,
    /// <summary>Two quick taps unlock it.</summary>
    DoubleTap,
    /// <summary>Resting a finger still for a moment unlocks it.</summary>
    Hold,
    /// <summary>Either a double tap or a hold unlocks it.</summary>
    DoubleTapOrHold,
}

public sealed class TouchpadMouseSettings
{
    /// <summary>Use the touchpad like a laptop trackpad.</summary>
    public bool Enabled { get; set; } = true;
    public TouchpadActivation Activation { get; set; } = TouchpadActivation.DoubleTapOrHold;
    /// <summary>How long a finger must rest to unlock (Hold modes), in ms.</summary>
    public int HoldMs { get; set; } = 500;
    /// <summary>Lock again after this many seconds without touching; 0 = stay unlocked.</summary>
    public int AutoLockSeconds { get; set; } = 8;
    /// <summary>Holding the touchpad click this long turns the mouse off; 0 = disabled.</summary>
    public int LockHoldSeconds { get; set; } = 5;
    /// <summary>1-100.</summary>
    public double Sensitivity { get; set; } = 40;
    /// <summary>0-100: how much faster quick swipes move the cursor.</summary>
    public double Acceleration { get; set; } = 40;
    /// <summary>Pressing the right half of the pad gives a right click.</summary>
    public bool RightHalfRightClick { get; set; } = true;
    /// <summary>A quick tap clicks without pressing the pad down.</summary>
    public bool TapToClick { get; set; } = true;
    /// <summary>Two fingers dragging up/down scroll the page.</summary>
    public bool TwoFingerScroll { get; set; } = true;
    public bool InvertScroll { get; set; }
    /// <summary>Also send the touchpad click to the virtual controller while the mouse is active.</summary>
    public bool KeepClickOnController { get; set; }
}

/// <summary>
/// Laptop-style trackpad on the DualSense/DS4 touchpad (1920x1080 coordinate space):
/// one finger moves, press clicks (right half = right click), tap clicks, two fingers scroll,
/// two-finger tap or press = right click. Depending on <see cref="TouchpadActivation"/> it
/// first has to be unlocked with a double tap or a resting finger, so games keep the touchpad
/// until the player actually wants the mouse.
/// </summary>
public sealed class TouchpadMouse
{
    private const double TapMaxSeconds = 0.18;
    private const int TapMaxTravel = 40;
    private const double DoubleTapWindow = 0.35;

    private TouchPoint _last1, _last2;
    private double _remX, _remY, _scrollRem;
    private double _touchStart;
    private int _travel;
    private int _maxFingers;
    private MappingTarget? _heldButton;

    private bool _unlocked;
    private double _lastActivity;
    private double _lastTapEnd = double.NegativeInfinity;
    private bool _ignoreUntilLift; // the gesture that unlocked must not also click or move
    private bool _lockedUntilLift; // just locked by a long press: wait for the finger to leave before re-arming
    private double _pressStart = -1;
    private bool _manualLocked;    // locked by long press: even AlwaysOn needs a gesture to come back

    /// <summary>True while touches drive the mouse.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Raised when the mouse switches on (true) or locks again (false).</summary>
    public event Action<bool>? ActiveChanged;

    /// <summary>Processes one report. Returns true when the touchpad is acting as a mouse.</summary>
    public bool Process(DualSenseInput i, TouchpadMouseSettings s, double now)
    {
        var t1 = i.Touch1;
        var t2 = i.Touch2;
        int fingers = (t1.Active ? 1 : 0) + (t2.Active ? 1 : 0);
        bool wasTouching = _last1.Active || _last2.Active;
        bool pressed = (i.Buttons & DualSenseButtons.Touchpad) != 0;

        // ---- Gesture bookkeeping (needed both locked and unlocked) ------------
        if (fingers > 0 && !wasTouching)
        {
            _touchStart = now;
            _travel = 0;
            _maxFingers = fingers;
        }
        _maxFingers = Math.Max(_maxFingers, fingers);
        if (fingers == 1 && t1.Active && _last1.Active && t1.Id == _last1.Id)
            _travel += Math.Abs(t1.X - _last1.X) + Math.Abs(t1.Y - _last1.Y);
        if (fingers > 0 || pressed) _lastActivity = now;

        if (pressed && _pressStart < 0) _pressStart = now;
        if (!pressed) _pressStart = -1;
        if (_lockedUntilLift)
        {
            if (fingers == 0 && !pressed) _lockedUntilLift = false;
            _last1 = t1; _last2 = t2;
            return false;
        }

        // Long press on the pad switches the mouse off (any activation mode).
        if (IsActive && s.LockHoldSeconds > 0 && pressed && now - _pressStart >= s.LockHoldSeconds)
        {
            Release();
            _unlocked = false;
            _lockedUntilLift = true;
            _manualLocked = true;
            _ignoreUntilLift = false;
            SetActive(false);
            _last1 = t1; _last2 = t2;
            return false;
        }

        bool tapEnded = fingers == 0 && wasTouching && !pressed
                        && now - _touchStart < TapMaxSeconds && _travel < TapMaxTravel;

        // ---- Lock / unlock ------------------------------------------------------
        var activation = s.Activation == TouchpadActivation.AlwaysOn && _manualLocked ? TouchpadActivation.DoubleTapOrHold : s.Activation;
        if (activation == TouchpadActivation.AlwaysOn)
        {
            SetActive(true);
        }
        else if (!_unlocked)
        {
            bool viaDoubleTap = activation is TouchpadActivation.DoubleTap or TouchpadActivation.DoubleTapOrHold;
            bool viaHold = activation is TouchpadActivation.Hold or TouchpadActivation.DoubleTapOrHold;

            if (tapEnded)
            {
                if (viaDoubleTap && now - _lastTapEnd < DoubleTapWindow)
                {
                    Unlock(now);
                    _lastTapEnd = double.NegativeInfinity;
                }
                else _lastTapEnd = now;
            }
            if (viaHold && fingers == 1 && !pressed && now - _touchStart >= Math.Max(150, s.HoldMs) / 1000.0 && _travel < TapMaxTravel)
            {
                Unlock(now);
                _ignoreUntilLift = true;
            }
            if (!_unlocked)
            {
                _last1 = t1; _last2 = t2;
                return false;
            }
        }
        else if (s.AutoLockSeconds > 0 && fingers == 0 && !pressed && now - _lastActivity > s.AutoLockSeconds)
        {
            _unlocked = false;
            Release();
            SetActive(false);
            _last1 = t1; _last2 = t2;
            return false;
        }

        if (_ignoreUntilLift)
        {
            if (fingers == 0) _ignoreUntilLift = false;
            _last1 = t1; _last2 = t2;
            return true;
        }

        // ---- Physical click (pressing the pad) -------------------------------
        if (pressed && _heldButton == null)
        {
            bool right = fingers >= 2 || (s.RightHalfRightClick && t1.Active && t1.X > 960);
            _heldButton = right ? MappingTarget.MouseRight : MappingTarget.MouseLeft;
            InputSimulator.MouseButton(_heldButton.Value, true);
        }
        else if (!pressed && _heldButton != null)
        {
            InputSimulator.MouseButton(_heldButton.Value, false);
            _heldButton = null;
        }

        // ---- Tap to click ----------------------------------------------------------
        if (tapEnded && s.TapToClick && _heldButton == null)
        {
            var b = _maxFingers >= 2 ? MappingTarget.MouseRight : MappingTarget.MouseLeft;
            InputSimulator.MouseButton(b, true);
            InputSimulator.MouseButton(b, false);
        }

        // ---- Movement / scrolling ---------------------------------------------
        if (fingers == 1 && t1.Active && _last1.Active && t1.Id == _last1.Id && !_last2.Active)
        {
            int dx = t1.X - _last1.X, dy = t1.Y - _last1.Y;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            // Linear base speed plus acceleration that grows with swipe speed.
            double gain = Math.Clamp(s.Sensitivity, 1, 100) / 45.0
                          * (1 + Math.Clamp(s.Acceleration, 0, 100) / 100.0 * Math.Min(dist / 25.0, 3.0));
            _remX += dx * gain;
            _remY += dy * gain;
            int mx = (int)_remX, my = (int)_remY;
            _remX -= mx; _remY -= my;
            if (mx != 0 || my != 0) InputSimulator.MouseMove(mx, my);
        }
        else if (fingers == 2 && s.TwoFingerScroll && _last1.Active && _last2.Active)
        {
            int dy = ((t1.Y - _last1.Y) + (t2.Y - _last2.Y)) / 2;
            _travel += Math.Abs(dy);
            // ~60 touchpad units per wheel notch; natural direction unless inverted.
            _scrollRem += (s.InvertScroll ? dy : -dy) / 60.0;
            int notches = (int)_scrollRem;
            _scrollRem -= notches;
            if (notches != 0) InputSimulator.Wheel(-notches);
        }
        else
        {
            _remX = _remY = 0;
        }

        _last1 = t1;
        _last2 = t2;
        return true;
    }

    private void Unlock(double now)
    {
        _unlocked = true;
        _manualLocked = false;
        _lastActivity = now;
        SetActive(true);
    }

    /// <summary>Locks the touchpad again (e.g. PS + touchpad shortcut).</summary>
    public void Lock()
    {
        _unlocked = false;
        Release();
        SetActive(false);
    }

    private void SetActive(bool active)
    {
        if (IsActive == active) return;
        IsActive = active;
        ActiveChanged?.Invoke(active);
    }

    public void Release()
    {
        if (_heldButton != null) InputSimulator.MouseButton(_heldButton.Value, false);
        _heldButton = null;
    }
}
