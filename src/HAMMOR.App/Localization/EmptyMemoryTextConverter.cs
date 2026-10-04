using System.Globalization;
using System.Windows.Data;

namespace HAMMOR.App.Localization;

/// <summary>
/// Chooses the right empty-state message for the memory page: "nothing saved
/// yet" before a search, "no matches" after one.
/// </summary>
/// <remarks>
/// Lives beside the localisation types because its whole job is picking
/// between two resource keys.
/// </remarks>
public sealed class EmptyMemoryTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        LocalizationSource.Instance[value is true ? "Memory.NoResults" : "Memory.Empty"];

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException("One-way only.");
}
