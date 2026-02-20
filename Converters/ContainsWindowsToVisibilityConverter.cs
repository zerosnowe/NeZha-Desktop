using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace NeZha_Desktop.Converters;

public sealed class ContainsWindowsToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var text = value as string;
        if (!string.IsNullOrWhiteSpace(text) &&
            text.Contains("windows", StringComparison.OrdinalIgnoreCase))
        {
            return Visibility.Visible;
        }

        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }
}
