using System.Globalization;

namespace Droppa.Converters;

/// <summary>Returns true when the bound string has content. Used to show/hide labels.</summary>
public class IsStringNotEmptyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        !string.IsNullOrWhiteSpace(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
