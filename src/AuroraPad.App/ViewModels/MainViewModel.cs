using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AuroraPad.App.Localization;
using AuroraPad.Core;
using AuroraPad.Core.Devices;
using AuroraPad.Core.Dsu;
using AuroraPad.Core.Emulation;
using AuroraPad.Core.Games;
using AuroraPad.Core.Haptics;
using AuroraPad.Core.Mapping;
using AuroraPad.Core.Osc;
using AuroraPad.Core.Processing;
using AuroraPad.Core.Profiles;
using AuroraPad.Core.Protocol;
using AuroraPad.Core.Udp;

namespace AuroraPad.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly DeviceManager _devices = new();
    private readonly ProfileStore _store;
    private readonly UdpModServer _udp;
    private readonly DsuServer _dsu;
    private readonly OscSender _osc = new();
    private readonly HidHideManager _hidHide = new();
    private readonly ForegroundWatcher _foreground;
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _liveTimer;
    private readonly AppSettings _settings;
    private readonly List<ControllerSession> _sessions = [];
    private bool _loading;

    // Profile auto-switching
    private Profile? _manualProfile;
    private Profile? _autoProfile;
    private bool _autoSwitching;

    public ObservableCollection<ControllerViewModel> Controllers { get; } = [];
    public ObservableCollection<Profile> Profiles { get; } = [];
    public ObservableCollection<string> LogLines { get; } = [];
    public ObservableCollection<MappingRowViewModel> Mappings { get; } = [];
    public ObservableCollection<TurboRowViewModel> TurboButtons { get; } = [];
    public ObservableCollection<string> GameExecutables { get; } = [];

    public TriggerEditorViewModel LeftTrigger { get; }
    public TriggerEditorViewModel RightTrigger { get; }

    public IReadOnlyList<ResponseCurve> Curves { get; } = Enum.GetValues<ResponseCurve>();
    public IReadOnlyList<MicLedMode> MicModes { get; } = Enum.GetValues<MicLedMode>();
    public IReadOnlyList<LedBrightness> LedBrightnesses { get; } = Enum.GetValues<LedBrightness>();

    public IReadOnlyList<Option<EmulationMode>> EmulationModes { get; } =
    [
        new(EmulationMode.None, L.T("emu.off")),
        new(EmulationMode.Xbox360, "Xbox 360"),
        new(EmulationMode.DualShock4, "DualShock 4"),
    ];

    public IReadOnlyList<Option<MouseSource>> MouseSources { get; } =
    [
        new(MouseSource.None, L.T("km.src.none")),
        new(MouseSource.Gyro, L.T("km.src.gyro")),
        new(MouseSource.Touchpad, "Touchpad"),
        new(MouseSource.RightStick, L.T("km.src.right")),
        new(MouseSource.LeftStick, L.T("km.src.left")),
        new(MouseSource.FlickStick, L.T("km.src.flick")),
    ];

    public IReadOnlyList<Option<HapticsMode>> HapticsModes { get; } =
    [
        new(HapticsMode.Off, L.T("hap.off")),
        new(HapticsMode.RumbleToHaptics, L.T("hap.rumble")),
        new(HapticsMode.AudioToHaptics, L.T("hap.audio")),
        new(HapticsMode.Both, L.T("hap.both")),
    ];

    public IReadOnlyList<Option<DualSenseButtons>> ActivationButtons { get; } =
        [new(DualSenseButtons.None, L.T("km.always")), .. MappingRowViewModel.AllButtons.Where(b => MappingRowViewModel.IsPhysical(b.Value))];

    public IReadOnlyList<Option<string>> Languages { get; } = L.Languages.Select(l => new Option<string>(l.Code, l.Name)).ToList();

    [ObservableProperty] private ControllerViewModel? _selectedController;
    [ObservableProperty] private Profile? _selectedProfile;
    [ObservableProperty] private string _newProfileName = "";
    [ObservableProperty] private string _statusText = "";

    // Lighting
    [ObservableProperty] private int _red, _green, _blue;
    [ObservableProperty] private int _brightness;
    [ObservableProperty] private bool _batteryLightbar;
    [ObservableProperty] private bool _led1, _led2, _led3, _led4, _led5;
    [ObservableProperty] private MicLedMode _micLed;
    [ObservableProperty] private LedBrightness _playerLedBrightness;
    [ObservableProperty] private Brush _previewBrush = Brushes.Blue;

    // Rumble / haptics
    [ObservableProperty] private int _rumbleIntensity;
    [ObservableProperty] private int _testLarge, _testSmall;
    [ObservableProperty] private Option<HapticsMode>? _selectedHapticsMode;
    [ObservableProperty] private int _hapticsGain;
    [ObservableProperty] private int _hapticsLowPassHz;
    [ObservableProperty] private bool _hapticsReplaceRumble;
    [ObservableProperty] private string _hapticsStatus = "";

    // Sticks
    [ObservableProperty] private double _leftDeadzone, _rightDeadzone, _leftAntiDeadzone, _rightAntiDeadzone;
    [ObservableProperty] private ResponseCurve _leftCurve, _rightCurve;
    [ObservableProperty] private bool _leftInvertY, _rightInvertY;
    [ObservableProperty] private double _triggerDeadzone;

    // Emulation
    [ObservableProperty] private Option<EmulationMode>? _selectedEmulation;
    [ObservableProperty] private bool _swapAB;
    [ObservableProperty] private bool _useGameLightbar;
    [ObservableProperty] private string _vigemStatus = "";
    [ObservableProperty] private bool _vigemMissing;

    // Mouse / turbo
    [ObservableProperty] private Option<MouseSource>? _selectedMouseSource;
    [ObservableProperty] private double _mouseSensitivity;
    [ObservableProperty] private Option<DualSenseButtons>? _mouseActivation;
    [ObservableProperty] private bool _mouseInvertX, _mouseInvertY, _mouseSuppressStick;
    [ObservableProperty] private int _turboRate;

    // Touchpad as mouse
    [ObservableProperty] private bool _tpEnabled, _tpRightHalf, _tpTap, _tpScroll, _tpInvertScroll, _tpKeepClick;
    [ObservableProperty] private double _tpSensitivity, _tpAcceleration;
    [ObservableProperty] private Option<TouchpadActivation>? _tpActivation;
    [ObservableProperty] private int _tpHoldMs, _tpAutoLock, _tpLockHold;
    [ObservableProperty] private string _tpState = "";

    public IReadOnlyList<Option<TouchpadActivation>> TouchpadActivations { get; } =
    [
        new(TouchpadActivation.DoubleTapOrHold, L.T("tp.act.both")),
        new(TouchpadActivation.DoubleTap, L.T("tp.act.double")),
        new(TouchpadActivation.Hold, L.T("tp.act.hold")),
        new(TouchpadActivation.AlwaysOn, L.T("tp.act.always")),
    ];

    // Games
    [ObservableProperty] private string _newExecutable = "";
    [ObservableProperty] private string? _selectedExecutable;
    [ObservableProperty] private string _activeGameLabel = "";

    // App settings
    [ObservableProperty] private bool _useUdp;
    [ObservableProperty] private int _udpPort;
    [ObservableProperty] private string _udpStatus = "";
    [ObservableProperty] private bool _useDsu;
    [ObservableProperty] private int _dsuPort;
    [ObservableProperty] private string _dsuStatus = "";
    [ObservableProperty] private bool _useOsc;
    [ObservableProperty] private string _oscAddress = "127.0.0.1";
    [ObservableProperty] private int _oscPort;
    [ObservableProperty] private string _oscStatus = "";
    [ObservableProperty] private bool _autoSwitchProfiles;
    [ObservableProperty] private bool _minimizeToTray;
    [ObservableProperty] private bool _startMinimized;
    [ObservableProperty] private bool _launchOnBoot;
    [ObservableProperty] private bool _useHidHide;
    [ObservableProperty] private string _hidHideStatus = "";
    [ObservableProperty] private bool _enableShortcuts;
    [ObservableProperty] private Option<string>? _selectedLanguage;

    public bool HidHideInstalled => _hidHide.IsInstalled;
    public bool HasControllers => Controllers.Count > 0;

    public MainViewModel(ProfileStore store, AppSettings settings)
    {
        _store = store;
        _settings = settings;
        LeftTrigger = new TriggerEditorViewModel(L.T("trg.left"), ProfileEdited);
        RightTrigger = new TriggerEditorViewModel(L.T("trg.right"), ProfileEdited);
        foreach (var b in MappingRowViewModel.AllButtons)
        {
            Mappings.Add(new MappingRowViewModel(b, MappingsEdited));
            if (MappingRowViewModel.IsPhysical(b.Value)) TurboButtons.Add(new TurboRowViewModel(b, TurboEdited));
        }
        ShortcutList = ShortcutEngine.Combos
            .Select(kv => $"PS + {MappingRowViewModel.AllButtons.First(b => b.Value == kv.Key).Label}  →  {L.T("sc." + kv.Value)}")
            .ToList();
        InitLibrary();
        EditingLayer = Layers[0];

        foreach (var p in _store.LoadProfiles()) Profiles.Add(p);
        SelectedProfile = Profiles.FirstOrDefault(p => p.Name == _settings.ActiveProfile) ?? Profiles[0];

        Func<IReadOnlyList<ControllerSession>> sessions = () => { lock (_sessions) return _sessions.ToList(); };
        _udp = new UdpModServer(sessions);
        _udp.Log += msg => Log(msg);
        _dsu = new DsuServer(sessions);

        // Controllers left hidden by a previous crash become visible again.
        if (_settings.HiddenInstanceIds.Count > 0)
        {
            _hidHide.Unhide(_settings.HiddenInstanceIds);
            _settings.HiddenInstanceIds.Clear();
            _store.SaveSettings(_settings);
        }

        _loading = true;
        UseUdp = _settings.UseIncomingUdp;
        UdpPort = _settings.IncomingUdpPort;
        UseDsu = _settings.UseDsuServer;
        DsuPort = _settings.DsuPort;
        UseOsc = _settings.UseOsc;
        OscAddress = _settings.OscAddress;
        OscPort = _settings.OscPort;
        AutoSwitchProfiles = _settings.AutoSwitchProfiles;
        MinimizeToTray = _settings.MinimizeToTray;
        StartMinimized = _settings.StartMinimized;
        LaunchOnBoot = AutoStart.IsEnabled();
        UseHidHide = _settings.UseHidHide && _hidHide.IsInstalled;
        EnableShortcuts = _settings.EnableShortcuts;
        GyroAutoCalibrate = _settings.GyroAutoCalibrate;
        LowBatteryAlert = _settings.LowBatteryAlert;
        SelectedLanguage = Languages.FirstOrDefault(l => l.Value == L.Language) ?? Languages[0];
        _loading = false;
        RestartUdp();
        RestartDsu();
        RestartOsc();

        VigemMissing = !ViGEm.IsAvailable;
        VigemStatus = VigemMissing ? L.T("emu.vigemMissing") : L.T("emu.vigemOk");

        _devices.DeviceConnected += d => Application.Current.Dispatcher.BeginInvoke(() => OnConnected(d));
        _devices.DeviceDisconnected += d => Application.Current.Dispatcher.BeginInvoke(() => OnDisconnected(d));
        _devices.Start();

        _foreground = new ForegroundWatcher(TimeSpan.FromSeconds(1));
        _foreground.ForegroundChanged += exe => Application.Current.Dispatcher.BeginInvoke(() => OnForegroundChanged(exe));

        // Live input is refreshed ~60 times a second at render priority so the drawing follows the hands.
        // (CompositionTarget.Rendering was not used: subscribing to it makes WPF redraw the whole window every
        // frame even when nothing changed, which cost ~10% of a CPU core.)
        _liveTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, OnRendering, Application.Current.Dispatcher);
        _liveTimer.Start();
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => UpdateStatus(), Application.Current.Dispatcher);
        _timer.Start();
    }

    public IReadOnlyList<string> ShortcutList { get; }

    // ---- Devices -------------------------------------------------------

    private void OnConnected(DualSenseDevice dev)
    {
        int index;
        lock (_sessions) index = Enumerable.Range(0, 8).First(i => _sessions.All(s => s.Index != i));
        var session = new ControllerSession(dev, SelectedProfile!, index);
        session.ShortcutsEnabled = EnableShortcuts;
        session.GyroAutoCalibrate = GyroAutoCalibrate;
        session.ShortcutTriggered += (s, action) => Application.Current.Dispatcher.BeginInvoke(() => OnShortcut(s, action));
        lock (_sessions) _sessions.Add(session);
        _dsu.Attach(session);
        _osc.Attach(session);
        var vm = new ControllerViewModel(session);
        Controllers.Add(vm);
        SelectedController ??= vm;
        OnPropertyChanged(nameof(HasControllers));
        Log(L.F("log.connected", vm.Name, vm.ConnectionKind));
        ApplyHidHide();
    }

    private void OnDisconnected(DualSenseDevice dev)
    {
        var vm = Controllers.FirstOrDefault(c => c.Session.Device == dev);
        if (vm == null) return;
        lock (_sessions) _sessions.Remove(vm.Session);
        vm.Session.Dispose();
        Controllers.Remove(vm);
        if (SelectedController == vm) SelectedController = Controllers.FirstOrDefault();
        OnPropertyChanged(nameof(HasControllers));
        Log(L.F("log.disconnected", vm.Name));
    }

    // Diagnostics (AURORAPAD_DIAG=1): frame intervals and refresh cost, written once per second.
    private static readonly bool Diag = Environment.GetEnvironmentVariable("AURORAPAD_DIAG") == "1";
    private readonly Stopwatch _diagClock = Stopwatch.StartNew();
    private double _diagLast, _diagMax, _diagSum, _diagWork;
    private int _diagCount;

    /// <summary>Per-frame refresh of everything that follows the controller live.</summary>
    private void OnRendering(object? sender, EventArgs e)
    {
        double t0 = _diagClock.Elapsed.TotalMilliseconds;
        // Only the selected controller is drawn live; others are refreshed by the status timer.
        SelectedController?.Refresh();
        var live = SelectedController?.Session.Device.Input;
        LeftTrigger.SetLive(live?.L2 ?? 0);
        RightTrigger.SetLive(live?.R2 ?? 0);
        if (!Diag) return;
        double t1 = _diagClock.Elapsed.TotalMilliseconds;
        if (_diagLast > 0) { double d = t0 - _diagLast; _diagSum += d; _diagMax = Math.Max(_diagMax, d); _diagCount++; }
        _diagWork += t1 - t0;
        _diagLast = t0;
        if (_diagSum > 1000)
        {
            File.AppendAllText(Path.Combine(_store.Root, "diag.log"),
                $"{DateTime.Now:HH:mm:ss} frames={_diagCount} avg={_diagSum / _diagCount:F1}ms max={_diagMax:F1}ms work={_diagWork / _diagCount:F2}ms tier={RenderCapability.Tier >> 16}\n");
            _diagSum = _diagMax = _diagWork = 0; _diagCount = 0;
        }
    }

    /// <summary>Slow refresh (4 Hz) of status text and of controllers that are not selected.</summary>
    private void UpdateStatus()
    {
        CheckBattery();
        if (IsInBackground) return; // nothing on screen to update
        UpdateMemory();
        foreach (var c in Controllers)
        {
            c.RefreshStatus();
            if (c != SelectedController) c.Refresh();
        }
        UdpStatus = !UseUdp ? L.T("common.disabled")
            : _udp.IsRunning ? L.F("udp.status", UdpPort, _udp.PacketsReceived) + (_udp.LastPacket is { } t ? L.F("udp.last", t.ToString("HH:mm:ss")) : "")
            : L.F("common.error", _udp.LastError);
        DsuStatus = !UseDsu ? L.T("common.disabled")
            : _dsu.IsRunning ? L.F("dsu.status", DsuPort, _dsu.ClientCount)
            : L.F("common.error", _dsu.LastError);
        OscStatus = !UseOsc ? L.T("common.disabled") : L.F("osc.status", OscAddress, OscPort, _osc.MessagesSent);
        HidHideStatus = !_hidHide.IsInstalled ? L.T("hh.missing")
            : _hidHide.LastError is { } err ? L.F("hh.error", err)
            : _hidHide.HiddenIds.Count > 0 ? L.F("hh.hidden", _hidHide.HiddenIds.Count)
            : L.T("hh.ok");
        var hs = SelectedController?.Session;
        TpState = hs == null || !TpEnabled ? "" : hs.TouchpadMouseActive ? L.T("tp.state.on") : L.T("tp.state.off");
        HapticsStatus = hs == null ? "" : hs.HapticsDevice != null ? L.F("ctl.haptics", hs.HapticsDevice)
            : hs.HapticsError != null ? L.F("ctl.hapticsError", hs.HapticsError) : "";
        StatusText = (Controllers.Count switch
        {
            0 => L.T("status.none"),
            1 => L.T("status.one"),
            var n => L.F("status.many", n),
        }) + (SelectedProfile != null ? L.F("status.profile", SelectedProfile.Name) : "")
           + (_autoProfile != null ? L.T("status.auto") : "");
    }
    // ---- Profile editing ----------------------------------------------

    partial void OnSelectedProfileChanged(Profile? value)
    {
        if (value == null) return;
        if (!_autoSwitching)
        {
            _manualProfile = value;
            _autoProfile = null;
            _settings.ActiveProfile = value.Name;
            _store.SaveSettings(_settings);
        }
        LoadEditors(value);
        ApplyToSessions();
    }

    private void LoadEditors(Profile p)
    {
        _loading = true;
        Red = p.LightbarR; Green = p.LightbarG; Blue = p.LightbarB;
        Brightness = p.LightbarBrightness; BatteryLightbar = p.BatteryLightbar;
        Led1 = p.PlayerLeds[0]; Led2 = p.PlayerLeds[1]; Led3 = p.PlayerLeds[2]; Led4 = p.PlayerLeds[3]; Led5 = p.PlayerLeds[4];
        MicLed = p.MicLed; PlayerLedBrightness = p.PlayerLedBrightness;
        RumbleIntensity = p.RumbleIntensity;
        SelectedHapticsMode = HapticsModes.First(m => m.Value == p.HapticsMode);
        HapticsGain = p.HapticsGain; HapticsLowPassHz = p.HapticsLowPassHz; HapticsReplaceRumble = p.HapticsReplaceRumble;
        LeftDeadzone = p.LeftStick.Deadzone * 100; RightDeadzone = p.RightStick.Deadzone * 100;
        LeftAntiDeadzone = p.LeftStick.AntiDeadzone * 100; RightAntiDeadzone = p.RightStick.AntiDeadzone * 100;
        LeftCurve = p.LeftStick.Curve; RightCurve = p.RightStick.Curve;
        LeftInvertY = p.LeftStick.InvertY; RightInvertY = p.RightStick.InvertY;
        TriggerDeadzone = p.TriggerDeadzone * 100;
        SelectedEmulation = EmulationModes.First(m => m.Value == p.Emulation);
        SwapAB = p.SwapAB;
        UseGameLightbar = p.UseGameLightbar;
        SelectedMouseSource = MouseSources.First(m => m.Value == p.Mouse.Source);
        MouseSensitivity = p.Mouse.Sensitivity;
        MouseActivation = ActivationButtons.FirstOrDefault(b => b.Value == p.Mouse.ActivationButton) ?? ActivationButtons[0];
        MouseInvertX = p.Mouse.InvertX; MouseInvertY = p.Mouse.InvertY; MouseSuppressStick = p.Mouse.SuppressStick;
        TurboRate = p.TurboRate;
        TpEnabled = p.Touchpad.Enabled; TpSensitivity = p.Touchpad.Sensitivity; TpAcceleration = p.Touchpad.Acceleration;
        TpRightHalf = p.Touchpad.RightHalfRightClick; TpTap = p.Touchpad.TapToClick; TpScroll = p.Touchpad.TwoFingerScroll;
        TpInvertScroll = p.Touchpad.InvertScroll; TpKeepClick = p.Touchpad.KeepClickOnController;
        TpActivation = TouchpadActivations.FirstOrDefault(a => a.Value == p.Touchpad.Activation) ?? TouchpadActivations[0];
        TpHoldMs = p.Touchpad.HoldMs; TpAutoLock = p.Touchpad.AutoLockSeconds; TpLockHold = p.Touchpad.LockHoldSeconds;
        foreach (var t in TurboButtons) { t.Loading = true; t.IsOn = (p.TurboButtons & t.Button) != 0; t.Loading = false; }
        LeftTrigger.Load(p.LeftTrigger);
        RightTrigger.Load(p.RightTrigger);
        LoadAdvanced(p);
        GameExecutables.Clear();
        foreach (var exe in p.Executables) GameExecutables.Add(exe);
        _loading = false;
        UpdatePreview();
    }

    private static readonly HashSet<string> ProfileFields =
    [
        nameof(Red), nameof(Green), nameof(Blue), nameof(Brightness), nameof(BatteryLightbar),
        nameof(Led1), nameof(Led2), nameof(Led3), nameof(Led4), nameof(Led5),
        nameof(MicLed), nameof(PlayerLedBrightness), nameof(RumbleIntensity),
        nameof(SelectedHapticsMode), nameof(HapticsGain), nameof(HapticsLowPassHz), nameof(HapticsReplaceRumble),
        nameof(LeftDeadzone), nameof(RightDeadzone), nameof(LeftAntiDeadzone), nameof(RightAntiDeadzone),
        nameof(LeftCurve), nameof(RightCurve), nameof(LeftInvertY), nameof(RightInvertY),
        nameof(TriggerDeadzone), nameof(SelectedEmulation), nameof(SwapAB), nameof(UseGameLightbar),
        nameof(SelectedMouseSource), nameof(MouseSensitivity), nameof(MouseActivation),
        nameof(MouseInvertX), nameof(MouseInvertY), nameof(MouseSuppressStick), nameof(TurboRate),
        nameof(TpEnabled), nameof(TpSensitivity), nameof(TpAcceleration), nameof(TpRightHalf), nameof(TpTap),
        nameof(TpScroll), nameof(TpInvertScroll), nameof(TpKeepClick), nameof(TpActivation), nameof(TpHoldMs), nameof(TpAutoLock), nameof(TpLockHold),
    ];

    private static readonly HashSet<string> SettingFields =
    [
        nameof(UseUdp), nameof(UdpPort), nameof(UseDsu), nameof(DsuPort), nameof(UseOsc), nameof(OscAddress), nameof(OscPort),
        nameof(AutoSwitchProfiles), nameof(MinimizeToTray), nameof(StartMinimized), nameof(LaunchOnBoot),
        nameof(UseHidHide), nameof(EnableShortcuts), nameof(SelectedLanguage),
    ];

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (_loading || SelectedProfile == null || e.PropertyName == null) return;
        if (ProfileFields.Contains(e.PropertyName) || AdvancedProfileFields.Contains(e.PropertyName))
            WriteEditorsToProfile();
        else if (AdvancedSettingFields.Contains(e.PropertyName))
            AdvancedSettingsEdited(e.PropertyName);
        else if (e.PropertyName is nameof(TestLarge) or nameof(TestSmall))
            foreach (var c in Controllers) c.Session.SetTestRumble((byte)TestLarge, (byte)TestSmall);
        else if (SettingFields.Contains(e.PropertyName))
            SettingsEdited(e.PropertyName);
        else if (e.PropertyName == nameof(LibraryFilter))
            LibraryView.Refresh();
    }

    private void WriteEditorsToProfile()
    {
        var p = SelectedProfile!;
        p.LightbarR = (byte)Red; p.LightbarG = (byte)Green; p.LightbarB = (byte)Blue;
        p.LightbarBrightness = Brightness; p.BatteryLightbar = BatteryLightbar;
        p.PlayerLeds = [Led1, Led2, Led3, Led4, Led5];
        p.MicLed = MicLed; p.PlayerLedBrightness = PlayerLedBrightness;
        p.RumbleIntensity = RumbleIntensity;
        p.HapticsMode = SelectedHapticsMode?.Value ?? HapticsMode.Off;
        p.HapticsGain = HapticsGain; p.HapticsLowPassHz = HapticsLowPassHz; p.HapticsReplaceRumble = HapticsReplaceRumble;
        p.LeftStick.Deadzone = LeftDeadzone / 100; p.RightStick.Deadzone = RightDeadzone / 100;
        p.LeftStick.AntiDeadzone = LeftAntiDeadzone / 100; p.RightStick.AntiDeadzone = RightAntiDeadzone / 100;
        p.LeftStick.Curve = LeftCurve; p.RightStick.Curve = RightCurve;
        p.LeftStick.InvertY = LeftInvertY; p.RightStick.InvertY = RightInvertY;
        p.TriggerDeadzone = TriggerDeadzone / 100;
        p.Emulation = SelectedEmulation?.Value ?? EmulationMode.None;
        p.SwapAB = SwapAB;
        p.UseGameLightbar = UseGameLightbar;
        p.Mouse.Source = SelectedMouseSource?.Value ?? MouseSource.None;
        p.Mouse.Sensitivity = MouseSensitivity;
        p.Mouse.ActivationButton = MouseActivation?.Value ?? DualSenseButtons.None;
        p.Mouse.InvertX = MouseInvertX; p.Mouse.InvertY = MouseInvertY; p.Mouse.SuppressStick = MouseSuppressStick;
        p.TurboRate = TurboRate;
        WriteAdvanced(p);
        p.Touchpad.Enabled = TpEnabled; p.Touchpad.Sensitivity = TpSensitivity; p.Touchpad.Acceleration = TpAcceleration;
        p.Touchpad.RightHalfRightClick = TpRightHalf; p.Touchpad.TapToClick = TpTap; p.Touchpad.TwoFingerScroll = TpScroll;
        p.Touchpad.InvertScroll = TpInvertScroll; p.Touchpad.KeepClickOnController = TpKeepClick;
        p.Touchpad.Activation = TpActivation?.Value ?? TouchpadActivation.DoubleTapOrHold;
        p.Touchpad.HoldMs = TpHoldMs; p.Touchpad.AutoLockSeconds = TpAutoLock; p.Touchpad.LockHoldSeconds = TpLockHold;
        UpdatePreview();
        ProfileEdited();
    }

    private void MappingsEdited()
    {
        if (_loading || SelectedProfile == null) return;
        SelectedProfile.Mappings = MergeMappingRows(SelectedProfile);
        ProfileEdited();
    }

    private void TurboEdited()
    {
        if (_loading || SelectedProfile == null) return;
        SelectedProfile.TurboButtons = TurboButtons.Where(t => t.IsOn).Aggregate(DualSenseButtons.None, (m, t) => m | t.Button);
        ProfileEdited();
    }

    private void ProfileEdited()
    {
        if (_loading) return;
        ApplyToSessions();
    }

    private void ApplyToSessions()
    {
        if (SelectedProfile == null) return;
        foreach (var c in Controllers) c.Session.SetProfile(SelectedProfile);
        ApplyHidHide();
    }

    private void UpdatePreview()
    {
        double k = Brightness / 100.0;
        var brush = new SolidColorBrush(Color.FromRgb((byte)(Red * k), (byte)(Green * k), (byte)(Blue * k)));
        brush.Freeze();
        PreviewBrush = brush;
    }

    [RelayCommand]
    private void SetColor(object? value)
    {
        // Swatch buttons pass their Background brush; "#AARRGGBB" strings also work.
        if (value?.ToString() is not { } hex) return;
        var c = (Color)ColorConverter.ConvertFromString(hex);
        _loading = true;
        Red = c.R; Green = c.G; Blue = c.B;
        _loading = false;
        WriteEditorsToProfile();
    }

    [RelayCommand]
    private void SetPlayer(string player)
    {
        byte mask = OutputState.PlayerPattern(int.Parse(player));
        _loading = true;
        Led1 = (mask & 1) != 0; Led2 = (mask & 2) != 0; Led3 = (mask & 4) != 0; Led4 = (mask & 8) != 0;
        Led5 = (mask & 16) != 0;
        _loading = false;
        WriteEditorsToProfile();
    }

    [RelayCommand]
    private void PlayPattern(string name)
    {
        if (!Enum.TryParse<RumblePattern>(name, out var pattern)) return;
        foreach (var c in Controllers) _ = c.Session.PlayPatternAsync(pattern);
    }

    [RelayCommand]
    private void ClearMappings()
    {
        _loading = true;
        foreach (var row in Mappings) row.Load(null);
        _loading = false;
        MappingsEdited();
    }

    // ---- Per-game profiles --------------------------------------------

    private void OnForegroundChanged(string exe)
    {
        ActiveGameLabel = L.F("set.foreground", exe);
        if (!AutoSwitchProfiles) return;
        var match = Profiles.FirstOrDefault(p => p.Executables.Any(e => string.Equals(Path.GetFileName(e), exe, StringComparison.OrdinalIgnoreCase)));
        if (match != null && match != SelectedProfile)
        {
            _autoSwitching = true;
            _autoProfile = match;
            SelectedProfile = match;
            _autoSwitching = false;
            Log(L.F("log.autoProfile", exe, match.Name));
        }
        else if (match == null && _autoProfile != null && _manualProfile != null)
        {
            _autoSwitching = true;
            _autoProfile = null;
            SelectedProfile = _manualProfile;
            _autoSwitching = false;
            Log(L.F("log.backToProfile", _manualProfile.Name));
        }
    }

    [RelayCommand]
    private void AddExecutable()
    {
        var exe = Path.GetFileName(NewExecutable.Trim().Trim('"'));
        if (string.IsNullOrEmpty(exe) || SelectedProfile == null) return;
        if (!exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) exe += ".exe";
        AddExecutableToProfile(SelectedProfile, exe.ToLowerInvariant());
        NewExecutable = "";
    }

    [RelayCommand]
    private void BrowseExecutable()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = L.T("prof.exeFilter") + " (*.exe)|*.exe", Title = L.T("prof.browseTitle") };
        if (dlg.ShowDialog() == true && SelectedProfile != null)
            AddExecutableToProfile(SelectedProfile, Path.GetFileName(dlg.FileName).ToLowerInvariant());
    }

    [RelayCommand]
    private void RemoveExecutable()
    {
        if (SelectedExecutable == null || SelectedProfile == null) return;
        SelectedProfile.Executables.RemoveAll(e => e == SelectedExecutable);
        GameExecutables.Remove(SelectedExecutable);
        _store.Save(SelectedProfile);
    }

    private void AddExecutableToProfile(Profile p, string exe)
    {
        if (p.Executables.Contains(exe, StringComparer.OrdinalIgnoreCase)) return;
        // One game, one profile: remove it from any other profile first.
        foreach (var other in Profiles.Where(o => o != p && o.Executables.RemoveAll(e => string.Equals(e, exe, StringComparison.OrdinalIgnoreCase)) > 0))
            _store.Save(other);
        p.Executables.Add(exe);
        if (p == SelectedProfile) GameExecutables.Add(exe);
        _store.Save(p);
        Log(L.F("log.exeAdded", exe, p.Name));
    }

    // ---- Profiles commands --------------------------------------------

    [RelayCommand]
    private void SaveProfile()
    {
        if (SelectedProfile == null) return;
        _store.Save(SelectedProfile);
        Log(L.F("log.saved", SelectedProfile.Name));
    }

    [RelayCommand]
    private void NewProfile() => CreateProfile(NewProfileName);

    private Profile? CreateProfile(string requestedName)
    {
        var name = string.IsNullOrWhiteSpace(requestedName) ? L.F("prof.defaultName", Profiles.Count + 1) : requestedName.Trim();
        if (Profiles.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            Log(L.F("log.exists", name));
            return null;
        }
        var p = (_manualProfile ?? SelectedProfile ?? new Profile()).Clone();
        p.Name = name;
        p.Executables.Clear(); // a game can only belong to one profile
        _store.Save(p);
        Profiles.Add(p);
        SelectedProfile = p;
        NewProfileName = "";
        Log(L.F("log.created", name));
        return p;
    }

    [RelayCommand]
    private void DeleteProfile()
    {
        if (SelectedProfile == null || Profiles.Count <= 1) return;
        var p = SelectedProfile;
        _store.Delete(p.Name);
        Profiles.Remove(p);
        if (_manualProfile == p) _manualProfile = null;
        SelectedProfile = Profiles[0];
        Log(L.F("log.deleted", p.Name));
    }

    [RelayCommand]
    private void ResetModOverrides()
    {
        foreach (var c in Controllers) c.Session.ResetModOverrides();
        Log(L.T("log.modsReset"));
    }

    [RelayCommand]
    private void OpenProfilesFolder() =>
        Process.Start(new ProcessStartInfo("explorer.exe", _store.Root) { UseShellExecute = true });

    [RelayCommand]
    private void InstallVigem()
    {
        var installer = Path.Combine(AppContext.BaseDirectory, "drivers", "ViGEmBus_1.22.0_x64_x86_arm64.exe");
        if (!File.Exists(installer))
        {
            Process.Start(new ProcessStartInfo("https://github.com/nefarius/ViGEmBus/releases/tag/v1.22.0") { UseShellExecute = true });
            return;
        }
        Process.Start(new ProcessStartInfo(installer) { UseShellExecute = true, Verb = "runas" });
    }

    [RelayCommand]
    private void OpenHidHidePage() =>
        Process.Start(new ProcessStartInfo("https://github.com/nefarius/HidHide/releases") { UseShellExecute = true });

    // ---- Settings -----------------------------------------------------

    private void SettingsEdited(string property)
    {
        _settings.UseIncomingUdp = UseUdp;
        _settings.IncomingUdpPort = UdpPort;
        _settings.UseDsuServer = UseDsu;
        _settings.DsuPort = DsuPort;
        _settings.UseOsc = UseOsc;
        _settings.OscAddress = OscAddress;
        _settings.OscPort = OscPort;
        _settings.AutoSwitchProfiles = AutoSwitchProfiles;
        _settings.MinimizeToTray = MinimizeToTray;
        _settings.StartMinimized = StartMinimized;
        _settings.LaunchOnBoot = LaunchOnBoot;
        _settings.UseHidHide = UseHidHide;
        _settings.EnableShortcuts = EnableShortcuts;
        if (SelectedLanguage != null) _settings.Language = SelectedLanguage.Value;
        _store.SaveSettings(_settings);

        switch (property)
        {
            case nameof(UseUdp) or nameof(UdpPort): RestartUdp(); break;
            case nameof(UseDsu) or nameof(DsuPort): RestartDsu(); break;
            case nameof(UseOsc) or nameof(OscAddress) or nameof(OscPort): RestartOsc(); break;
            case nameof(UseHidHide): ApplyHidHide(); break;
            case nameof(EnableShortcuts):
                foreach (var c in Controllers) c.Session.ShortcutsEnabled = EnableShortcuts;
                break;
            case nameof(SelectedLanguage) when SelectedLanguage != null && SelectedLanguage.Value != L.Language:
                MessageBox.Show(L.T("set.restart"), "AuroraPad", MessageBoxButton.OK, MessageBoxImage.Information);
                break;
            case nameof(LaunchOnBoot):
                try { AutoStart.Set(LaunchOnBoot); }
                catch (Exception ex) { Log(L.F("log.bootError", ex.Message)); }
                break;
            case nameof(AutoSwitchProfiles) when !AutoSwitchProfiles && _autoProfile != null && _manualProfile != null:
                _autoProfile = null;
                SelectedProfile = _manualProfile;
                break;
        }
    }

    private void RestartUdp()
    {
        _udp.Stop();
        if (UseUdp && UdpPort is > 0 and < 65536) _udp.Start("127.0.0.1", UdpPort);
    }

    private void RestartDsu()
    {
        _dsu.Stop();
        if (UseDsu && DsuPort is > 0 and < 65536)
        {
            if (_dsu.Start(DsuPort)) Log(L.F("log.dsuOn", DsuPort));
            else Log(L.F("log.dsuFail", _dsu.LastError));
        }
    }

    private void RestartOsc()
    {
        _osc.Stop();
        if (UseOsc && OscPort is > 0 and < 65536) _osc.Start(OscAddress, OscPort);
    }

    /// <summary>Hides emulated controllers from games (HidHide) and un-hides the rest.</summary>
    private void ApplyHidHide()
    {
        if (!_hidHide.IsInstalled) return;
        var app = Environment.ProcessPath ?? "";
        List<ControllerSession> sessions;
        lock (_sessions) sessions = _sessions.ToList();
        foreach (var s in sessions)
        {
            var id = HidHideManager.ToInstanceId(s.Device.DevicePath);
            if (UseHidHide && s.IsEmulating)
            {
                if (!_hidHide.HiddenIds.Contains(id) && _hidHide.Hide(s.Device.DevicePath, app))
                    Log(L.F("log.hidden", id));
            }
            else if (_hidHide.HiddenIds.Contains(id))
            {
                _hidHide.Unhide([id]);
            }
        }
        _settings.HiddenInstanceIds = _hidHide.HiddenIds.ToList();
        _store.SaveSettings(_settings);
    }

    // ---- Shortcuts ----------------------------------------------------

    private void OnShortcut(ControllerSession session, ShortcutAction action)
    {
        switch (action)
        {
            case ShortcutAction.NextProfile or ShortcutAction.PreviousProfile when Profiles.Count > 1:
                int i = SelectedProfile == null ? 0 : Profiles.IndexOf(SelectedProfile);
                int next = (i + (action == ShortcutAction.NextProfile ? 1 : -1) + Profiles.Count) % Profiles.Count;
                SelectedProfile = Profiles[next];
                session.FlashBattery(TimeSpan.Zero); // refresh output immediately
                Log(L.F("log.scProfile", SelectedProfile.Name));
                break;
            case ShortcutAction.BrightnessUp:
                Brightness = Math.Min(100, Brightness + 10);
                break;
            case ShortcutAction.BrightnessDown:
                Brightness = Math.Max(0, Brightness - 10);
                break;
            case ShortcutAction.ToggleKeyboardMouse:
                Log(session.MappingEnabled ? L.T("log.kmOn") : L.T("log.kmOff"));
                break;
            case ShortcutAction.ToggleVirtualController:
                Log(session.EmulationPaused ? L.T("log.vcOff") : L.T("log.vcOn"));
                ApplyHidHide();
                break;
            case ShortcutAction.ShowBattery:
                Log(L.F("log.battery", session.Device.Input.BatteryPercent));
                break;
        }
    }

    // ---- Misc ---------------------------------------------------------

    private void Log(string msg)
    {
        void Add()
        {
            LogLines.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {msg}");
            while (LogLines.Count > 200) LogLines.RemoveAt(LogLines.Count - 1);
        }
        if (Application.Current.Dispatcher.CheckAccess()) Add();
        else Application.Current.Dispatcher.BeginInvoke(Add);
    }

    public void Dispose()
    {
        _timer.Stop();
        _liveTimer.Stop();
        _foreground.Dispose();
        if (_manualProfile != null) _store.Save(_manualProfile);
        if (SelectedProfile != null) _store.Save(SelectedProfile);
        _udp.Dispose();
        _dsu.Dispose();
        _osc.Dispose();
        _hidHide.Unhide();
        _settings.HiddenInstanceIds.Clear();
        _store.SaveSettings(_settings);
        lock (_sessions) foreach (var s in _sessions) s.Dispose();
        _devices.Dispose();
    }
}
