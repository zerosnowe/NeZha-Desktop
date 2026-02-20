namespace NeZha_Desktop.Contracts;

public interface IDesktopWidgetService
{
    bool IsEnabled { get; }
    string BackdropMode { get; }
    string? CustomBackgroundPath { get; }
    bool KeepVisualConsistencyOnDeactivate { get; }

    Task SetEnabledAsync(bool enabled);
    Task SetBackdropModeAsync(string mode);
    Task SetCustomBackgroundPathAsync(string? path);
    Task SetKeepVisualConsistencyOnDeactivateAsync(bool enabled);

    Task RestoreAsync();
}
