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

    /// <summary>Raised to show a tray notification (title, message).</summary>
    public event Action<string, string>? Notify;

    private static readonly HashSet<string> AdvancedProfileFields =
    [
        nameof(ShiftButton), nameof(TriggerHalfPull), nameof(TriggerFullPull), nameof(FlickPixels360), nameof(GyroSmoothing),
        nameof(GsEnabled), nameof(GsInvertY), nameof(GsDps), nameof(GsActivation),
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
