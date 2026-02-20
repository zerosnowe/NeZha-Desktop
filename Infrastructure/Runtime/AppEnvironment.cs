using Windows.ApplicationModel;

namespace NeZha_Desktop.Infrastructure.Runtime;

public static class AppEnvironment
{
    private static readonly Lazy<bool> Packaged = new(() =>
    {
        try
        {
            _ = Package.Current.Id.FullName;
            return true;
        }
        catch
        {
            return false;
        }
    });

    public static bool IsPackaged => Packaged.Value;

    public static string GetAppDataDirectory()
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appDir = Path.Combine(baseDir, "NeZha-Desktop");
        Directory.CreateDirectory(appDir);
        return appDir;
    }
}
