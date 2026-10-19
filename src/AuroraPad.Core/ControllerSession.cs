using AuroraPad.Core.Devices;
using AuroraPad.Core.Emulation;
using AuroraPad.Core.Mapping;
using AuroraPad.Core.Processing;
using AuroraPad.Core.Profiles;
using AuroraPad.Core.Protocol;
using AuroraPad.Core.Triggers;

namespace AuroraPad.Core;

/// <summary>
/// Glue between one physical controller, its active profile, the optional
/// virtual Xbox pad and any temporary overrides sent by mods over UDP.
/// </summary>
public sealed class ControllerSession : IDisposable
{
    private readonly object _lock = new();
    private Profile _profile;
    private IVirtualController? _emulator;
    private EmulationMode _emulatorMode;
    private readonly MappingEngine _mapping = new();
    private readonly ShortcutEngine _shortcuts = new();
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private DateTime _batteryFlashUntil;
    private Timer? _flashTimer;
    private bool _emulationPaused;
    private Haptics.HapticsEngine? _haptics;

    public string? HapticsError { get; private set; }
    public string? HapticsDevice => _haptics?.DeviceName;
    private (byte R, byte G, byte B)? _gameLightbar;
    private byte _gameRumbleLarge, _gameRumbleSmall;
    private byte _testRumbleLarge, _testRumbleSmall;
    private int _lastBatteryBucket = -1;

    // Mod (UDP) overrides; null = use profile.
    private TriggerEffect? _modLeft, _modRight;
    private int? _modLeftThreshold, _modRightThreshold;
    private (byte R, byte G, byte B)? _modColor;
    private byte? _modPlayerLeds;
    private MicLedMode? _modMic;

    public DualSenseDevice Device { get; }
    public int Index { get; set; }
    public string? EmulationError { get; private set; }
    public bool IsEmulating => _emulator != null;
    public string? EmulatorName => _emulator?.Name;

    /// <summary>Last processed state sent to the virtual controller (for the DSU server and UI).</summary>
    public PadState LastPad { get; private set; }

    /// <summary>Pause keyboard/mouse output (e.g. while the app window is being used).</summary>
    public bool MappingEnabled
    {
        get => _mapping.Enabled;
        set => _mapping.Enabled = value;
    }
    /// <summary>True while the touchpad is acting as a mouse (unlocked).</summary>
    public bool TouchpadMouseActive => _mapping.TouchpadMouseActive;

    public bool HasModOverrides => _modLeft != null || _modRight != null || _modColor != null || _modPlayerLeds != null || _modMic != null;

    public event Action<ControllerSession>? StateChanged;

    /// <summary>Raised on the read thread when a PS shortcut fires (after local actions ran).</summary>
    public event Action<ControllerSession, ShortcutAction>? ShortcutTriggered;

    public bool ShortcutsEnabled
    {
        get => _shortcuts.Enabled;
        set => _shortcuts.Enabled = value;
    }

    /// <summary>Temporarily disconnects the virtual controller without changing the profile.</summary>
    public bool EmulationPaused
    {
        get => _emulationPaused;
        set { _emulationPaused = value; ApplyEmulation(); }
    }

    public ControllerSession(DualSenseDevice device, Profile profile, int index = 0)
    {
        Device = device;
        Index = index;
        _profile = profile.Clone();
        Device.InputReceived += OnInput;
        _shortcuts.Triggered += OnShortcut;
        // Feel when the touchpad turns into a mouse (or locks again) without looking at the screen.
        _mapping.TouchpadMouseChanged += on => _ = PlayPatternAsync(on ? Haptics.RumblePattern.TickOn : Haptics.RumblePattern.TickOff);
        ApplyEmulation();
        ApplyHaptics();
        ApplyOutput();
    }

    public Profile Profile
    {
        get { lock (_lock) return _profile; }
    }

    public void SetProfile(Profile profile)
    {
        lock (_lock) _profile = profile.Clone();
        ApplyEmulation();
        ApplyHaptics();
        ApplyOutput();
    }

    private CancellationTokenSource? _patternCts;

