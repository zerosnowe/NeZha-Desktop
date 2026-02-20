using Microsoft.UI;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace NeZha_Desktop.Converters;

public sealed class TagTextToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush DefaultBlue = new(Color.FromArgb(0xFF, 0x1F, 0x78, 0xD1));
    private static readonly SolidColorBrush LineBlue = new(Color.FromArgb(0xFF, 0x1A, 0x55, 0xB5));
    private static readonly SolidColorBrush SpeedBlue = new(Color.FromArgb(0xFF, 0x2C, 0x7F, 0xE0));
    private static readonly SolidColorBrush Green = new(Color.FromArgb(0xFF, 0x15, 0xB5, 0x6D));
    private static readonly SolidColorBrush Purple = new(Color.FromArgb(0xFF, 0xA0, 0x3B, 0xD7));
    private static readonly SolidColorBrush Gray = new(Color.FromArgb(0xFF, 0x56, 0x63, 0x79));
    private static readonly SolidColorBrush Orange = new(Color.FromArgb(0xFF, 0xD0, 0x7B, 0x1C));

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var text = (value as string ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(text))
        {
            return DefaultBlue;
        }

        if (ContainsAny(text, "ipv6"))
        {
            return Purple;
        }

        if (ContainsAny(text, "waf", "cdn", "\u9632\u62a4"))
        {
            return Gray;
        }

        if (ContainsAny(text, "bps", "mbps", "gbps"))
        {
            return SpeedBlue;
        }

        if (ContainsAny(text, "lan", "\u8054\u901a", "\u7535\u4fe1", "\u79fb\u52a8"))
        {
            return LineBlue;
        }

        if (ContainsAny(text, "\u5bb6\u5bbd", "\u4e2d\u8f6c", "\u63a2\u9488", "\u8282\u70b9"))
        {
            return Orange;
        }

        if (ContainsAny(text, "infinitas", "premium", "pro"))
        {
            return Green;
        }

        return DefaultBlue;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }

    private static bool ContainsAny(string text, params string[] keywords)
    {
        foreach (var keyword in keywords)
        {
            if (text.Contains(keyword))
            {
                return true;
            }
        }

        return false;
    }
}
