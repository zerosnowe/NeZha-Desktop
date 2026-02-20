using NeZha_Desktop.Contracts;
using NeZha_Desktop.Infrastructure.Settings;
using NeZha_Desktop.Views;

namespace NeZha_Desktop.Services;

public sealed class DesktopWidgetService : IDesktopWidgetService
{
    private readonly IServerListSnapshotService _snapshotService;
    private DesktopWidgetWindow? _widgetWindow;

    public DesktopWidgetService(IServerListSnapshotService snapshotService)
    {
        _snapshotService = snapshotService;
        IsEnabled = DesktopWidgetPreferenceStore.LoadEnabled();
        BackdropMode = DesktopWidgetPreferenceStore.LoadBackdropMode();
        CustomBackgroundPath = DesktopWidgetPreferenceStore.LoadCustomBackgroundPath();
        KeepVisualConsistencyOnDeactivate = DesktopWidgetPreferenceStore.LoadKeepVisualConsistencyOnDeactivate();
    }

    public bool IsEnabled { get; private set; }
    public string BackdropMode { get; private set; }
    public string? CustomBackgroundPath { get; private set; }
    public bool KeepVisualConsistencyOnDeactivate { get; private set; }

    public Task RestoreAsync()
    {
        if (IsEnabled)
        {
            if (!TryShowWidget())
            {
                IsEnabled = false;
                DesktopWidgetPreferenceStore.SaveEnabled(false);
            }
        }

        return Task.CompletedTask;
    }

    public Task SetEnabledAsync(bool enabled)
    {
        IsEnabled = enabled;
        DesktopWidgetPreferenceStore.SaveEnabled(enabled);

        if (enabled)
        {
            if (!TryShowWidget())
            {
                IsEnabled = false;
                DesktopWidgetPreferenceStore.SaveEnabled(false);
            }
        }
        else
        {
            HideWidget();
        }

        return Task.CompletedTask;
    }

    public Task SetBackdropModeAsync(string mode)
    {
        BackdropMode = NormalizeMode(mode);
        DesktopWidgetPreferenceStore.SaveBackdropMode(BackdropMode);

        try
        {
            _widgetWindow?.ApplyAppearance(BackdropMode, CustomBackgroundPath);
            _widgetWindow?.SetKeepVisualConsistencyOnDeactivate(KeepVisualConsistencyOnDeactivate);
        }
        catch
        {
            // ignore
        }

        return Task.CompletedTask;
    }

    public Task SetCustomBackgroundPathAsync(string? path)
    {
        CustomBackgroundPath = string.IsNullOrWhiteSpace(path) ? null : path;
        DesktopWidgetPreferenceStore.SaveCustomBackgroundPath(CustomBackgroundPath);

        try
        {
            _widgetWindow?.ApplyAppearance(BackdropMode, CustomBackgroundPath);
            _widgetWindow?.SetKeepVisualConsistencyOnDeactivate(KeepVisualConsistencyOnDeactivate);
        }
        catch
        {
            // ignore
        }

        return Task.CompletedTask;
    }

    public Task SetKeepVisualConsistencyOnDeactivateAsync(bool enabled)
    {
        KeepVisualConsistencyOnDeactivate = enabled;
        DesktopWidgetPreferenceStore.SaveKeepVisualConsistencyOnDeactivate(enabled);
        try
        {
            _widgetWindow?.SetKeepVisualConsistencyOnDeactivate(enabled);
        }
        catch
        {
            // ignore
        }

        return Task.CompletedTask;
    }

    private bool TryShowWidget()
    {
        if (_widgetWindow is not null)
        {
            try
            {
                _widgetWindow.Activate();
                return true;
            }
            catch
            {
                _widgetWindow = null;
            }
        }

        try
        {
            _widgetWindow = new DesktopWidgetWindow(_snapshotService, BackdropMode, CustomBackgroundPath, KeepVisualConsistencyOnDeactivate);
            _widgetWindow.ExitRequested += WidgetWindow_ExitRequested;
            _widgetWindow.Activate();
            return true;
        }
        catch
        {
            _widgetWindow = null;
            return false;
        }
    }

    private void HideWidget()
    {
        if (_widgetWindow is null)
        {
            return;
        }

        _widgetWindow.ExitRequested -= WidgetWindow_ExitRequested;
        _widgetWindow.Close();
        _widgetWindow = null;
    }

    private void WidgetWindow_ExitRequested(object? sender, EventArgs e)
    {
        _ = SetEnabledAsync(false);
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