    /// <summary>Plays a test rumble pattern (also goes to the audio haptics when they are on).</summary>
    public async Task PlayPatternAsync(Haptics.RumblePattern pattern)
    {
        _patternCts?.Cancel();
        var cts = _patternCts = new CancellationTokenSource();
        try
        {
            foreach (var (large, small, ms) in Haptics.RumblePatterns.Steps(pattern))
            {
                SetTestRumble(large, small);
                if (ms > 0) await Task.Delay(ms, cts.Token);
            }
        }
        catch (OperationCanceledException) { }
        finally { if (_patternCts == cts) SetTestRumble(0, 0); }
    }

    public void SetTestRumble(byte large, byte small)
    {
        _testRumbleLarge = large;
        _testRumbleSmall = small;
        ApplyOutput();
    }

    // ---- Mod API -------------------------------------------------------

    public void ModSetTrigger(bool left, TriggerEffect effect)
    {
        if (left) _modLeft = effect; else _modRight = effect;
        ApplyOutput();
    }

    public void ModSetThreshold(bool left, int threshold)
    {
        if (left) _modLeftThreshold = threshold; else _modRightThreshold = threshold;
    }

    public void ModSetColor(byte r, byte g, byte b)
    {
        _modColor = (r, g, b);
        ApplyOutput();
    }

    public void ModSetPlayerLeds(byte mask)
    {
        _modPlayerLeds = mask;
        ApplyOutput();
    }

    public void ModSetMicLed(MicLedMode mode)
    {
        _modMic = mode;
        ApplyOutput();
    }

    public void ResetModOverrides()
    {
        _modLeft = _modRight = null;
        _modLeftThreshold = _modRightThreshold = null;
        _modColor = null;
        _modPlayerLeds = null;
        _modMic = null;
        ApplyOutput();
    }

    // ---- Internals -----------------------------------------------------

    private void ApplyEmulation()
    {
        var want = _emulationPaused ? EmulationMode.None : Profile.Emulation;
        if (_emulator != null && _emulatorMode == want) { StateChanged?.Invoke(this); return; }

        // Mode changed (or turned off): drop the old virtual pad first.
        var old = _emulator;
        _emulator = null;
        old?.Dispose();
        _gameRumbleLarge = _gameRumbleSmall = 0;
        _gameLightbar = null;
        EmulationError = null;

        if (want != EmulationMode.None)
        {
            var pad = VirtualControllerFactory.TryCreate(want);
            if (pad == null)
                EmulationError = ViGEm.Error ?? "ViGEmBus não encontrado";
            else
            {
                pad.RumbleReceived += (large, small) =>
                {
                    _gameRumbleLarge = large;
                    _gameRumbleSmall = small;
                    ApplyOutput();
                };
                pad.LightbarReceived += (r, g, b) =>
                {
                    _gameLightbar = (r, g, b);
                    if (Profile.UseGameLightbar) ApplyOutput();
                };
                _emulatorMode = want;
                _emulator = pad;
            }
        }
        StateChanged?.Invoke(this);
    }

    public void ApplyOutput()
    {
        var p = Profile;
        var o = Device.Output;

        if (_modColor is { } c)
        {
            (o.LightbarR, o.LightbarG, o.LightbarB) = c;
        }
        else if (p.UseGameLightbar && _gameLightbar is { } gl && _emulatorMode == EmulationMode.DualShock4)
        {
            (o.LightbarR, o.LightbarG, o.LightbarB) = gl;
        }
        else
        {
            bool flash = DateTime.UtcNow < _batteryFlashUntil;
            var (r, g, b) = p.BatteryLightbar || flash ? BatteryColor(Device.Input.BatteryPercent) : (p.LightbarR, p.LightbarG, p.LightbarB);
            double k = flash ? 1.0 : Math.Clamp(p.LightbarBrightness, 0, 100) / 100.0;
            o.LightbarR = (byte)(r * k);
            o.LightbarG = (byte)(g * k);
            o.LightbarB = (byte)(b * k);
        }

        o.PlayerLeds = _modPlayerLeds ?? p.PlayerLedMask();
        o.PlayerLedBrightness = p.PlayerLedBrightness;
        o.MicLed = _modMic ?? p.MicLed;
        o.LeftTrigger = _modLeft ?? p.LeftTrigger.ToEffect();
        o.RightTrigger = _modRight ?? p.RightTrigger.ToEffect();

        double intensity = Math.Clamp(p.RumbleIntensity, 0, 200) / 100.0;
        byte large = (byte)Math.Clamp(Math.Max(_gameRumbleLarge, _testRumbleLarge) * intensity, 0, 255);
        byte small = (byte)Math.Clamp(Math.Max(_gameRumbleSmall, _testRumbleSmall) * intensity, 0, 255);
        bool rumbleToHaptics = _haptics != null && p.HapticsMode is Haptics.HapticsMode.RumbleToHaptics or Haptics.HapticsMode.Both;
        _haptics?.SetRumble(large, small);
        if (rumbleToHaptics && p.HapticsReplaceRumble) large = small = 0;
        o.RumbleLeft = large;
        o.RumbleRight = small;

        Device.MarkOutputDirty();
    }

