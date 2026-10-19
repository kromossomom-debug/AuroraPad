using System.Diagnostics;
using AuroraPad.Core.Processing;
using AuroraPad.Core.Profiles;
using AuroraPad.Core.Protocol;

namespace AuroraPad.Core.Mapping;

/// <summary>
/// Turns controller input into keyboard/mouse events according to a profile.
/// Runs on the controller read thread (~250 Hz over USB).
/// Supports activators (press, tap, double tap, long press), toggles, a Shift layer,
/// two-stage triggers, gyro/stick/flick-stick mouse and the touchpad trackpad.
/// </summary>
public sealed class MappingEngine
{
    private const double LongPressSeconds = 0.40;
    private const double DoubleTapWindow = 0.25;
    private const double InjectTapSeconds = 0.06;

    private sealed class State
    {
        public bool Down;
        public double DownAt;
        public bool Fired;          // long press already fired during this hold
        public bool PassThrough;    // tap activator: held long, so the controller gets the button
        public int Taps;
        public double LastRelease;
        public bool ToggledOn;
        public bool OutputHeld;     // our key/click is currently held down
    }

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Dictionary<ButtonMapping, State> _states = [];
    private readonly Dictionary<DualSenseButtons, double> _injectUntil = [];
    private double _lastTime;
    private double _remX, _remY;
    private TouchPoint _lastTouch;
    private bool _enabled = true;
    private Profile? _lastProfile;
    private readonly TouchpadMouse _touchpad = new();

    // Flick stick
    private bool _flickActive;
    private double _flickPrevAngle, _flickRemaining;

    // Gyro smoothing (soft tiered smoothing over a short window)
    private readonly Queue<(double X, double Y)> _gyroWindow = new();

    public GyroCalibrator Gyro { get; } = new();

    /// <summary>True while the touchpad is driving the mouse.</summary>
    public bool TouchpadMouseActive => _touchpad.IsActive;

    /// <summary>True while the profile's Shift button is held.</summary>
    public bool ShiftActive { get; private set; }

    /// <summary>Raised (on the read thread) when the touchpad mouse switches on or off.</summary>
    public event Action<bool>? TouchpadMouseChanged
    {
        add => _touchpad.ActiveChanged += value;
        remove => _touchpad.ActiveChanged -= value;
    }

    /// <summary>When false, every held key is released and no events are sent.</summary>
    public bool Enabled
    {
        get => _enabled;
        set { _enabled = value; if (!value) ReleaseAll(); }
    }

    /// <summary>Adds the synthetic two-stage trigger buttons to the hardware buttons.</summary>
    public static DualSenseButtons WithTriggerStages(DualSenseInput i, Profile p)
    {
        var b = i.Buttons;
        if (i.L2 >= p.TriggerHalfPull) b |= DualSenseButtons.L2Half;
        if (i.L2 >= p.TriggerFullPull) b |= DualSenseButtons.L2Full;
        if (i.R2 >= p.TriggerHalfPull) b |= DualSenseButtons.R2Half;
        if (i.R2 >= p.TriggerFullPull) b |= DualSenseButtons.R2Full;
        return b;
    }

