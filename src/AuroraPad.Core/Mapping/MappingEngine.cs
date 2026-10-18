using System.Diagnostics;
using AuroraPad.Core.Processing;
using AuroraPad.Core.Profiles;
using AuroraPad.Core.Protocol;

namespace AuroraPad.Core.Mapping;

/// <summary>
/// Turns controller input into keyboard/mouse events according to a profile.
/// Runs on the controller read thread (~250 Hz over USB).
/// </summary>
public sealed class MappingEngine
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly HashSet<ButtonMapping> _held = [];
    private double _lastTime;
    private double _remX, _remY;
    private TouchPoint _lastTouch;
    private bool _enabled = true;
    private Profile? _lastProfile;
    private readonly TouchpadMouse _touchpad = new();

    /// <summary>When false, every held key is released and no events are sent.</summary>
    public bool Enabled
    {
        get => _enabled;
        set { _enabled = value; if (!value) ReleaseAll(); }
    }

    /// <summary>Processes one report; returns the buttons that must not reach the virtual controller.</summary>
    public DualSenseButtons Process(DualSenseInput i, Profile p)
    {
        double now = _clock.Elapsed.TotalSeconds;
        double dt = Math.Clamp(now - _lastTime, 0, 0.05);
        _lastTime = now;

        var suppressed = DualSenseButtons.None;
        if (!ReferenceEquals(p, _lastProfile)) { _lastProfile = p; _parsed.Clear(); }
        if (!_enabled) return suppressed;

        foreach (var m in p.Mappings)
        {
            if (m.Target == MappingTarget.None || m.Button == DualSenseButtons.None) continue;
            if (!m.KeepOnController) suppressed |= m.Button;

            bool pressed = (i.Buttons & m.Button) != 0;
            bool wasHeld = _held.Contains(m);
            if (pressed && !wasHeld) { _held.Add(m); Press(m, true); }
            else if (!pressed && wasHeld) { _held.Remove(m); Press(m, false); }
        }
        // Mappings removed while held must still be released.
        foreach (var stale in _held.Where(h => !p.Mappings.Contains(h)).ToList())
        {
            _held.Remove(stale);
            Press(stale, false);
            StopMacro(stale);
            _parsed.Remove(stale);
        }

        MoveMouse(i, p.Mouse, dt);

        if (p.Touchpad.Enabled)
        {
            _touchpad.Process(i, p.Touchpad, now);
            if (!p.Touchpad.KeepClickOnController) suppressed |= DualSenseButtons.Touchpad;
        }
        else _touchpad.Release();
        return suppressed;
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
                if (s.ActivationButton != DualSenseButtons.None && (i.Buttons & s.ActivationButton) == 0) break;
                // Raw gyro is roughly 16 units per degree/second. Yaw drives X, pitch drives Y.
                double yaw = -i.GyroY / 16.0, pitch = -i.GyroX / 16.0;
                if (Math.Abs(yaw) < 0.5) yaw = 0;   // ignore sensor noise when still
                if (Math.Abs(pitch) < 0.5) pitch = 0;
                dx = yaw * dt * sens * 0.6;
                dy = pitch * dt * sens * 0.6;
                break;

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
                bool right = s.Source == MouseSource.RightStick;
                var stick = new StickSettings { Deadzone = 0.1, Curve = ResponseCurve.Precise };
                var (x, y) = right ? StickProcessing.Process(i.RightX, i.RightY, stick) : StickProcessing.Process(i.LeftX, i.LeftY, stick);
                // Up to ~ sens * 40 pixels per second at full deflection.
                dx = x * sens * 40 * dt;
                dy = -y * sens * 40 * dt;
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
        _touchpad.Release();
        foreach (var m in _held) Press(m, false);
        _held.Clear();
        lock (_runningMacros)
        {
            foreach (var cts in _runningMacros.Values) cts.Cancel();
            _runningMacros.Clear();
        }
        _parsed.Clear();
    }
}
