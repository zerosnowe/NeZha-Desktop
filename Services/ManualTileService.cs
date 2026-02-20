using NeZha_Desktop.Contracts;
using NeZha_Desktop.Infrastructure.Runtime;
using NeZha_Desktop.Infrastructure.Settings;
using Serilog;
using Windows.Foundation.Metadata;
using Windows.UI.StartScreen;
using WinRT.Interop;

namespace NeZha_Desktop.Services;

public sealed class ManualTileService : IManualTileService
{
    public const string UnpackagedTileId = "NeZhaDesktop.ManualPinnedTile";

    public bool IsAvailable => !AppEnvironment.IsPackaged && IsApiAvailable();

    public async Task<ManualTileResult> AddManualTileAsync()
    {
        if (AppEnvironment.IsPackaged)
        {
            return new ManualTileResult(false, "Disabled in packaged mode.");
        }

        if (!IsApiAvailable())
        {
            ManualTileStateStore.SetPinned(false);
            return new ManualTileResult(false, "Current unpackaged runtime does not support tile pin API.");
        }

        try
        {
            if (SecondaryTile.Exists(UnpackagedTileId))
            {
                ManualTileStateStore.SetPinned(true);
                return new ManualTileResult(true, "Tile already exists.");
            }

            var tile = new SecondaryTile(
                UnpackagedTileId,
                "NeZha Servers",
                "NeZha Servers",
                "open/servers",
                TileOptions.ShowNameOnLogo | TileOptions.ShowNameOnWideLogo,
                new Uri("ms-appx:///Assets/Square150x150Logo.png"))
            {
                VisualElements =
                {
                    Square71x71Logo = new Uri("ms-appx:///Assets/SmallTile.png"),
                    Wide310x150Logo = new Uri("ms-appx:///Assets/Wide310x150Logo.png"),
                    Square310x310Logo = new Uri("ms-appx:///Assets/LargeTile.png"),
                    ShowNameOnSquare150x150Logo = true,
                    ShowNameOnWide310x150Logo = true,
                    ShowNameOnSquare310x310Logo = true
                }
            };

            if (App.MainAppWindow is null)
            {
                return new ManualTileResult(false, "Main window is not ready.");
            }

            var hwnd = WindowNative.GetWindowHandle(App.MainAppWindow);
            InitializeWithWindow.Initialize(tile, hwnd);

            var pinned = await tile.RequestCreateAsync();
            if (!pinned)
            {
                return new ManualTileResult(false, "Pin operation cancelled.");
            }

            ManualTileStateStore.SetPinned(true);
            return new ManualTileResult(true, "Tile added successfully.");
        }
        catch (Exception ex)
        {
            ManualTileStateStore.SetPinned(false);
            Log.Warning(ex, "Manual tile pin failed.");
            return new ManualTileResult(false, "Pinning failed. This mode or OS might not support it.");
        }
    }

    private static bool IsApiAvailable()
    {
        try
        {
            return ApiInformation.IsTypePresent("Windows.UI.StartScreen.SecondaryTile")
                   && ApiInformation.IsMethodPresent("Windows.UI.StartScreen.SecondaryTile", "RequestCreateAsync")
                   && ApiInformation.IsTypePresent("Windows.UI.Notifications.TileUpdateManager");
        }
        catch
        {
            return false;
        }
    }
}
