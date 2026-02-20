using System.Text.Json;
using NeZha_Desktop.Infrastructure.Runtime;
using Windows.Storage;

namespace NeZha_Desktop.Infrastructure.Settings;

public static class ManualTileStateStore
{
    private const string PackagedKey = "ManualTilePinned";
    private const string FileName = "manual-tile.json";

    private sealed class StateModel
    {
        public bool IsPinned { get; set; }
    }

    public static bool IsPinned()
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
            var state = JsonSerializer.Deserialize<StateModel>(json);
            return state?.IsPinned == true;
        }
        catch
        {
            return false;
        }
    }

    public static void SetPinned(bool pinned)
    {
        try
        {
            if (AppEnvironment.IsPackaged)
            {
                ApplicationData.Current.LocalSettings.Values[PackagedKey] = pinned;
                return;
            }

            var path = Path.Combine(AppEnvironment.GetAppDataDirectory(), FileName);
            var json = JsonSerializer.Serialize(new StateModel { IsPinned = pinned });
            File.WriteAllText(path, json);
        }
        catch
        {
            // ignore
        }
    }
}
