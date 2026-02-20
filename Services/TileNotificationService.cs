using System.Security;
using NeZha_Desktop.Contracts;
using NeZha_Desktop.Infrastructure.Runtime;
using NeZha_Desktop.Models;
using Serilog;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace NeZha_Desktop.Services;

public sealed class TileNotificationService : ITileNotificationService
{
    private static readonly TimeSpan RotationInterval = TimeSpan.FromSeconds(3);
    private readonly object _syncRoot = new();
    private readonly List<ServerSummary> _serversSnapshot = [];
    private readonly Timer _rotationTimer;
    private int _nextIndex;

    public TileNotificationService()
    {
        _rotationTimer = new Timer(OnRotationTick, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public Task UpdateServerTilesAsync(IReadOnlyList<ServerSummary> servers)
    {
        if (!AppEnvironment.IsPackaged)
        {
            return Task.CompletedTask;
        }

        if (servers.Count == 0)
        {
            return ClearAsync();
        }

        try
        {
            lock (_syncRoot)
            {
                _serversSnapshot.Clear();
                _serversSnapshot.AddRange(servers.Take(50));
                _nextIndex = 0;
            }

            TileUpdateManager.CreateTileUpdaterForApplication().Clear();
            PublishCurrentBatch();
            _rotationTimer.Change(RotationInterval, RotationInterval);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Update server tiles failed.");
        }

        return Task.CompletedTask;
    }

    public Task ClearAsync()
    {
        if (!AppEnvironment.IsPackaged)
        {
            return Task.CompletedTask;
        }

        try
        {
            lock (_syncRoot)
            {
                _serversSnapshot.Clear();
                _nextIndex = 0;
            }

            _rotationTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            var updater = TileUpdateManager.CreateTileUpdaterForApplication();
            updater.Clear();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Clear tiles failed.");
        }

        return Task.CompletedTask;
    }

    private static string EscapeXml(string? value)
    {
        return SecurityElement.Escape(value ?? string.Empty) ?? string.Empty;
    }

    private void OnRotationTick(object? _)
    {
        try
        {
            PublishCurrentBatch();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Tile rotation tick failed.");
        }
    }

    private void PublishCurrentBatch()
    {
        ServerSummary? current;
        lock (_syncRoot)
        {
            if (_serversSnapshot.Count == 0)
            {
                return;
            }

            current = _serversSnapshot[_nextIndex];
            _nextIndex = (_nextIndex + 1) % _serversSnapshot.Count;
        }

        var updater = TileUpdateManager.CreateTileUpdaterForApplication();
        var title = EscapeXml(current.Name);
        var status = current.IsOnline ? "在线" : "离线";
        var line2 = EscapeXml($"{status} | {current.Ip}");
        var line3 = EscapeXml($"CPU {current.CpuText}  内存 {current.MemoryText}  磁盘 {current.DiskText}");

        var xml = $"""
<tile>
  <visual>
    <binding template='TileMedium' hint-textStacking='top'>
      <text hint-style='caption'>{title}</text>
      <text hint-style='captionsubtle'>{line2}</text>
      <text hint-style='captionsubtle'>{line3}</text>
    </binding>
    <binding template='TileWide' hint-textStacking='top'>
      <text hint-style='subtitle'>{title}</text>
      <text hint-style='body'>{line2}</text>
      <text hint-style='captionsubtle'>{line3}</text>
    </binding>
    <binding template='TileLarge' hint-textStacking='top'>
      <text hint-style='title'>{title}</text>
      <text hint-style='body'>{line2}</text>
      <text hint-style='captionsubtle'>{line3}</text>
      <text hint-style='captionsubtle'>上传 {EscapeXml(current.UploadSpeed)}  下载 {EscapeXml(current.DownloadSpeed)}</text>
    </binding>
  </visual>
</tile>
""";

        var doc = new XmlDocument();
        doc.LoadXml(xml);

        var notification = new TileNotification(doc)
        {
            Tag = "server-rotating",
            ExpirationTime = DateTimeOffset.Now.AddMinutes(30)
        };

        updater.Update(notification);
    }
}
