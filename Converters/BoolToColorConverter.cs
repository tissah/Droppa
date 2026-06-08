using System.Globalization;

namespace Droppa.Converters;

/// <summary>
/// Maps a bool to one of two colours. Used by the order summary to colour completed steps
/// (TrueColor) differently from steps not yet reached (FalseColor).
/// </summary>
public class BoolToColorConverter : IValueConverter
{
    public Color TrueColor { get; set; } = Colors.MediumVioletRed;
    public Color FalseColor { get; set; } = Colors.Gray;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? TrueColor : FalseColor;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
