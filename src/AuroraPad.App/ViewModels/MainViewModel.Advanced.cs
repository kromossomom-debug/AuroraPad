using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AuroraPad.App.Localization;
using AuroraPad.Core.Mapping;
using AuroraPad.Core.Profiles;
using AuroraPad.Core.Protocol;

namespace AuroraPad.App.ViewModels;

/// <summary>Shift layer, two-stage triggers, flick stick, gyro tools, haptics file playback, battery alerts.</summary>
public sealed partial class MainViewModel
{
    public IReadOnlyList<Option<int>> Layers { get; } =
    [
        new(0, L.T("layer.main")),
        new(1, L.T("layer.shift")),
    ];

    public IReadOnlyList<Option<DualSenseButtons>> ShiftButtons { get; } =
        [new(DualSenseButtons.None, L.T("common.none")), .. MappingRowViewModel.AllButtons.Where(b => MappingRowViewModel.IsPhysical(b.Value))];

    [ObservableProperty] private Option<int>? _editingLayer;
    [ObservableProperty] private Option<DualSenseButtons>? _shiftButton;
    [ObservableProperty] private int _triggerHalfPull, _triggerFullPull;
    [ObservableProperty] private int _flickPixels360, _gyroSmoothing;
    [ObservableProperty] private bool _gsEnabled, _gsInvertY;
    [ObservableProperty] private int _gsDps;
    [ObservableProperty] private Option<DualSenseButtons>? _gsActivation;
    [ObservableProperty] private string _gyroStatus = "";
    [ObservableProperty] private bool _gyroAutoCalibrate;
    [ObservableProperty] private bool _lowBatteryAlert;
    [ObservableProperty] private string _hapticsFileStatus = "";
    [ObservableProperty] private bool _speakerFromPc;
    [ObservableProperty] private bool _gyroPlayerSpace, _gsPlayerSpace;
    [ObservableProperty] private int _gyroAcceleration;
    [ObservableProperty] private int _speakerVolume;
    [ObservableProperty] private string _speakerStatus = "";
    [ObservableProperty] private string _memoryText = "";

    private void UpdateMemory()
    {
        using var p = System.Diagnostics.Process.GetCurrentProcess();
        MemoryText = L.F("set.memory", p.WorkingSet64 / (1024 * 1024), p.PrivateMemorySize64 / (1024 * 1024));
    }

    /// <summary>Raised to show a tray notification (title, message).</summary>
    public event Action<string, string>? Notify;

    private static readonly HashSet<string> AdvancedProfileFields =
    [
        nameof(ShiftButton), nameof(TriggerHalfPull), nameof(TriggerFullPull), nameof(FlickPixels360), nameof(GyroSmoothing),
        nameof(GsEnabled), nameof(GsInvertY), nameof(GsDps), nameof(GsActivation),
        nameof(SpeakerFromPc), nameof(SpeakerVolume), nameof(GyroPlayerSpace), nameof(GsPlayerSpace), nameof(GyroAcceleration),
    ];

    private static readonly HashSet<string> AdvancedSettingFields = [nameof(GyroAutoCalibrate), nameof(LowBatteryAlert)];

    private int CurrentLayer => EditingLayer?.Value ?? 0;

    private void LoadAdvanced(Profile p)
    {
        ShiftButton = ShiftButtons.FirstOrDefault(b => b.Value == p.ShiftButton) ?? ShiftButtons[0];
        TriggerHalfPull = p.TriggerHalfPull; TriggerFullPull = p.TriggerFullPull;
        FlickPixels360 = p.Mouse.FlickPixels360; GyroSmoothing = p.Mouse.GyroSmoothing;
        GsEnabled = p.GyroStick.Enabled; GsInvertY = p.GyroStick.InvertY; GsDps = p.GyroStick.FullDeflectionDps;
        GsActivation = ActivationButtons.FirstOrDefault(b => b.Value == p.GyroStick.ActivationButton) ?? ActivationButtons[0];
        SpeakerFromPc = p.SpeakerFromPc; SpeakerVolume = p.SpeakerVolume;
        GyroPlayerSpace = p.Mouse.GyroPlayerSpace; GyroAcceleration = p.Mouse.GyroAcceleration; GsPlayerSpace = p.GyroStick.PlayerSpace;
        LoadMappingRows(p);
    }

    private void LoadMappingRows(Profile p)
    {
        int layer = CurrentLayer;
        foreach (var row in Mappings)
        {
            row.Layer = layer;
            row.Load(p.Mappings.FirstOrDefault(m => m.Button == row.Button && m.Layer == layer));
        }
    }

