using Microsoft.UI.Xaml;
using NeZha_Desktop.Infrastructure.Runtime;
using System.Text.Json;
using Windows.Storage;

namespace NeZha_Desktop.Infrastructure.Settings;

public static class ThemePreferenceStore
{
    private const string ThemeKey = "AppThemePreference";
    private const string FileName = "ui-settings.json";

    private sealed class ThemeStore
    {
        public string Theme { get; set; } = "Default";
    }

    public static ElementTheme LoadTheme()
    {
        try
        {
            if (AppEnvironment.IsPackaged)
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (settings.Values.TryGetValue(ThemeKey, out var value) && value is string raw)
                {
                    return ParseTheme(raw);
                }

                return ElementTheme.Default;
            }

            var path = Path.Combine(AppEnvironment.GetAppDataDirectory(), FileName);
            if (!File.Exists(path))
            {
                return ElementTheme.Default;
            }

            var json = File.ReadAllText(path);
            var store = JsonSerializer.Deserialize<ThemeStore>(json);
            return ParseTheme(store?.Theme);
        }
        catch
        {
            return ElementTheme.Default;
        }
    }

    public static void SaveTheme(ElementTheme theme)
    {
        try
        {
            var raw = ToRaw(theme);
            if (AppEnvironment.IsPackaged)
            {
                var settings = ApplicationData.Current.LocalSettings;
                settings.Values[ThemeKey] = raw;
                return;
            }

            var path = Path.Combine(AppEnvironment.GetAppDataDirectory(), FileName);
            var json = JsonSerializer.Serialize(new ThemeStore { Theme = raw });
            File.WriteAllText(path, json);
        }
        catch
        {
            // Ignore storage failures; theme still applies for current session.
        }
    }

    private static ElementTheme ParseTheme(string? raw)
    {
        return raw switch
        {
            "Light" => ElementTheme.Light,
            "Dark" => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
    }

    private static string ToRaw(ElementTheme theme)
    {
        return theme switch
        {
            ElementTheme.Light => "Light",
            ElementTheme.Dark => "Dark",
            _ => "Default",
        };
    }
}
