using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace DownloaderV2.Converters;

public sealed class NullableImageSourceConverter : IValueConverter
{
    private static readonly ImageSourceConverter Converter = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null || value is string text && string.IsNullOrWhiteSpace(text)) return null;

        try
        {
            return Converter.ConvertFrom(null, culture, value);
        }
        catch (Exception ex) when (ex is NotSupportedException or UriFormatException or IOException)
        {
            return DependencyProperty.UnsetValue;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