    private void WriteAdvanced(Profile p)
    {
        p.ShiftButton = ShiftButton?.Value ?? DualSenseButtons.None;
        p.TriggerHalfPull = TriggerHalfPull; p.TriggerFullPull = Math.Max(TriggerFullPull, TriggerHalfPull + 1);
        p.Mouse.FlickPixels360 = FlickPixels360; p.Mouse.GyroSmoothing = GyroSmoothing;
        p.GyroStick.Enabled = GsEnabled; p.GyroStick.InvertY = GsInvertY; p.GyroStick.FullDeflectionDps = GsDps;
        p.GyroStick.ActivationButton = GsActivation?.Value ?? DualSenseButtons.None;
        p.SpeakerFromPc = SpeakerFromPc; p.SpeakerVolume = SpeakerVolume;
        p.Mouse.GyroPlayerSpace = GyroPlayerSpace; p.Mouse.GyroAcceleration = GyroAcceleration; p.GyroStick.PlayerSpace = GsPlayerSpace;
    }

    /// <summary>Rows show one layer; the other layer's mappings are kept as they are.</summary>
    private List<ButtonMapping> MergeMappingRows(Profile p) =>
        p.Mappings.Where(m => m.Layer != CurrentLayer)
            .Concat(Mappings.Select(r => r.ToMapping()).OfType<ButtonMapping>())
            .ToList();

    partial void OnEditingLayerChanged(Option<int>? value)
    {
        if (SelectedProfile == null) return;
        _loading = true;
        LoadMappingRows(SelectedProfile);
        _loading = false;
    }

    /// <summary>Topics of the in-app guide (title, text).</summary>
    public IReadOnlyList<GuideTopic> GuideTopics { get; } =
        Enumerable.Range(1, 10).Select(n => new GuideTopic(L.T($"guide.{n}.t"), L.T($"guide.{n}.b"))).ToList();

    // ---- Background mode ------------------------------------------------------

    [System.Runtime.InteropServices.DllImport("psapi.dll")]
    private static extern bool EmptyWorkingSet(IntPtr process);

    public bool IsInBackground { get; private set; }

    /// <summary>
    /// While the window is hidden or minimized nothing needs drawing: stop the per-frame refresh,
    /// slow the status timer down and hand the UI's memory back to Windows. Controllers, emulation,
    /// mappings, haptics and servers keep running.
    /// </summary>
    public void SetBackground(bool background)
    {
        if (background == IsInBackground) return;
        IsInBackground = background;
        if (background)
        {
            _liveTimer.Stop();
            _timer.Interval = TimeSpan.FromSeconds(5);
            _ = Task.Run(async () =>
            {
                await Task.Delay(1500); // let WPF release the hidden window's render resources first
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                EmptyWorkingSet(System.Diagnostics.Process.GetCurrentProcess().Handle);
            });
        }
        else
        {
            _timer.Interval = TimeSpan.FromMilliseconds(250);
            _liveTimer.Start();
        }
    }
    // ---- Gyro --------------------------------------------------------------

    [RelayCommand]
    private async Task CalibrateGyro()
    {
        var s = SelectedController?.Session;
        if (s == null) { GyroStatus = L.T("gyro.noController"); return; }
        GyroStatus = L.T("gyro.calibrating");
        bool ok = await s.CalibrateGyroAsync();
        GyroStatus = ok ? L.T("gyro.done") : L.T("gyro.timeout");
    }

    // ---- Haptics file --------------------------------------------------------

    [RelayCommand]
    private void PlayHapticsFile()
    {
        var s = SelectedController?.Session;
        if (s == null) return;
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = L.T("hap.fileFilter") + " (*.wav;*.mp3;*.m4a;*.wma;*.aac)|*.wav;*.mp3;*.m4a;*.wma;*.aac", Title = L.T("hap.fileTitle") };
        if (dlg.ShowDialog() != true) return;
        var err = s.PlayHapticsFile(dlg.FileName);
        HapticsFileStatus = err == null ? L.F("hap.filePlaying", Path.GetFileName(dlg.FileName)) : L.F("ctl.hapticsError", err);
    }

    // ---- Battery alerts -------------------------------------------------------

    private readonly Dictionary<ControllerViewModel, int> _batteryAlerted = [];

    private void CheckBattery()
    {
        if (!LowBatteryAlert) return;
        foreach (var c in Controllers)
        {
            var input = c.Session.Device.Input;
            if (input.Charging is ChargingState.Charging or ChargingState.Full) { _batteryAlerted.Remove(c); continue; }
            int level = input.BatteryPercent <= 10 ? 10 : input.BatteryPercent <= 20 ? 20 : 100;
            if (level == 100 || input.BatteryPercent == 0) continue;
            if (_batteryAlerted.TryGetValue(c, out var alerted) && alerted <= level) continue;
            _batteryAlerted[c] = level;
            Notify?.Invoke("AuroraPad", L.F("battery.low", c.Name, input.BatteryPercent));
            Log(L.F("battery.low", c.Name, input.BatteryPercent));
        }
    }

    private void AdvancedSettingsEdited(string property)
    {
        _settings.GyroAutoCalibrate = GyroAutoCalibrate;
        _settings.LowBatteryAlert = LowBatteryAlert;
        _store.SaveSettings(_settings);
        if (property == nameof(GyroAutoCalibrate))
            foreach (var c in Controllers) c.Session.GyroAutoCalibrate = GyroAutoCalibrate;
    }
}

/// <summary>One card of the in-app guide.</summary>
public sealed record GuideTopic(string Title, string Body);