    private static (byte, byte, byte) BatteryColor(int percent)
    {
        // Red at 0%, yellow at 50%, green at 100%.
        double t = Math.Clamp(percent / 100.0, 0, 1);
        byte r = (byte)(t < 0.5 ? 255 : 255 * (1 - t) * 2);
        byte g = (byte)(t < 0.5 ? 255 * t * 2 : 255);
        return (r, g, 0);
    }

    private void OnInput(DualSenseDevice dev, DualSenseInput input)
    {
        var p = Profile;
        var (shortcutSuppressed, injectPs) = _shortcuts.Process(input.Buttons);
        var (mapSuppressed, injected) = _mapping.Process(input, p);
        var suppressed = mapSuppressed | shortcutSuppressed;
        var pad = PadState.From(input, p, suppressed,
            _modLeftThreshold ?? p.LeftTrigger.Threshold,
            _modRightThreshold ?? p.RightTrigger.Threshold);
        if (p.Mouse.SuppressStick)
        {
            if (p.Mouse.Source == MouseSource.RightStick) pad = pad with { RightX = 0, RightY = 0 };
            if (p.Mouse.Source == MouseSource.LeftStick) pad = pad with { LeftX = 0, LeftY = 0 };
        }
        if (injectPs) injected |= DualSenseButtons.PS;
        if (injected != DualSenseButtons.None)
        {
            // Gestures that did not trigger a mapping (e.g. a single tap on a double-tap button) reach the game as a short tap.
            pad = pad with
            {
                Buttons = pad.Buttons | injected,
                Up = pad.Up || (injected & DualSenseButtons.DPadUp) != 0,
                Down = pad.Down || (injected & DualSenseButtons.DPadDown) != 0,
                Left = pad.Left || (injected & DualSenseButtons.DPadLeft) != 0,
                Right = pad.Right || (injected & DualSenseButtons.DPadRight) != 0,
                L2 = (injected & DualSenseButtons.L2) != 0 ? (byte)255 : pad.L2,
                R2 = (injected & DualSenseButtons.R2) != 0 ? (byte)255 : pad.R2,
            };
        }
        pad = ApplyGyroStick(pad, input, p);
        if (p.TurboButtons != DualSenseButtons.None)
        {
            // Square wave: held turbo buttons alternate pressed/released TurboRate times per second.
            double period = 1.0 / Math.Clamp(p.TurboRate, 2, 30);
            bool off = (_clock.Elapsed.TotalSeconds % period) >= period / 2;
            if (off)
            {
                var t = p.TurboButtons;
                pad = pad with
                {
                    Buttons = pad.Buttons & ~t,
                    Up = pad.Up && (t & DualSenseButtons.DPadUp) == 0,
                    Down = pad.Down && (t & DualSenseButtons.DPadDown) == 0,
                    Left = pad.Left && (t & DualSenseButtons.DPadLeft) == 0,
                    Right = pad.Right && (t & DualSenseButtons.DPadRight) == 0,
                    L2 = (t & DualSenseButtons.L2) != 0 ? (byte)0 : pad.L2,
                    R2 = (t & DualSenseButtons.R2) != 0 ? (byte)0 : pad.R2,
                };
            }
        }
        LastPad = pad;
        _emulator?.Update(pad, input);

        if (p.BatteryLightbar && _modColor == null)
        {
            int bucket = input.BatteryPercent / 10;
            if (bucket != _lastBatteryBucket)
            {
                _lastBatteryBucket = bucket;
                ApplyOutput();
            }
        }
    }

