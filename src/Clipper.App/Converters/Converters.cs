using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Clipper.App;

/// <summary>true/false → one of two strings passed as "onText|offText".</summary>
public sealed class BoolTextConverter : IValueConverter
{
    public static readonly BoolTextConverter OnOff = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var parts = (parameter as string ?? "On|Off").Split('|');
        bool b = value is true;
        return b ? parts[0] : (parts.Length > 1 ? parts[1] : parts[0]);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>int count == 0 → Visible, else Collapsed (for empty-state UI).</summary>
public sealed class CountToVisibilityConverter : IValueConverter
{
    public static readonly CountToVisibilityConverter ZeroVisible = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => (value is int i && i == 0) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Returns "active" (for the NavButton Tag) when the bool matches the target, else null.</summary>
public sealed class ActiveTagConverter : IValueConverter
{
    public static readonly ActiveTagConverter WhenTrue = new() { _target = true };
    public static readonly ActiveTagConverter WhenFalse = new() { _target = false };
    private bool _target;
    public object? Convert(object? value, Type t, object? p, CultureInfo c) => (value is true) == _target ? "active" : null;
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>bool true → Visible, false → Collapsed.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public static readonly BoolToVisibilityConverter Instance = new();
    public object Convert(object? value, Type t, object? p, CultureInfo c) => value is true ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => value is Visibility.Visible;
}

/// <summary>bool true → Collapsed, false → Visible.</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public static readonly InverseBoolToVisibilityConverter Instance = new();
    public object Convert(object? value, Type t, object? p, CultureInfo c) => value is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => value is not Visibility.Visible;
}

/// <summary>Inverts a bool (for IsEnabled = !Exporting etc.).</summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public static readonly InverseBoolConverter Instance = new();
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}

/// <summary>Non-empty string → Visible, else Collapsed.</summary>
public sealed class NotEmptyToVisibilityConverter : IValueConverter
{
    public static readonly NotEmptyToVisibilityConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => !string.IsNullOrEmpty(value as string) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
