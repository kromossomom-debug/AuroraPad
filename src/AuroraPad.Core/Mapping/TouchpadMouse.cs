using AuroraPad.Core.Protocol;

namespace AuroraPad.Core.Mapping;

public sealed class TouchpadMouseSettings
{
    /// <summary>Use the touchpad like a laptop trackpad.</summary>
    public bool Enabled { get; set; } = true;
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
    /// <summary>Also send the touchpad click to the virtual controller.</summary>
    public bool KeepClickOnController { get; set; }
}

/// <summary>
/// Laptop-style trackpad on the DualSense/DS4 touchpad (1920x1080 coordinate space):
/// one finger moves, press clicks (right half = right click), tap clicks, two fingers scroll,
/// two-finger tap or press = right click.
/// </summary>
public sealed class TouchpadMouse
{
    private const double TapMaxSeconds = 0.18;
    private const int TapMaxTravel = 40;

    private TouchPoint _last1, _last2;
    private double _remX, _remY, _scrollRem;
    private double _touchStart;
    private int _travel;
    private int _maxFingers;
    private MappingTarget? _heldButton;

    public void Process(DualSenseInput i, TouchpadMouseSettings s, double now)
    {
        var t1 = i.Touch1;
        var t2 = i.Touch2;
        int fingers = (t1.Active ? 1 : 0) + (t2.Active ? 1 : 0);

        // ---- Physical click (pressing the pad) -------------------------------
        bool pressed = (i.Buttons & DualSenseButtons.Touchpad) != 0;
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

        // ---- Gesture start / tap detection ------------------------------------
        bool wasTouching = _last1.Active || _last2.Active;
        if (fingers > 0 && !wasTouching)
        {
            _touchStart = now;
            _travel = 0;
            _maxFingers = fingers;
        }
        _maxFingers = Math.Max(_maxFingers, fingers);

        if (fingers == 0 && wasTouching)
        {
            // Tap: short, almost no movement, and not a physical press.
            if (s.TapToClick && !pressed && _heldButton == null && now - _touchStart < TapMaxSeconds && _travel < TapMaxTravel)
            {
                var b = _maxFingers >= 2 ? MappingTarget.MouseRight : MappingTarget.MouseLeft;
                InputSimulator.MouseButton(b, true);
                InputSimulator.MouseButton(b, false);
            }
        }

        // ---- Movement / scrolling ---------------------------------------------
        if (fingers == 1 && t1.Active && _last1.Active && t1.Id == _last1.Id && !_last2.Active)
        {
            int dx = t1.X - _last1.X, dy = t1.Y - _last1.Y;
            _travel += Math.Abs(dx) + Math.Abs(dy);
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
            // ~60 touchpad units per wheel notch; natural direction (fingers up = content up) unless inverted.
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
    }

    public void Release()
    {
        if (_heldButton != null) InputSimulator.MouseButton(_heldButton.Value, false);
        _heldButton = null;
    }
}