    /// <summary>Adds gyro motion to the right stick (aiming in games that only read a gamepad).</summary>
    private PadState ApplyGyroStick(PadState pad, DualSenseInput input, Profile p)
    {
        var g = p.GyroStick;
        if (!g.Enabled) return pad;
        if (g.ActivationButton != DualSenseButtons.None && (input.Buttons & g.ActivationButton) == 0) return pad;
        var (pitch, yaw, _) = _mapping.Gyro.Dps;
        double full = Math.Max(10, g.FullDeflectionDps);
        static double Shape(double v) => Math.Abs(v) < 0.02 ? 0 : Math.Sign(v) * Math.Min(1, 0.08 + Math.Abs(v)); // small anti-deadzone so slow aim registers
        double sx = Shape(-yaw / full), sy = Shape((g.InvertY ? -pitch : pitch) / full);
        return pad with
        {
            RightX = Math.Clamp(pad.RightX + sx, -1, 1),
            RightY = Math.Clamp(pad.RightY + sy, -1, 1),
        };
    }

    /// <summary>Learns the gyro's resting offset; the controller must lie still. Returns false on timeout.</summary>
    public Task<bool> CalibrateGyroAsync() => _mapping.Gyro.CalibrateAsync(TimeSpan.FromSeconds(6));

    public bool GyroAutoCalibrate
    {
        get => _mapping.Gyro.AutoCalibrate;
        set => _mapping.Gyro.AutoCalibrate = value;
    }

    public bool ShiftActive => _mapping.ShiftActive;

    /// <summary>Plays an audio file through the DualSense actuators (USB or Bluetooth). Returns an error or null.</summary>
    public string? PlayHapticsFile(string path)
    {
        if (Device.IsDualShock4) return "o DualShock 4 não tem hápticos";
        if (_haptics == null)
        {
            _haptics = CreateHapticsEngine(out var err);
            if (_haptics == null) return err;
            _haptics.Configure(Profile.HapticsMode, Profile.HapticsGain / 100.0, Profile.HapticsLowPassHz);
        }
        return _haptics.PlayFile(path);
    }

    /// <summary>USB: the controller's sound card. Bluetooth: HID report 0x32 streamed by the app.</summary>
    private Haptics.HapticsEngine? CreateHapticsEngine(out string? error)
    {
        if (Device.Connection == ConnectionType.Bluetooth)
        {
            error = null;
            return Haptics.HapticsEngine.CreateBluetooth(Device.WriteReport, "DualSense (Bluetooth)");
        }
        return Haptics.HapticsEngine.TryCreate(Index, out error);
    }

    private void ApplyHaptics()
    {
        var p = Profile;
        bool want = p.HapticsMode != Haptics.HapticsMode.Off && !Device.IsDualShock4;
        if (!want)
        {
            if (p.HapticsMode == Haptics.HapticsMode.Off && _haptics != null) { _haptics.Configure(Haptics.HapticsMode.Off, 1, 200); HapticsError = null; return; }
            _haptics?.Dispose();
            _haptics = null;
            HapticsError = p.HapticsMode == Haptics.HapticsMode.Off ? null : "o DualShock 4 não tem hápticos";
            return;
        }
        if (_haptics == null)
        {
            _haptics = CreateHapticsEngine(out var err);
            HapticsError = err;
        }
        _haptics?.Configure(p.HapticsMode, p.HapticsGain / 100.0, p.HapticsLowPassHz);
    }

    private void OnShortcut(ShortcutAction action)
    {
        switch (action)
        {
            case ShortcutAction.ToggleKeyboardMouse:
                MappingEnabled = !MappingEnabled;
                break;
            case ShortcutAction.ToggleVirtualController:
                EmulationPaused = !EmulationPaused;
                break;
            case ShortcutAction.ShowBattery:
                FlashBattery(TimeSpan.FromSeconds(3));
                break;
        }
        ShortcutTriggered?.Invoke(this, action);
    }

    /// <summary>Shows the battery level as lightbar colour for a moment.</summary>
    public void FlashBattery(TimeSpan duration)
    {
        _batteryFlashUntil = DateTime.UtcNow + duration;
        ApplyOutput();
        _flashTimer?.Dispose();
        _flashTimer = new Timer(_ => ApplyOutput(), null, duration + TimeSpan.FromMilliseconds(50), Timeout.InfiniteTimeSpan);
    }

    public void Dispose()
    {
        _flashTimer?.Dispose();
        _haptics?.Dispose();
        _haptics = null;
        Device.InputReceived -= OnInput;
        _mapping.ReleaseAll();
        _emulator?.Dispose();
        _emulator = null;
    }
}
