using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace NeZha_Desktop.Converters;

public sealed class StringToPointCollectionConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var text = value as string;
        var points = new PointCollection();

        if (string.IsNullOrWhiteSpace(text))
        {
            points.Add(new Point(0, 52));
            points.Add(new Point(220, 52));
            return points;
        }

        var segments = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var segment in segments)
        {
            var xy = segment.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (xy.Length != 2)
            {
                continue;
            }

            if (double.TryParse(xy[0], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var x) &&
                double.TryParse(xy[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var y))
            {
                points.Add(new Point(x, y));
            }
        }

        if (points.Count == 0)
        {
            points.Add(new Point(0, 52));
            points.Add(new Point(220, 52));
        }

        return points;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }
}
