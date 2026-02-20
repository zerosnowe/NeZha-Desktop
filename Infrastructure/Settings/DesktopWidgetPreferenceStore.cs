using NeZha_Desktop.Infrastructure.Runtime;
using System.Text.Json;
using Windows.Storage;

namespace NeZha_Desktop.Infrastructure.Settings;

public static class DesktopWidgetPreferenceStore
{
    private const string PackagedKey = "DesktopWidgetEnabled";
    private const string PackagedBackdropKey = "DesktopWidgetBackdropMode";
    private const string PackagedCustomBackgroundKey = "DesktopWidgetCustomBackgroundPath";
    private const string PackagedKeepVisualConsistencyKey = "DesktopWidgetKeepVisualConsistencyOnDeactivate";
    private const string PackagedXKey = "DesktopWidgetX";
    private const string PackagedYKey = "DesktopWidgetY";
    private const string PackagedWidthKey = "DesktopWidgetWidth";
    private const string PackagedHeightKey = "DesktopWidgetHeight";
    private const string FileName = "desktop-widget.json";

    private sealed class Store
    {
        public bool Enabled { get; set; }
        public string BackdropMode { get; set; } = "Mica";
        public string? CustomBackgroundPath { get; set; }
        public bool KeepVisualConsistencyOnDeactivate { get; set; } = true;
        public int? X { get; set; }
        public int? Y { get; set; }
        public int? Width { get; set; }
        public int? Height { get; set; }
    }

    public readonly record struct WidgetBounds(int X, int Y, int Width, int Height);

    public static bool LoadEnabled()
    {
        try
        {
            if (AppEnvironment.IsPackaged)
            {
                var settings = ApplicationData.Current.LocalSettings;
                return settings.Values.TryGetValue(PackagedKey, out var value) && value is true;
            }

            var path = Path.Combine(AppEnvironment.GetAppDataDirectory(), FileName);
            if (!File.Exists(path))
            {
                return false;
            }

            var json = File.ReadAllText(path);
            var store = JsonSerializer.Deserialize<Store>(json);
            return store?.Enabled == true;
        }
        catch
        {
            return false;
        }
    }

    public static void SaveEnabled(bool enabled)
    {
        try
        {
            if (AppEnvironment.IsPackaged)
            {
                ApplicationData.Current.LocalSettings.Values[PackagedKey] = enabled;
                return;
            }

            var path = Path.Combine(AppEnvironment.GetAppDataDirectory(), FileName);
            var store = LoadStore(path);
            store.Enabled = enabled;
            var json = JsonSerializer.Serialize(store);
            File.WriteAllText(path, json);
        }
        catch
        {
            // ignore
        }
    }

