using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NeZha_Desktop.Contracts;
using NeZha_Desktop.Infrastructure.Runtime;
using NeZha_Desktop.Models;
using Windows.Security.Credentials;

namespace NeZha_Desktop.Infrastructure.Security;

public sealed class PasswordVaultTokenStore : ITokenStore
{
    private const string ResourcePrefix = "NeZhaDesktop.Auth";
    private const string FallbackFileName = "auth-secrets.dat";

    private sealed class FallbackStore
    {
        public Dictionary<string, string> EncryptedSecrets { get; set; } = new(StringComparer.Ordinal);
    }

    public async Task SaveSecretAsync(StoredAuthSecret secret)
    {
        if (AppEnvironment.IsPackaged)
        {
            var vault = new PasswordVault();
            var resource = BuildResource(secret.DashboardUrl, secret.Username);

            TryRemove(vault, resource, secret.Username);

            var serialized = JsonSerializer.Serialize(secret);
            vault.Add(new PasswordCredential(resource, secret.Username, serialized));
            return;
        }

        var key = BuildFallbackKey(secret.DashboardUrl, secret.Username);
        var encrypted = Encrypt(JsonSerializer.Serialize(secret));
        var model = await LoadFallbackAsync();
        model.EncryptedSecrets[key] = encrypted;
        await SaveFallbackAsync(model);
    }

    public async Task<StoredAuthSecret?> GetSecretAsync(string dashboardUrl, string username)
    {
        if (AppEnvironment.IsPackaged)
        {
            var vault = new PasswordVault();
            var resource = BuildResource(dashboardUrl, username);

            try
            {
                var credential = vault.Retrieve(resource, username);
                credential.RetrievePassword();
                return JsonSerializer.Deserialize<StoredAuthSecret>(credential.Password);
            }
            catch
            {
                return null;
            }
        }

        try
        {
            var key = BuildFallbackKey(dashboardUrl, username);
            var model = await LoadFallbackAsync();
            if (!model.EncryptedSecrets.TryGetValue(key, out var payload))
            {
                return null;
            }

            var json = Decrypt(payload);
            return JsonSerializer.Deserialize<StoredAuthSecret>(json);
        }
        catch
        {
            return null;
        }
    }

    public async Task DeleteSecretAsync(string dashboardUrl, string username)
    {
        if (AppEnvironment.IsPackaged)
        {
            var vault = new PasswordVault();
            var resource = BuildResource(dashboardUrl, username);
            TryRemove(vault, resource, username);
            return;
        }

        var key = BuildFallbackKey(dashboardUrl, username);
        var model = await LoadFallbackAsync();
        if (model.EncryptedSecrets.Remove(key))
        {
            await SaveFallbackAsync(model);
        }
    }

    private static string BuildResource(string dashboardUrl, string username)
    {
        var uri = new Uri(NormalizeDashboardUrl(dashboardUrl));
        return $"{ResourcePrefix}:{uri.Scheme}://{uri.Host}:{uri.Port}:{username}";
    }

    private static string BuildFallbackKey(string dashboardUrl, string username)
    {
        return BuildResource(dashboardUrl, username).ToLowerInvariant();
    }

    private static string NormalizeDashboardUrl(string value)
    {
        var input = value.Trim();
        if (!input.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !input.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            input = $"https://{input}";
        }

        return input.TrimEnd('/');
    }

    private static void TryRemove(PasswordVault vault, string resource, string username)
    {
        try
        {
            var credential = vault.Retrieve(resource, username);
            vault.Remove(credential);
        }
        catch
        {
        }
    }

    private static string Encrypt(string plainText)
    {
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var protectedBytes = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedBytes);
    }

    private static string Decrypt(string cipherText)
    {
        var protectedBytes = Convert.FromBase64String(cipherText);
        var plainBytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(plainBytes);
    }

    private static async Task<FallbackStore> LoadFallbackAsync()
    {
        var path = Path.Combine(AppEnvironment.GetAppDataDirectory(), FallbackFileName);
        if (!File.Exists(path))
        {
            return new FallbackStore();
        }

        try
        {
            var json = await File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize<FallbackStore>(json) ?? new FallbackStore();
        }
        catch
        {
            return new FallbackStore();
        }
    }

    private static async Task SaveFallbackAsync(FallbackStore store)
    {
        var path = Path.Combine(AppEnvironment.GetAppDataDirectory(), FallbackFileName);
        var json = JsonSerializer.Serialize(store);
        await File.WriteAllTextAsync(path, json);
    }
}
