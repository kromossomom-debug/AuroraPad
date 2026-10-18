using CommunityToolkit.Mvvm.ComponentModel;
using AuroraPad.App.Localization;
using AuroraPad.Core.Mapping;
using AuroraPad.Core.Protocol;

namespace AuroraPad.App.ViewModels;

public sealed record Option<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>One controller button in the keyboard/mouse mapping table.</summary>
public sealed partial class MappingRowViewModel : ObservableObject
{
    private readonly Action _changed;
    internal bool Loading;

    public static IReadOnlyList<Option<DualSenseButtons>> AllButtons { get; } =
    [
        new(DualSenseButtons.Cross, L.T("btn.cross")), new(DualSenseButtons.Circle, L.T("btn.circle")),
        new(DualSenseButtons.Square, L.T("btn.square")), new(DualSenseButtons.Triangle, L.T("btn.triangle")),
        new(DualSenseButtons.L1, "L1"), new(DualSenseButtons.R1, "R1"),
        new(DualSenseButtons.L2, L.T("btn.l2")), new(DualSenseButtons.R2, L.T("btn.r2")),
        new(DualSenseButtons.L3, "L3"), new(DualSenseButtons.R3, "R3"),
        new(DualSenseButtons.DPadUp, L.T("btn.up")), new(DualSenseButtons.DPadDown, L.T("btn.down")),
        new(DualSenseButtons.DPadLeft, L.T("btn.left")), new(DualSenseButtons.DPadRight, L.T("btn.right")),
        new(DualSenseButtons.Create, "Create / Share"), new(DualSenseButtons.Options, "Options"),
        new(DualSenseButtons.PS, "PS"), new(DualSenseButtons.Touchpad, L.T("btn.touchpad")),
        new(DualSenseButtons.Mute, L.T("btn.mute")),
        new(DualSenseButtons.LeftFn, L.T("btn.lfn")), new(DualSenseButtons.RightFn, L.T("btn.rfn")),
        new(DualSenseButtons.LeftPaddle, L.T("btn.lpaddle")), new(DualSenseButtons.RightPaddle, L.T("btn.rpaddle")),
    ];

    public static IReadOnlyList<Option<MappingTarget>> Targets { get; } =
    [
        new(MappingTarget.None, L.T("map.controller")), new(MappingTarget.Keyboard, L.T("map.key")),
        new(MappingTarget.Macro, L.T("map.macro")),
        new(MappingTarget.MouseLeft, L.T("map.lclick")), new(MappingTarget.MouseRight, L.T("map.rclick")),
        new(MappingTarget.MouseMiddle, L.T("map.mclick")), new(MappingTarget.Mouse4, L.T("map.mouse4")),
        new(MappingTarget.Mouse5, L.T("map.mouse5")), new(MappingTarget.WheelUp, L.T("map.wheelUp")),
        new(MappingTarget.WheelDown, L.T("map.wheelDown")),
    ];

    public static IReadOnlyList<KeyCatalog.KeyEntry> Keys => KeyCatalog.Keys;

    public DualSenseButtons Button { get; }
    public string Label { get; }

    [ObservableProperty] private Option<MappingTarget> _target = Targets[0];
    [ObservableProperty] private KeyCatalog.KeyEntry? _key;
    [ObservableProperty] private bool _keepOnController;
    [ObservableProperty] private string _macroText = "";
    [ObservableProperty] private bool _repeatWhileHeld;
    [ObservableProperty] private string _macroError = "";

    public bool IsKeyboard => Target.Value == MappingTarget.Keyboard;
    public bool IsMacro => Target.Value == MappingTarget.Macro;
    public bool HasMacroError => IsMacro && MacroError.Length > 0;

    public MappingRowViewModel(Option<DualSenseButtons> button, Action changed)
    {
        Button = button.Value;
        Label = button.Label;
        _changed = changed;
    }

    public void Load(ButtonMapping? m)
    {
        Loading = true;
        Target = Targets.FirstOrDefault(t => t.Value == (m?.Target ?? MappingTarget.None)) ?? Targets[0];
        Key = m == null || m.VirtualKey == 0 ? null : KeyCatalog.Keys.FirstOrDefault(k => k.Vk == m.VirtualKey);
        KeepOnController = m?.KeepOnController ?? false;
        MacroText = m?.Macro ?? "";
        RepeatWhileHeld = m?.RepeatWhileHeld ?? false;
        Loading = false;
        ValidateMacro();
        RaiseVisibility();
    }

    public ButtonMapping? ToMapping() => Target.Value == MappingTarget.None ? null : new ButtonMapping
    {
        Button = Button,
        Target = Target.Value,
        VirtualKey = Key?.Vk ?? 0,
        KeepOnController = KeepOnController,
        Macro = MacroText,
        RepeatWhileHeld = RepeatWhileHeld,
    };

    private void RaiseVisibility()
    {
        OnPropertyChanged(nameof(IsKeyboard));
        OnPropertyChanged(nameof(IsMacro));
        OnPropertyChanged(nameof(HasMacroError));
    }

    private void ValidateMacro()
    {
        MacroError = IsMacro && !Macro.TryParse(MacroText, out _, out var err) ? L.F("map.macroError", err) : "";
        OnPropertyChanged(nameof(HasMacroError));
    }

    partial void OnTargetChanged(Option<MappingTarget> value)
    {
        ValidateMacro();
        RaiseVisibility();
        if (!Loading) _changed();
    }

    partial void OnKeyChanged(KeyCatalog.KeyEntry? value) { if (!Loading) _changed(); }
    partial void OnKeepOnControllerChanged(bool value) { if (!Loading) _changed(); }
    partial void OnRepeatWhileHeldChanged(bool value) { if (!Loading) _changed(); }

    partial void OnMacroTextChanged(string value)
    {
        if (Loading) return;
        ValidateMacro();
        _changed();
    }
}

/// <summary>Checkbox row for the turbo button list.</summary>
public sealed partial class TurboRowViewModel(Option<DualSenseButtons> button, Action changed) : ObservableObject
{
    internal bool Loading;
    public DualSenseButtons Button { get; } = button.Value;
    public string Label { get; } = button.Label;

    [ObservableProperty] private bool _isOn;

    partial void OnIsOnChanged(bool value) { if (!Loading) changed(); }
}
