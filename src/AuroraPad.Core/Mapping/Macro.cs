namespace AuroraPad.Core.Mapping;

/// <summary>
/// Text macros, e.g. <c>ctrl+c</c>, <c>w hold 300, wait 50, space</c>, <c>lclick, wait 30, lclick</c>.
/// Steps are separated by commas. A step is either <c>wait N</c> (milliseconds) or a chord of
/// keys joined with <c>+</c>, optionally followed by <c>hold N</c> (default 30 ms).
/// </summary>
public sealed class Macro
{
    public abstract record Step;
    public sealed record WaitStep(int Milliseconds) : Step;
    public sealed record ChordStep(IReadOnlyList<MacroKey> Keys, int HoldMs) : Step;

    public readonly record struct MacroKey(MappingTarget Target, ushort Vk);

    public IReadOnlyList<Step> Steps { get; }
    public string Source { get; }

    private Macro(string source, List<Step> steps) { Source = source; Steps = steps; }

    private static readonly Dictionary<string, MacroKey> Aliases = BuildAliases();

    private static Dictionary<string, MacroKey> BuildAliases()
    {
        var d = new Dictionary<string, MacroKey>(StringComparer.OrdinalIgnoreCase);
        void K(ushort vk, params string[] names) { foreach (var n in names) d[n] = new(MappingTarget.Keyboard, vk); }
        void M(MappingTarget t, params string[] names) { foreach (var n in names) d[n] = new(t, 0); }
        for (char c = 'A'; c <= 'Z'; c++) K(c, c.ToString());
        for (char c = '0'; c <= '9'; c++) K(c, c.ToString());
        for (int i = 1; i <= 24; i++) K((ushort)(0x6F + i), $"f{i}");
        K(0xA2, "ctrl", "control", "lctrl"); K(0xA3, "rctrl");
        K(0xA0, "shift", "lshift"); K(0xA1, "rshift");
        K(0xA4, "alt", "lalt"); K(0xA5, "ralt", "altgr");
        K(0x5B, "win", "windows", "meta");
        K(0x20, "space", "espaço", "espaco"); K(0x0D, "enter", "return"); K(0x1B, "esc", "escape");
        K(0x09, "tab"); K(0x08, "backspace"); K(0x2E, "delete", "del"); K(0x2D, "insert", "ins");
        K(0x24, "home"); K(0x23, "end"); K(0x21, "pageup", "pgup"); K(0x22, "pagedown", "pgdn");
        K(0x26, "up", "cima"); K(0x28, "down", "baixo"); K(0x25, "left", "esquerda"); K(0x27, "right", "direita");
        K(0x14, "capslock"); K(0x2C, "printscreen", "prtsc");
        K(0xAD, "mute"); K(0xAE, "volumedown"); K(0xAF, "volumeup");
        K(0xB3, "playpause"); K(0xB0, "nexttrack"); K(0xB1, "prevtrack");
        for (int i = 0; i <= 9; i++) K((ushort)(0x60 + i), $"num{i}");
        K(0xBD, "-", "minus"); K(0xBB, "=", "plus"); K(0xBC, ",", "comma"); K(0xBE, ".", "period");
        M(MappingTarget.MouseLeft, "lclick", "mouse1", "lmb");
        M(MappingTarget.MouseRight, "rclick", "mouse2", "rmb");
        M(MappingTarget.MouseMiddle, "mclick", "mouse3", "mmb");
        M(MappingTarget.Mouse4, "mouse4"); M(MappingTarget.Mouse5, "mouse5");
        M(MappingTarget.WheelUp, "wheelup"); M(MappingTarget.WheelDown, "wheeldown");
        return d;
    }

    public static bool TryParse(string? text, out Macro? macro, out string? error)
    {
        macro = null;
        error = null;
        if (string.IsNullOrWhiteSpace(text)) { error = "macro vazia"; return false; }
        var steps = new List<Step>();
        foreach (var raw in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts[0].Equals("wait", StringComparison.OrdinalIgnoreCase) || parts[0].Equals("esperar", StringComparison.OrdinalIgnoreCase))
            {
                if (parts.Length < 2 || !int.TryParse(parts[1], out int ms) || ms < 0) { error = $"\"{raw}\": tempo inválido"; return false; }
                steps.Add(new WaitStep(Math.Min(ms, 60000)));
                continue;
            }
            int hold = 30;
            if (parts.Length >= 3 && (parts[1].Equals("hold", StringComparison.OrdinalIgnoreCase) || parts[1].Equals("segurar", StringComparison.OrdinalIgnoreCase)))
            {
                if (!int.TryParse(parts[2], out hold) || hold < 0) { error = $"\"{raw}\": tempo inválido"; return false; }
            }
            else if (parts.Length > 1) { error = $"\"{raw}\": use \"tecla hold 100\""; return false; }

            var keys = new List<MacroKey>();
            foreach (var k in parts[0].Split('+', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!Aliases.TryGetValue(k, out var key)) { error = $"tecla desconhecida \"{k}\""; return false; }
                keys.Add(key);
            }
            if (keys.Count == 0) { error = $"\"{raw}\": nenhuma tecla"; return false; }
            steps.Add(new ChordStep(keys, Math.Min(hold, 60000)));
        }
        macro = new Macro(text, steps);
        return true;
    }

    /// <summary>Plays the macro once; cancellable between steps.</summary>
    public async Task PlayAsync(CancellationToken ct)
    {
        foreach (var step in Steps)
        {
            ct.ThrowIfCancellationRequested();
            switch (step)
            {
                case WaitStep w:
                    await Task.Delay(w.Milliseconds, ct);
                    break;
                case ChordStep c:
                    foreach (var k in c.Keys) Send(k, true);
                    try { await Task.Delay(c.HoldMs, ct); }
                    finally { for (int i = c.Keys.Count - 1; i >= 0; i--) Send(c.Keys[i], false); }
                    break;
            }
        }
    }

    private static void Send(MacroKey k, bool down)
    {
        switch (k.Target)
        {
            case MappingTarget.Keyboard: InputSimulator.Key(k.Vk, down); break;
            case MappingTarget.WheelUp when down: InputSimulator.Wheel(1); break;
            case MappingTarget.WheelDown when down: InputSimulator.Wheel(-1); break;
            case MappingTarget.WheelUp or MappingTarget.WheelDown: break;
            default: InputSimulator.MouseButton(k.Target, down); break;
        }
    }
}
