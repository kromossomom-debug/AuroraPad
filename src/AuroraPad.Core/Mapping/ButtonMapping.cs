using AuroraPad.Core.Protocol;

namespace AuroraPad.Core.Mapping;

public enum MappingTarget
{
    None,
    Keyboard,
    MouseLeft,
    MouseRight,
    MouseMiddle,
    Mouse4,
    Mouse5,
    WheelUp,
    WheelDown,
    Macro,
}

public sealed class ButtonMapping
{
    public DualSenseButtons Button { get; set; }
    public MappingTarget Target { get; set; }
    /// <summary>Windows virtual-key code when <see cref="Target"/> is Keyboard.</summary>
    public ushort VirtualKey { get; set; }
    /// <summary>Also keep the button on the virtual controller.</summary>
    public bool KeepOnController { get; set; }
    /// <summary>Macro text when <see cref="Target"/> is Macro (see <see cref="Mapping.Macro"/>).</summary>
    public string Macro { get; set; } = "";
    /// <summary>Macro keeps repeating while the button is held.</summary>
    public bool RepeatWhileHeld { get; set; }
}

/// <summary>App actions triggered by holding PS and pressing another button.</summary>
public enum ShortcutAction
{
    NextProfile,
    PreviousProfile,
    ToggleKeyboardMouse,
    ToggleVirtualController,
    BrightnessUp,
    BrightnessDown,
    ShowBattery,
}

public enum MouseSource { None, Gyro, Touchpad, RightStick, LeftStick }

public sealed class MouseSettings
{
    public MouseSource Source { get; set; } = MouseSource.None;
    /// <summary>1-100; meaning depends on source.</summary>
    public double Sensitivity { get; set; } = 30;
    /// <summary>Gyro mouse only moves while this button is held; None = always.</summary>
    public DualSenseButtons ActivationButton { get; set; } = DualSenseButtons.None;
    public bool InvertX { get; set; }
    public bool InvertY { get; set; }
    /// <summary>Stick used as mouse is removed from the virtual controller.</summary>
    public bool SuppressStick { get; set; } = true;
}

/// <summary>Names for keys shown in the mapping UI.</summary>
public static class KeyCatalog
{
    public sealed record KeyEntry(ushort Vk, string Name)
    {
        public override string ToString() => Name;
    }

    public static IReadOnlyList<KeyEntry> Keys { get; } = Build();

    public static string NameOf(ushort vk) => Keys.FirstOrDefault(k => k.Vk == vk)?.Name ?? $"VK 0x{vk:X2}";

    private static List<KeyEntry> Build()
    {
        var l = new List<KeyEntry>();
        for (char c = 'A'; c <= 'Z'; c++) l.Add(new(c, c.ToString()));
        for (char c = '0'; c <= '9'; c++) l.Add(new(c, c.ToString()));
        for (int i = 1; i <= 12; i++) l.Add(new((ushort)(0x6F + i), $"F{i}"));
        l.AddRange(
        [
            new(0x20, "Espaço"), new(0x0D, "Enter"), new(0x1B, "Esc"), new(0x09, "Tab"),
            new(0x08, "Backspace"), new(0xA0, "Shift esquerdo"), new(0xA1, "Shift direito"),
            new(0xA2, "Ctrl esquerdo"), new(0xA3, "Ctrl direito"), new(0xA4, "Alt esquerdo"),
            new(0x14, "Caps Lock"), new(0x26, "Seta para cima"), new(0x28, "Seta para baixo"),
            new(0x25, "Seta para a esquerda"), new(0x27, "Seta para a direita"),
            new(0x2D, "Insert"), new(0x2E, "Delete"), new(0x24, "Home"), new(0x23, "End"),
            new(0x21, "Page Up"), new(0x22, "Page Down"), new(0x2C, "Print Screen"),
            new(0x5B, "Windows"), new(0xC0, "' (apóstrofo)"), new(0xBD, "-"), new(0xBB, "="),
            new(0xDB, "´"), new(0xDD, "["), new(0xBA, "Ç"), new(0xBC, ","), new(0xBE, "."),
            new(0xBF, ";"), new(0x60, "Num 0"), new(0x61, "Num 1"), new(0x62, "Num 2"),
            new(0x63, "Num 3"), new(0x64, "Num 4"), new(0x65, "Num 5"), new(0x66, "Num 6"),
            new(0x67, "Num 7"), new(0x68, "Num 8"), new(0x69, "Num 9"),
            new(0xAD, "Mudo (mídia)"), new(0xAE, "Volume -"), new(0xAF, "Volume +"),
            new(0xB3, "Play/Pause"), new(0xB0, "Próxima faixa"), new(0xB1, "Faixa anterior"),
        ]);
        return l;
    }
}