    /// <summary>
    /// Processes one report. Returns the buttons that must not reach the virtual controller and
    /// the buttons to inject into it (short taps for gestures that did not trigger a mapping).
    /// </summary>
    public (DualSenseButtons Suppressed, DualSenseButtons Injected) Process(DualSenseInput i, Profile p)
    {
        double now = _clock.Elapsed.TotalSeconds;
        double dt = Math.Clamp(now - _lastTime, 0, 0.05);
        _lastTime = now;
        Gyro.Update(i, now);

        var suppressed = DualSenseButtons.None;
        if (!ReferenceEquals(p, _lastProfile)) { _lastProfile = p; _parsed.Clear(); }
        if (!_enabled) return (suppressed, DualSenseButtons.None);

        var buttons = WithTriggerStages(i, p);

        // ---- Shift layer -------------------------------------------------------
        ShiftActive = p.ShiftButton != DualSenseButtons.None && (buttons & p.ShiftButton) != 0;
        if (p.ShiftButton != DualSenseButtons.None && p.Mappings.Any(m => m.Layer == 1))
            suppressed |= p.ShiftButton;
        int layer = ShiftActive ? 1 : 0;

        // ---- Button mappings ----------------------------------------------------
        foreach (var m in p.Mappings)
        {
            if (m.Target == MappingTarget.None || m.Button == DualSenseButtons.None) continue;
            if (!_states.TryGetValue(m, out var st)) _states[m] = st = new State();

            bool inLayer = m.Layer == layer;
            bool pressed = inLayer && (buttons & m.Button) != 0;

            if (inLayer)
            {
                if (m.Activator != PressActivator.Press || !m.KeepOnController) suppressed |= m.Button;
            }
            else if (!st.Down && !st.OutputHeld) continue;

            Step(m, st, pressed, now);
        }

        // Mappings removed from the profile while held must still be released.
        foreach (var stale in _states.Keys.Where(k => !p.Mappings.Contains(k)).ToList())
        {
            var st = _states[stale];
            if (st.OutputHeld) Press(stale, false);
            StopMacro(stale);
            _parsed.Remove(stale);
            _states.Remove(stale);
        }

        // ---- Mouse ---------------------------------------------------------------
        MoveMouse(i, p.Mouse, dt);

        if (p.Touchpad.Enabled)
        {
            // While locked (waiting for double tap / hold) the click still belongs to the game.
            bool mouseActive = _touchpad.Process(i, p.Touchpad, now);
            if (mouseActive && !p.Touchpad.KeepClickOnController) suppressed |= DualSenseButtons.Touchpad;
        }
        else if (_touchpad.IsActive) _touchpad.Lock();

        // ---- Injected taps ----------------------------------------------------------
        var injected = DualSenseButtons.None;
        foreach (var (b, until) in _injectUntil.ToList())
        {
            if (now < until) injected |= b;
            else _injectUntil.Remove(b);
        }
        foreach (var (m, st) in _states)
            if (st.PassThrough && st.Down) injected |= m.Button;

        return (suppressed, injected);
    }

    /// <summary>Activator state machine for one mapping.</summary>
    private void Step(ButtonMapping m, State st, bool pressed, double now)
    {
        bool pressEdge = pressed && !st.Down;
        bool releaseEdge = !pressed && st.Down;
        if (pressEdge) { st.Down = true; st.DownAt = now; st.Fired = false; st.PassThrough = false; }
        if (releaseEdge) st.Down = false;
        double held = now - st.DownAt;

        switch (m.Activator)
        {
            case PressActivator.Press:
                if (m.Toggle)
                {
                    if (pressEdge) { st.ToggledOn = !st.ToggledOn; Output(m, st, st.ToggledOn); }
                }
                else
                {
                    if (pressEdge) Output(m, st, true);
                    if (releaseEdge) Output(m, st, false);
                }
                break;

            case PressActivator.LongPress:
                if (st.Down && !st.Fired && held >= LongPressSeconds)
                {
                    st.Fired = true;
                    if (m.Toggle) { st.ToggledOn = !st.ToggledOn; Output(m, st, st.ToggledOn); }
                    else Output(m, st, true);
                }
                if (releaseEdge)
                {
                    if (st.Fired) { if (!m.Toggle) Output(m, st, false); }
                    else InjectTap(m.Button, now); // short press: the game gets the button
                }
                break;

            case PressActivator.Tap:
                if (st.Down && !st.PassThrough && held >= LongPressSeconds) st.PassThrough = true; // long hold goes to the game
                if (releaseEdge && !st.PassThrough) Click(m, st);
                break;

            case PressActivator.DoubleTap:
                if (st.Down && !st.PassThrough && held >= LongPressSeconds) { st.PassThrough = true; st.Taps = 0; }
                if (releaseEdge && !st.PassThrough)
                {
                    st.Taps++;
                    st.LastRelease = now;
                    if (st.Taps >= 2) { st.Taps = 0; Click(m, st); }
                }
                // A single tap that was not followed by a second one is handed to the game.
                if (!st.Down && st.Taps == 1 && now - st.LastRelease > DoubleTapWindow)
                {
                    st.Taps = 0;
                    InjectTap(m.Button, now);
                }
                break;
        }
    }