    public static string LoadBackdropMode()
    {
        try
        {
            if (AppEnvironment.IsPackaged)
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (settings.Values.TryGetValue(PackagedBackdropKey, out var value) && value is string raw && !string.IsNullOrWhiteSpace(raw))
                {
                    return NormalizeMode(raw);
                }

                return "Mica";
            }

            var path = Path.Combine(AppEnvironment.GetAppDataDirectory(), FileName);
            if (!File.Exists(path))
            {
                return "Mica";
            }

            var store = LoadStore(path);
            return NormalizeMode(store.BackdropMode);
        }
        catch
        {
            return "Mica";
        }
    }

    public static void SaveBackdropMode(string mode)
    {
        var normalized = NormalizeMode(mode);
        try
        {
            if (AppEnvironment.IsPackaged)
            {
                ApplicationData.Current.LocalSettings.Values[PackagedBackdropKey] = normalized;
                return;
            }

            var path = Path.Combine(AppEnvironment.GetAppDataDirectory(), FileName);
            var store = LoadStore(path);
            store.BackdropMode = normalized;
            var json = JsonSerializer.Serialize(store);
            File.WriteAllText(path, json);
        }
        catch
        {
            // ignore
        }
    }

    public static string? LoadCustomBackgroundPath()
    {
        try
        {
            if (AppEnvironment.IsPackaged)
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (settings.Values.TryGetValue(PackagedCustomBackgroundKey, out var value) && value is string path && !string.IsNullOrWhiteSpace(path))
                {
                    return path;
                }

                return null;
            }

            var pathFile = Path.Combine(AppEnvironment.GetAppDataDirectory(), FileName);
            if (!File.Exists(pathFile))
            {
                return null;
            }

            var store = LoadStore(pathFile);
            return string.IsNullOrWhiteSpace(store.CustomBackgroundPath) ? null : store.CustomBackgroundPath;
        }
        catch
        {
            return null;
        }
    }

    public static void SaveCustomBackgroundPath(string? path)
    {
        try
        {
            var normalized = string.IsNullOrWhiteSpace(path) ? null : path;
            if (AppEnvironment.IsPackaged)
            {
                ApplicationData.Current.LocalSettings.Values[PackagedCustomBackgroundKey] = normalized ?? string.Empty;
                return;
            }

            var settingFile = Path.Combine(AppEnvironment.GetAppDataDirectory(), FileName);
            var store = LoadStore(settingFile);
            store.CustomBackgroundPath = normalized;
            var json = JsonSerializer.Serialize(store);
            File.WriteAllText(settingFile, json);
        }
        catch
        {
            // ignore
        }
    }

    public static bool LoadKeepVisualConsistencyOnDeactivate()
    {
        try
        {
            if (AppEnvironment.IsPackaged)
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (settings.Values.TryGetValue(PackagedKeepVisualConsistencyKey, out var value) && value is bool flag)
                {
                    return flag;
                }

                return true;
            }

            var settingFile = Path.Combine(AppEnvironment.GetAppDataDirectory(), FileName);
            var store = LoadStore(settingFile);
            return store.KeepVisualConsistencyOnDeactivate;
        }
        catch
        {
            return true;
        }
    }

    public static void SaveKeepVisualConsistencyOnDeactivate(bool enabled)
    {
        try
        {
            if (AppEnvironment.IsPackaged)
            {
                ApplicationData.Current.LocalSettings.Values[PackagedKeepVisualConsistencyKey] = enabled;
                return;
            }

            var settingFile = Path.Combine(AppEnvironment.GetAppDataDirectory(), FileName);
            var store = LoadStore(settingFile);
            store.KeepVisualConsistencyOnDeactivate = enabled;
            var json = JsonSerializer.Serialize(store);
            File.WriteAllText(settingFile, json);
        }
        catch
        {
            // ignore
        }
    }

    public static WidgetBounds? LoadBounds()
    {
        try
        {
            if (AppEnvironment.IsPackaged)
            {
                var values = ApplicationData.Current.LocalSettings.Values;
                if (!values.TryGetValue(PackagedXKey, out var xObj) ||
                    !values.TryGetValue(PackagedYKey, out var yObj) ||
                    !values.TryGetValue(PackagedWidthKey, out var wObj) ||
                    !values.TryGetValue(PackagedHeightKey, out var hObj))
                {
                    return null;
                }

                if (xObj is int x && yObj is int y && wObj is int w && hObj is int h && w > 0 && h > 0)
                {
                    return new WidgetBounds(x, y, w, h);
                }

                return null;
            }

            var path = Path.Combine(AppEnvironment.GetAppDataDirectory(), FileName);
            var store = LoadStore(path);
            if (store.X.HasValue && store.Y.HasValue && store.Width.HasValue && store.Height.HasValue
                && store.Width.Value > 0 && store.Height.Value > 0)
            {
                return new WidgetBounds(store.X.Value, store.Y.Value, store.Width.Value, store.Height.Value);
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    public static void SaveBounds(int x, int y, int width, int height)
    {
        try
        {
            if (width <= 0 || height <= 0)
            {
                return;
            }

            if (AppEnvironment.IsPackaged)
            {
                var values = ApplicationData.Current.LocalSettings.Values;
                values[PackagedXKey] = x;
                values[PackagedYKey] = y;
                values[PackagedWidthKey] = width;
                values[PackagedHeightKey] = height;
                return;
            }

            var path = Path.Combine(AppEnvironment.GetAppDataDirectory(), FileName);
            var store = LoadStore(path);
            store.X = x;
            store.Y = y;
            store.Width = width;
            store.Height = height;
            var json = JsonSerializer.Serialize(store);
            File.WriteAllText(path, json);
        }
        catch
        {
            // ignore
        }
    }

    private static Store LoadStore(string path)
    {
        if (!File.Exists(path))
        {
            return new Store();
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<Store>(json) ?? new Store();
    }

    private static string NormalizeMode(string? mode)
    {
        if (string.Equals(mode, "Acrylic", StringComparison.OrdinalIgnoreCase))
        {
            return "Acrylic";
        }

        if (string.Equals(mode, "Custom", StringComparison.OrdinalIgnoreCase))
        {
            return "Custom";
        }

        if (string.Equals(mode, "TextOnly", StringComparison.OrdinalIgnoreCase))
        {
            return "TextOnly";
        }

        return "Mica";
    }
}
