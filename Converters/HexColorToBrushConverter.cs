using Microsoft.UI;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace NeZha_Desktop.Converters;

public sealed class HexColorToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not string hex || string.IsNullOrWhiteSpace(hex))
        {
            return new SolidColorBrush(Colors.Transparent);
        }

        if (!TryParseHexColor(hex.Trim(), out var color))
        {
            return new SolidColorBrush(Colors.Transparent);
        }

        return new SolidColorBrush(color);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();

    private static bool TryParseHexColor(string hex, out Color color)
    {
        color = Colors.Transparent;
        if (!hex.StartsWith('#'))
        {
            return false;
        }

        var payload = hex[1..];
        if (payload.Length == 6 && uint.TryParse(payload, System.Globalization.NumberStyles.HexNumber, null, out var rgb))
        {
            color = Color.FromArgb(255, (byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF));
            return true;
        }

        if (payload.Length == 8 && uint.TryParse(payload, System.Globalization.NumberStyles.HexNumber, null, out var argb))
        {
            color = Color.FromArgb((byte)((argb >> 24) & 0xFF), (byte)((argb >> 16) & 0xFF), (byte)((argb >> 8) & 0xFF), (byte)(argb & 0xFF));
            return true;
        }

        return false;
    }
}
