using Microsoft.UI;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace NeZha_Desktop.Converters;

public sealed class OnlineStatusBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush OnlineBrush = new(Colors.LimeGreen);
    private static readonly SolidColorBrush OfflineBrush = new(Colors.IndianRed);

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var isOnline = value is bool b && b;
        return isOnline ? OnlineBrush : OfflineBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }
}
