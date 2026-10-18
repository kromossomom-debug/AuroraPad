using System.Windows.Markup;

namespace AuroraPad.App.Localization;

/// <summary>
/// UI strings. The language is chosen once at startup (changing it asks for a restart).
/// Missing keys fall back to Portuguese, then to the key itself.
/// </summary>
public static class L
{
    public static IReadOnlyList<(string Code, string Name)> Languages { get; } =
    [
        ("pt-BR", "Português (Brasil)"),
        ("en-US", "English"),
        ("es", "Español"),
    ];

    private static IReadOnlyDictionary<string, string> _current = Strings.PtBr;

    public static string Language { get; private set; } = "pt-BR";

    public static void Load(string code)
    {
        Language = code;
        _current = code switch
        {
            "en-US" => Strings.EnUs,
            "es" => Strings.Es,
            _ => Strings.PtBr,
        };
    }

    public static string T(string key) =>
        _current.TryGetValue(key, out var s) ? s
        : Strings.PtBr.TryGetValue(key, out var pt) ? pt
        : key;

    public static string F(string key, params object?[] args) => string.Format(T(key), args);
}

/// <summary>XAML: <c>Text="{l:T home.title}"</c>.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension(string key) : MarkupExtension
{
    public string Key { get; set; } = key;

    public override object ProvideValue(IServiceProvider serviceProvider) => L.T(Key);
}
