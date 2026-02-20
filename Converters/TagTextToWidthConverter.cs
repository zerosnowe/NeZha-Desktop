using Microsoft.UI.Xaml.Data;
using System.Globalization;

namespace NeZha_Desktop.Converters;

public sealed class TagTextToWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var text = value as string ?? string.Empty;
        var normalized = text.Trim();
        if (normalized.Length == 0)
        {
            return 36d;
        }

        // CJK characters are visually wider than ASCII; use weighted width.
        var weightedLength = 0d;
        foreach (var ch in normalized)
        {
            weightedLength += IsWideCharacter(ch) ? 1.8d : 1d;
        }

        var width = 15d + (weightedLength * 5.6d);
        return Math.Clamp(width, 36d, 120d);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }

    private static bool IsWideCharacter(char ch)
    {
        var category = CharUnicodeInfo.GetUnicodeCategory(ch);
        if (category == UnicodeCategory.OtherLetter)
        {
            return true;
        }

        return ch >= 0x2E80;
    }
}
