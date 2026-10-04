using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using HAMMOR.App.Localization;
using HAMMOR.Core.Ai;
using HAMMOR.Core.Tasks;

namespace HAMMOR.App.Converters;

/// <summary>
/// Maps a <see cref="ProviderState"/> onto one of the status brushes defined
/// in Tokens.xaml, so status colour stays centralised.
/// </summary>
public sealed class ProviderStateToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = value switch
        {
            ProviderAvailability availability => StatusKey(availability.State),
            ProviderState state => StatusKey(state),
            _ => "StatusNeutralBrush",
        };

        return Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException("One-way only.");

    private static string StatusKey(ProviderState state) => state switch
    {
        ProviderState.Ready => "StatusSuccessBrush",
        ProviderState.Error => "StatusErrorBrush",
        _ => "StatusNeutralBrush",
    };
}

/// <summary>
/// Renders a <see cref="ProviderAvailability"/> as a localised label:
/// "Connected", "Not Configured" or "Error".
/// </summary>
public sealed class ProviderStateToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var state = value switch
        {
            ProviderAvailability availability => availability.State,
            ProviderState s => s,
            _ => ProviderState.NotConfigured,
        };

        var key = state switch
        {
            ProviderState.Ready => "Status.Connected",
            ProviderState.Error => "Status.Error",
            _ => "Common.NotConfigured",
        };

        return LocalizationSource.Instance[key];
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException("One-way only.");
}

/// <summary>True maps to the success brush, false to neutral.</summary>
public sealed class BooleanToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = value is true ? "StatusSuccessBrush" : "StatusNeutralBrush";
        return Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException("One-way only.");
}

/// <summary>True maps to Visible, false to Collapsed.</summary>
public sealed class BooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility.Visible;
}

/// <summary>True maps to Collapsed, false to Visible.</summary>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility.Collapsed;
}

/// <summary>Maps a <see cref="TaskState"/> onto a status brush.</summary>
public sealed class TaskStateToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = value is TaskState state
            ? state switch
            {
                TaskState.Completed => "StatusSuccessBrush",
                TaskState.Running => "StatusWarningBrush",
                TaskState.Failed => "StatusErrorBrush",
                _ => "StatusNeutralBrush",
            }
            : "StatusNeutralBrush";

        return Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException("One-way only.");
}

/// <summary>
/// Looks an enum value up in the string resources using a key prefix supplied
/// as the converter parameter, e.g. prefix <c>Tasks.State</c> plus value
/// <c>Running</c> resolves <c>Tasks.State.Running</c>.
/// </summary>
/// <remarks>
/// Keeps enum display names in the resx files rather than in C# switch
/// statements, so they are translated like every other string.
/// </remarks>
public sealed class EnumToLocalizedTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is null)
        {
            return string.Empty;
        }

        var prefix = parameter as string;

        return string.IsNullOrWhiteSpace(prefix)
            ? value.ToString() ?? string.Empty
            : LocalizationSource.Instance[$"{prefix}.{value}"];
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException("One-way only.");
}
