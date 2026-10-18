using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace AuroraPad.App;

/// <summary>Dot count for the speaker grille drawn on the controller.</summary>
public static class Grille
{
    public static int[] Dots { get; } = Enumerable.Range(0, 21).ToArray();
}

/// <summary>Pressed button → accent colour, released → dark grey.</summary>
public sealed class PressedBrushConverter : IValueConverter
{
    private static readonly Brush On = Freeze(new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4)));
    private static readonly Brush Off = Freeze(new SolidColorBrush(Color.FromRgb(0x3A, 0x3F, 0x4A)));

    private static Brush Freeze(Brush b) { b.Freeze(); return b; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? On : Off;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class InverseBoolVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Offsets a 0-100 coordinate so a dot of the given size (parameter) is centred on it.</summary>
public sealed class CenterDotConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value is double d ? d : 0) - double.Parse((string)parameter, CultureInfo.InvariantCulture) / 2;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
