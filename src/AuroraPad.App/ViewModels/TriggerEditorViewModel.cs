using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AuroraPad.App.Localization;
using AuroraPad.Core.Triggers;

namespace AuroraPad.App.ViewModels;

public sealed record ModeOption(TriggerMode Mode, string Label);

/// <summary>One of the 10 zones of trigger travel in the resistance graph.</summary>
public sealed partial class ZoneBar : ObservableObject
{
    [ObservableProperty] private double _height;
    [ObservableProperty] private bool _vibrates;
    [ObservableProperty] private bool _reached;
}

/// <summary>Edits one <see cref="TriggerSettings"/> and reports every change.</summary>
public sealed partial class TriggerEditorViewModel : ObservableObject
{
    private readonly Action _changed;
    private bool _loading;

    public string Title { get; }

    public static IReadOnlyList<ModeOption> Modes { get; } =
    [
        new(TriggerMode.Normal, L.T("tm.normal")),
        new(TriggerMode.Resistance, L.T("tm.resistance")),
        new(TriggerMode.SlopeFeedback, L.T("tm.slope")),
        new(TriggerMode.SemiAutomaticGun, L.T("tm.semi")),
        new(TriggerMode.AutomaticGun, L.T("tm.auto")),
        new(TriggerMode.Bow, L.T("tm.bow")),
        new(TriggerMode.Galloping, L.T("tm.galloping")),
        new(TriggerMode.Machine, L.T("tm.machine")),
        new(TriggerMode.VibrateTrigger, L.T("tm.vibration")),
        new(TriggerMode.VibrateTriggerPulse, L.T("tm.pulse")),
        new(TriggerMode.Choppy, L.T("tm.choppy")),
        new(TriggerMode.GameCube, "GameCube"),
        new(TriggerMode.VerySoft, L.T("tm.verySoft")),
        new(TriggerMode.Soft, L.T("tm.soft")),
        new(TriggerMode.Medium, L.T("tm.medium")),
        new(TriggerMode.Hard, L.T("tm.hard")),
        new(TriggerMode.VeryHard, L.T("tm.veryHard")),
        new(TriggerMode.Hardest, L.T("tm.hardest")),
        new(TriggerMode.Rigid, L.T("tm.rigid")),
    ];

    public TriggerSettings Settings { get; private set; } = new();

    [ObservableProperty] private ModeOption _selectedMode = Modes[0];
    [ObservableProperty] private int _start;
    [ObservableProperty] private int _end;
    [ObservableProperty] private int _strength;
    [ObservableProperty] private int _strengthB;
    [ObservableProperty] private int _snapForce;
    [ObservableProperty] private int _frequency;
    [ObservableProperty] private int _period;
    [ObservableProperty] private int _firstFoot;
    [ObservableProperty] private int _secondFoot;
    [ObservableProperty] private int _threshold;

    public TriggerEditorViewModel(string title, Action changed)
    {
        Title = title;
        _changed = changed;
        for (int i = 0; i < 10; i++) Zones.Add(new ZoneBar());
    }

    /// <summary>Resistance per zone (0 = released, 9 = fully pressed) for the graph.</summary>
    public ObservableCollection<ZoneBar> Zones { get; } = [];

    /// <summary>Live trigger press 0-1, drawn as a marker over the graph.</summary>
    [ObservableProperty] private double _livePress;
    [ObservableProperty] private double _liveMarker;

    public void SetLive(byte raw)
    {
        LivePress = raw / 255.0;
        LiveMarker = LivePress * 300; // graph is 300 px wide
        int reached = (int)Math.Ceiling(LivePress * 10);
        for (int i = 0; i < 10; i++) Zones[i].Reached = i < reached;
    }

    private void UpdateGraph()
    {
        var (force, vibrates) = Settings.ToEffect().ZoneProfile();
        for (int i = 0; i < 10; i++)
        {
            Zones[i].Height = 4 + force[i] / 8.0 * 56; // 60 px tall graph
            Zones[i].Vibrates = vibrates && force[i] > 0;
        }
    }

