using System.Text.Json;
using NeZha_Desktop.Contracts;
using NeZha_Desktop.Infrastructure.Runtime;
using NeZha_Desktop.Models;
using Windows.Storage;

namespace NeZha_Desktop.Infrastructure.Settings;

public sealed class PanelProfileStore : IPanelProfileStore
{
    private const string FileName = "panel-profiles.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private sealed class StoreModel
    {
        public List<DashboardProfile> Profiles { get; set; } = new();

        public string? LastUsedProfileId { get; set; }
    }

    public async Task<IReadOnlyList<DashboardProfile>> GetProfilesAsync()
    {
        var model = await LoadAsync();
        return model.Profiles
            .OrderByDescending(p => p.LastUsedUtc)
            .ToList();
    }

    public async Task SaveOrUpdateAsync(DashboardProfile profile)
    {
        var model = await LoadAsync();
        var current = model.Profiles.FirstOrDefault(x => x.Id == profile.Id);
        if (current is null)
        {
            model.Profiles.Add(profile);
        }
        else
        {
            current.DashboardUrl = profile.DashboardUrl;
            current.Username = profile.Username;
            current.RememberLogin = profile.RememberLogin;
            current.LastUsedUtc = profile.LastUsedUtc;
            current.PublicMode = profile.PublicMode;
        }

        model.LastUsedProfileId = profile.Id;
        await SaveAsync(model);
    }

    public async Task<DashboardProfile?> GetLastUsedAsync()
    {
        var model = await LoadAsync();
        if (!string.IsNullOrWhiteSpace(model.LastUsedProfileId))
        {
            return model.Profiles.FirstOrDefault(p => p.Id == model.LastUsedProfileId);
        }

        return model.Profiles
            .OrderByDescending(p => p.LastUsedUtc)
            .FirstOrDefault();
    }

    public async Task SetLastUsedAsync(string profileId)
    {
        var model = await LoadAsync();
        model.LastUsedProfileId = profileId;
        await SaveAsync(model);
    }

    private static async Task<StoreModel> LoadAsync()
    {
        try
        {
            if (AppEnvironment.IsPackaged)
            {
                var file = await ApplicationData.Current.LocalFolder.TryGetItemAsync(FileName) as StorageFile;
                if (file is null)
                {
                    return new StoreModel();
                }

                var json = await FileIO.ReadTextAsync(file);
                return JsonSerializer.Deserialize<StoreModel>(json, JsonOptions) ?? new StoreModel();
            }

            var path = Path.Combine(AppEnvironment.GetAppDataDirectory(), FileName);
            if (!File.Exists(path))
            {
                return new StoreModel();
            }

            var jsonText = await File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize<StoreModel>(jsonText, JsonOptions) ?? new StoreModel();
        }
        catch
        {
            return new StoreModel();
        }
    }

    private static async Task SaveAsync(StoreModel model)
    {
        var json = JsonSerializer.Serialize(model, JsonOptions);

        if (AppEnvironment.IsPackaged)
        {
            var file = await ApplicationData.Current.LocalFolder.CreateFileAsync(
                FileName,
                CreationCollisionOption.ReplaceExisting);

            await FileIO.WriteTextAsync(file, json);
            return;
        }

        var path = Path.Combine(AppEnvironment.GetAppDataDirectory(), FileName);
        await File.WriteAllTextAsync(path, json);
    }
}
