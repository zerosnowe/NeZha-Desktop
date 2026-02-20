namespace NeZha_Desktop.Contracts;

public interface IManualTileService
{
    bool IsAvailable { get; }

    Task<ManualTileResult> AddManualTileAsync();
}

public sealed record ManualTileResult(bool Success, string Message);
