namespace NeZha_Desktop.Infrastructure.Runtime;

public static class AppEnvironment
{
    public static bool IsPackaged => true;

    public static string GetAppDataDirectory()
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appDir = Path.Combine(baseDir, "NeZha-Desktop");
        Directory.CreateDirectory(appDir);
        return appDir;
    }
}