    /// <summary>Ready-made effects that feel like common game situations.</summary>
    [RelayCommand]
    private void ApplyPreset(string name)
    {
        _loading = true;
        (SelectedMode, Start, End, Strength, StrengthB, SnapForce, Frequency, Period, FirstFoot, SecondFoot) = name switch
        {
            "pistol" => (Mode(TriggerMode.SemiAutomaticGun), 4, 6, 8, 0, 4, 20, 10, 2, 6),
            "rifle" => (Mode(TriggerMode.AutomaticGun), 3, 9, 6, 0, 4, 12, 10, 2, 6),
            "bow" => (Mode(TriggerMode.Bow), 1, 6, 6, 0, 8, 20, 10, 2, 6),
            "brake" => (Mode(TriggerMode.SlopeFeedback), 0, 8, 1, 8, 4, 20, 10, 2, 6),
            "accel" => (Mode(TriggerMode.Resistance), 2, 9, 3, 0, 4, 20, 10, 2, 6),
            "horse" => (Mode(TriggerMode.Galloping), 0, 9, 5, 0, 4, 4, 10, 2, 6),
            "machine" => (Mode(TriggerMode.Machine), 1, 9, 7, 3, 4, 12, 5, 2, 6),
            "rough" => (Mode(TriggerMode.VibrateTrigger), 0, 9, 8, 0, 4, 30, 10, 2, 6),
            _ => (Mode(TriggerMode.Normal), Start, End, Strength, StrengthB, SnapForce, Frequency, Period, FirstFoot, SecondFoot),
        };
        _loading = false;
        RaiseVisibility();
        Push();
    }

    private static ModeOption Mode(TriggerMode m) => Modes.First(o => o.Mode == m);

    public void Load(TriggerSettings s)
    {
        _loading = true;
        Settings = s;
        SelectedMode = Modes.FirstOrDefault(m => m.Mode == s.Mode) ?? Modes[0];
        Start = s.Start; End = s.End; Strength = s.Strength; StrengthB = s.StrengthB;
        SnapForce = s.SnapForce; Frequency = s.Frequency; Period = s.Period;
        FirstFoot = s.FirstFoot; SecondFoot = s.SecondFoot; Threshold = s.Threshold;
        _loading = false;
        RaiseVisibility();
        UpdateGraph();
    }

    private TriggerMode M => SelectedMode.Mode;
    public bool ShowStart => M is TriggerMode.Resistance or TriggerMode.SlopeFeedback or TriggerMode.SemiAutomaticGun
        or TriggerMode.AutomaticGun or TriggerMode.Bow or TriggerMode.Galloping or TriggerMode.Machine;
    public bool ShowEnd => M is TriggerMode.SlopeFeedback or TriggerMode.SemiAutomaticGun or TriggerMode.Bow
        or TriggerMode.Galloping or TriggerMode.Machine;
    public bool ShowStrength => M is TriggerMode.Resistance or TriggerMode.SlopeFeedback or TriggerMode.SemiAutomaticGun
        or TriggerMode.AutomaticGun or TriggerMode.Bow or TriggerMode.Machine;
    public bool ShowStrengthB => M is TriggerMode.SlopeFeedback or TriggerMode.Machine;
    public bool ShowSnapForce => M is TriggerMode.Bow;
    public bool ShowFrequency => M is TriggerMode.AutomaticGun or TriggerMode.Galloping or TriggerMode.Machine
        or TriggerMode.VibrateTrigger or TriggerMode.VibrateTriggerPulse;
    public bool ShowPeriod => M is TriggerMode.Machine;
    public bool ShowFeet => M is TriggerMode.Galloping;
    public string StrengthBLabel => M == TriggerMode.SlopeFeedback ? L.T("trg.endStrength") : L.T("trg.strengthB");

    private void RaiseVisibility()
    {
        foreach (var n in new[] { nameof(ShowStart), nameof(ShowEnd), nameof(ShowStrength), nameof(ShowStrengthB),
                     nameof(ShowSnapForce), nameof(ShowFrequency), nameof(ShowPeriod), nameof(ShowFeet), nameof(StrengthBLabel) })
            OnPropertyChanged(n);
    }

    partial void OnSelectedModeChanged(ModeOption value) { RaiseVisibility(); Push(); }
    partial void OnStartChanged(int value) => Push();
    partial void OnEndChanged(int value) => Push();
    partial void OnStrengthChanged(int value) => Push();
    partial void OnStrengthBChanged(int value) => Push();
    partial void OnSnapForceChanged(int value) => Push();
    partial void OnFrequencyChanged(int value) => Push();
    partial void OnPeriodChanged(int value) => Push();
    partial void OnFirstFootChanged(int value) => Push();
    partial void OnSecondFootChanged(int value) => Push();
    partial void OnThresholdChanged(int value) => Push();

    private void Push()
    {
        if (_loading) return;
        var s = Settings;
        s.Mode = SelectedMode.Mode;
        s.Start = Start; s.End = End; s.Strength = Strength; s.StrengthB = StrengthB;
        s.SnapForce = SnapForce; s.Frequency = Frequency; s.Period = Period;
        s.FirstFoot = FirstFoot; s.SecondFoot = SecondFoot; s.Threshold = Threshold;
        UpdateGraph();
        _changed();
    }
}
