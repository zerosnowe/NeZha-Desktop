namespace NeZha_Desktop.Models;

public sealed class ServerSummary
{
    public ulong Id { get; set; }

    public string Name { get; set; } = "-";

    public string Status { get; set; } = "Unknown";

    public string Ip { get; set; } = "-";

    public string Uptime { get; set; } = "-";

    public DateTimeOffset? LastActiveUtc { get; set; }

    public bool IsOnline { get; set; }

    public string System { get; set; } = "-";

    public double CpuPercent { get; set; }

    public double MemoryPercent { get; set; }

    public double DiskPercent { get; set; }

    public string CpuText { get; set; } = "-";

    public string MemoryText { get; set; } = "-";

    public string DiskText { get; set; } = "-";

    public string UploadSpeed { get; set; } = "-";

    public string DownloadSpeed { get; set; } = "-";

    public string UploadTotal { get; set; } = "-";

    public string DownloadTotal { get; set; } = "-";

    public IReadOnlyList<string> Tags { get; set; } = Array.Empty<string>();
}

public sealed class ServerDetail
{
    public ulong Id { get; set; }

    public string Name { get; set; } = "-";

    public string Status { get; set; } = "Unknown";

    public string Ip { get; set; } = "-";

    public string Uptime { get; set; } = "-";

    public string Cpu { get; set; } = "-";

    public string System { get; set; } = "-";

    public string CpuInfo { get; set; } = "-";

    public string Memory { get; set; } = "-";

    public string Disk { get; set; } = "-";

    public string NetworkIn { get; set; } = "-";

    public string NetworkOut { get; set; } = "-";

    public string Upload { get; set; } = "-";

    public string Download { get; set; } = "-";

    public string BootTime { get; set; } = "-";

    public string LastReportTime { get; set; } = "-";

    public string UploadSpeed { get; set; } = "-";

    public string DownloadSpeed { get; set; } = "-";

    public string UploadTotal { get; set; } = "-";

    public string DownloadTotal { get; set; } = "-";

    public string TcpConnections { get; set; } = "-";

    public string UdpConnections { get; set; } = "-";

    public string ProcessCount { get; set; } = "-";

    public string LoadAverage { get; set; } = "-";

    public double CpuPercent { get; set; }

    public double MemoryPercent { get; set; }

    public double DiskPercent { get; set; }

    public double UploadSpeedBytes { get; set; }

    public double DownloadSpeedBytes { get; set; }

    public double TcpConnectionsValue { get; set; }

    public double UdpConnectionsValue { get; set; }

    public double ProcessCountValue { get; set; }

    public double Load1Value { get; set; }
}

public sealed class NetworkMonitorSummary
{
    public ulong MonitorId { get; set; }

    public int DisplayIndex { get; set; }

    public string MonitorName { get; set; } = "-";

    public string LatestDelay { get; set; } = "-";

    public string MinDelay { get; set; } = "-";

    public string MaxDelay { get; set; } = "-";

    public string AverageDelay { get; set; } = "-";

    public string AverageLoss { get; set; } = "-";

    public string SampleCount { get; set; } = "0";

    public string LastSampleTime { get; set; } = "-";

    public string SeriesColorHex { get; set; } = "#4C8DFF";

    public IReadOnlyList<long> Timestamps { get; set; } = Array.Empty<long>();

    public IReadOnlyList<double> DelaySeries { get; set; } = Array.Empty<double>();
}

public sealed class ServerGroupSummary
{
    public string Name { get; set; } = "All";

    public IReadOnlyList<ulong> ServerIds { get; set; } = Array.Empty<ulong>();
}