    private void InjectTap(DualSenseButtons b, double now) => _injectUntil[b] = now + InjectTapSeconds;

    private void Output(ButtonMapping m, State st, bool down)
    {
        if (st.OutputHeld == down && m.Target != MappingTarget.Macro) return;
        st.OutputHeld = down;
        Press(m, down);
    }

    /// <summary>A short press of the mapped key/click (used by tap gestures).</summary>
    private void Click(ButtonMapping m, State st)
    {
        if (m.Toggle) { st.ToggledOn = !st.ToggledOn; Output(m, st, st.ToggledOn); return; }
        Press(m, true);
        if (m.Target is MappingTarget.Macro or MappingTarget.WheelUp or MappingTarget.WheelDown) return;
        _ = Task.Delay(40).ContinueWith(_ => Press(m, false));
    }

    private readonly Dictionary<ButtonMapping, CancellationTokenSource> _runningMacros = [];
    private readonly Dictionary<ButtonMapping, Macro?> _parsed = [];

    private void Press(ButtonMapping m, bool down)
    {
        switch (m.Target)
        {
            case MappingTarget.Macro:
                if (down) StartMacro(m);
                else if (m.RepeatWhileHeld) StopMacro(m);
                break;
            case MappingTarget.Keyboard when m.VirtualKey != 0:
                InputSimulator.Key(m.VirtualKey, down);
                break;
            case MappingTarget.WheelUp when down:
                InputSimulator.Wheel(1);
                break;
            case MappingTarget.WheelDown when down:
                InputSimulator.Wheel(-1);
                break;
            case MappingTarget.MouseLeft or MappingTarget.MouseRight or MappingTarget.MouseMiddle
                or MappingTarget.Mouse4 or MappingTarget.Mouse5:
                InputSimulator.MouseButton(m.Target, down);
                break;
        }
    }

    private void MoveMouse(DualSenseInput i, MouseSettings s, double dt)
    {
        double dx = 0, dy = 0;
        double sens = Math.Clamp(s.Sensitivity, 1, 100);
        switch (s.Source)
        {
            case MouseSource.Gyro:
            {
                if (s.ActivationButton != DualSenseButtons.None && (i.Buttons & s.ActivationButton) == 0) { _gyroWindow.Clear(); break; }
                var (pitch, yawRate, _) = Gyro.Dps;
                double yaw = -(s.GyroPlayerSpace ? Gyro.PlayerYawDps : yawRate), pit = -pitch;
                // Soft tiered smoothing: slow, small movements (hand tremor) are averaged, fast flicks pass through.
                double speed = Math.Sqrt(yaw * yaw + pit * pit);
                double smoothBelow = Math.Clamp(s.GyroSmoothing, 0, 100) / 100.0 * 12; // dps
                _gyroWindow.Enqueue((yaw, pit));
                while (_gyroWindow.Count > 8) _gyroWindow.Dequeue();
                if (speed < smoothBelow && _gyroWindow.Count > 0)
                {
                    double ax = _gyroWindow.Average(g => g.X), ay = _gyroWindow.Average(g => g.Y);
                    double w = speed / Math.Max(smoothBelow, 1e-6); // 0 = fully smoothed, 1 = raw
                    yaw = ax * (1 - w) + yaw * w;
                    pit = ay * (1 - w) + pit * w;
                }
                if (Math.Abs(yaw) < 0.3) yaw = 0;
                if (Math.Abs(pit) < 0.3) pit = 0;
                // Acceleration: speeds above ~60°/s get up to (1 + accel) times more cursor travel.
                double boost = 1 + Math.Clamp(s.GyroAcceleration, 0, 100) / 100.0 * Math.Clamp((speed - 20) / 60, 0, 1);
                dx = yaw * dt * sens * 0.6 * boost;
                dy = pit * dt * sens * 0.6 * boost;
                break;
            }

            case MouseSource.Touchpad:
                var t = i.Touch1;
                if (t.Active && _lastTouch.Active && t.Id == _lastTouch.Id)
                {
                    dx = (t.X - _lastTouch.X) * sens / 40.0;
                    dy = (t.Y - _lastTouch.Y) * sens / 40.0;
                }
                _lastTouch = t;
                break;

            case MouseSource.RightStick:
            case MouseSource.LeftStick:
            {
                bool right = s.Source == MouseSource.RightStick;
                var stick = new StickSettings { Deadzone = 0.1, Curve = ResponseCurve.Precise };
                var (x, y) = right ? StickProcessing.Process(i.RightX, i.RightY, stick) : StickProcessing.Process(i.LeftX, i.LeftY, stick);
                // Up to ~ sens * 40 pixels per second at full deflection.
                dx = x * sens * 40 * dt;
                dy = -y * sens * 40 * dt;
                break;
            }

            case MouseSource.FlickStick:
                dx = FlickStick(i, s, dt);
                break;
        }
        if (s.InvertX) dx = -dx;
        if (s.InvertY) dy = -dy;

        // Keep sub-pixel remainders so slow motion still moves the cursor.
        _remX += dx; _remY += dy;
        int mx = (int)_remX, my = (int)_remY;
        _remX -= mx; _remY -= my;
        if (mx != 0 || my != 0) InputSimulator.MouseMove(mx, my);
    }

