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
    /// <summary>Which kind of press fires this mapping.</summary>
    public PressActivator Activator { get; set; } = PressActivator.Press;
    /// <summary>Press once to hold the key/click, press again to release it.</summary>
    public bool Toggle { get; set; }
    /// <summary>0 = main layer, 1 = Shift layer (active while the profile's Shift button is held).</summary>
    public int Layer { get; set; }
}

/// <summary>
/// Press = behaves like the button (held while held). Tap / DoubleTap / LongPress fire only on that
/// gesture; any other use of the button still reaches the virtual controller.
/// </summary>
public enum PressActivator { Press, Tap, DoubleTap, LongPress }

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

public enum MouseSource { None, Gyro, Touchpad, RightStick, LeftStick, FlickStick }

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
    /// <summary>Flick stick: mouse pixels that make a full 360° turn in the game (calibrate per game).</summary>
    public int FlickPixels360 { get; set; } = 3000;
    /// <summary>Gyro mouse: smooth out tiny hand tremors (0 = off, 100 = strongest).</summary>
    public int GyroSmoothing { get; set; } = 40;
}

/// <summary>Gyro added to the virtual controller's right stick, for games without mouse aiming.</summary>
public sealed class GyroStickSettings
{
    public bool Enabled { get; set; }
    /// <summary>Turn speed (degrees per second) that pushes the stick all the way.</summary>
    public int FullDeflectionDps { get; set; } = 120;
    /// <summary>Only active while this button is held (e.g. L2 to aim); None = always.</summary>
    public DualSenseButtons ActivationButton { get; set; } = DualSenseButtons.None;
    public bool InvertY { get; set; }
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
