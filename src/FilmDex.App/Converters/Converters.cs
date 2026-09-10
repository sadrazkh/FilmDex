using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace FilmDex.App.Converters;

/// <summary>Shows the pane whose index matches the selected section.</summary>
public sealed class SectionToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var section = value as int? ?? 0;
        var wanted = int.TryParse(parameter as string, out var index) ? index : 0;

        return section == wanted ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

/// <summary>Green for a strong match, amber for a plausible one, grey for the rest.</summary>
public sealed class PercentToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Strong = Frozen(0x2E, 0x7D, 0x32);
    private static readonly SolidColorBrush Likely = Frozen(0x9A, 0x6B, 0x00);
    private static readonly SolidColorBrush Weak = Frozen(0x45, 0x45, 0x4F);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value as int?) switch
        {
            >= 85 => Strong,
            >= 70 => Likely,
            _ => Weak
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;

    private static SolidColorBrush Frozen(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }
}

public sealed class BooleanToVisibilityConverter : IValueConverter
{
    /// <summary>Set to true to hide when the value is true.</summary>
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value as bool? ?? false;
        if (Invert) flag = !flag;

        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        !(value as bool? ?? false);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        !(value as bool? ?? false);
}

/// <summary>Shows a string only when it has something in it.</summary>
public sealed class TextToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