    /// <summary>
    /// Flick stick: pushing the right stick turns the camera straight to that direction
    /// (a quick 0.1 s turn), and rotating it while held keeps turning by the same angle.
    /// </summary>
    private double FlickStick(DualSenseInput i, MouseSettings s, double dt)
    {
        double x = (i.RightX - 127.5) / 127.5, y = -(i.RightY - 127.5) / 127.5;
        double mag = Math.Sqrt(x * x + y * y);
        double pixelsPerRad = Math.Max(100, s.FlickPixels360) / (2 * Math.PI);
        double turn = 0; // radians this frame

        if (mag > 0.85)
        {
            double angle = Math.Atan2(x, y); // 0 = up, positive = right
            if (!_flickActive)
            {
                _flickActive = true;
                _flickRemaining = angle; // start a flick toward the stick direction
            }
            else
            {
                double delta = angle - _flickPrevAngle;
                while (delta > Math.PI) delta -= 2 * Math.PI;
                while (delta < -Math.PI) delta += 2 * Math.PI;
                turn += delta;
            }
            _flickPrevAngle = angle;
        }
        else if (mag < 0.7)
        {
            _flickActive = false;
        }

        if (_flickRemaining != 0)
        {
            // Finish the flick over ~100 ms so the game can follow it.
            double step = Math.Sign(_flickRemaining) * Math.Min(Math.Abs(_flickRemaining), Math.PI * 2 * dt / 0.1 / 2);
            _flickRemaining -= step;
            if (Math.Abs(_flickRemaining) < 1e-4) _flickRemaining = 0;
            turn += step;
        }
        return turn * pixelsPerRad;
    }

    private void StartMacro(ButtonMapping m)
    {
        if (!_parsed.TryGetValue(m, out var macro))
        {
            Macro.TryParse(m.Macro, out macro, out _);
            _parsed[m] = macro;
        }
        if (macro == null) return;
        StopMacro(m);
        var cts = new CancellationTokenSource();
        lock (_runningMacros) _runningMacros[m] = cts;
        _ = Task.Run(async () =>
        {
            try
            {
                do await macro.PlayAsync(cts.Token);
                while (m.RepeatWhileHeld && !cts.IsCancellationRequested);
            }
            catch (OperationCanceledException) { }
            finally { lock (_runningMacros) if (_runningMacros.TryGetValue(m, out var c) && c == cts) _runningMacros.Remove(m); }
        });
    }

    private void StopMacro(ButtonMapping m)
    {
        lock (_runningMacros)
            if (_runningMacros.Remove(m, out var cts)) cts.Cancel();
    }

    public void ReleaseAll()
    {
        _touchpad.Lock();
        foreach (var (m, st) in _states)
            if (st.OutputHeld) { st.OutputHeld = false; Press(m, false); }
        _states.Clear();
        _injectUntil.Clear();
        lock (_runningMacros)
        {
            foreach (var cts in _runningMacros.Values) cts.Cancel();
            _runningMacros.Clear();
        }
        _parsed.Clear();
    }
}
