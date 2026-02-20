using System.Collections.Concurrent;
using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using NeZha_Desktop.Contracts;
using NeZha_Desktop.Models;

namespace NeZha_Desktop.Infrastructure.Api;

public sealed class ServerApiClient : IServerApiClient
{
    private static readonly string[] MonitorSeriesPalette =
    [
        "#4C8DFF",
        "#E2509B",
        "#F59E0B",
        "#A855F7",
        "#22C55E",
        "#06B6D4",
        "#6366F1",
        "#EC4899",
        "#14B8A6",
        "#8B5CF6",
    ];

    private static readonly string[] ServerListPaths = ["/api/v1/server/list", "/api/v1/server"];
    private const string ServerGroupPath = "/api/v1/server-group";
    private static readonly string[] ServerDetailPathTemplates = ["/api/v1/server/details?id={0}", "/api/v1/server/{0}"];
    private static readonly string[] ServerServicePathTemplates = ["/api/v1/server/{0}/service?period={1}", "/api/v1/server/{0}/service"];
    private const string ServerListWebSocketPath = "/api/v1/ws/server";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IAuthSessionService _authSessionService;
    private readonly ConcurrentDictionary<ulong, string> _serverSnapshotCache = new();

    public ServerApiClient(IHttpClientFactory httpClientFactory, IAuthSessionService authSessionService)
    {
        _httpClientFactory = httpClientFactory;
        _authSessionService = authSessionService;
    }

    public async Task<IReadOnlyList<ServerSummary>> GetServersAsync(CancellationToken cancellationToken)
    {
        var session = _authSessionService.CurrentSession ?? throw new InvalidOperationException("未登录");
        var client = BuildClient(session.DashboardUrl);

        Exception? lastError = null;
        foreach (var path in ServerListPaths)
        {
            try
            {
                using var response = await client.GetAsync(path, cancellationToken);
                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var apiError = TryReadApiError(document.RootElement);
                    throw new InvalidOperationException(apiError ?? $"获取服务器列表失败，HTTP {(int)response.StatusCode}");
                }

                UpdateServerSnapshotCache(document.RootElement);
                var list = ParseServerList(document.RootElement);
                if (list.Count > 0)
                {
                    return list;
                }

                lastError = new InvalidOperationException($"接口 {path} 返回空服务器列表");
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }

        try
        {
            return await GetServersViaWebSocketAsync(session, cancellationToken);
        }
        catch (Exception ex)
        {
            lastError = ex;
        }

        throw lastError ?? new InvalidOperationException("获取服务器列表失败");
    }

    public async Task<IReadOnlyList<ServerGroupSummary>> GetServerGroupsAsync(CancellationToken cancellationToken)
    {
        var session = _authSessionService.CurrentSession ?? throw new InvalidOperationException("未登录");
        var client = BuildClient(session.DashboardUrl);

        using var response = await client.GetAsync(ServerGroupPath, cancellationToken);
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var apiError = TryReadApiError(document.RootElement);
            throw new InvalidOperationException(apiError ?? $"获取服务器分组失败，HTTP {(int)response.StatusCode}");
        }

        return ParseServerGroups(document.RootElement);
    }

    public async Task<ServerDetail> GetServerDetailAsync(ulong serverId, CancellationToken cancellationToken)
    {
        var session = _authSessionService.CurrentSession ?? throw new InvalidOperationException("未登录");
        var client = BuildClient(session.DashboardUrl);

        Exception? lastError = null;
        foreach (var template in ServerDetailPathTemplates)
        {
            var path = string.Format(CultureInfo.InvariantCulture, template, serverId);
            try
            {
                using var response = await client.GetAsync(path, cancellationToken);
                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var apiError = TryReadApiError(document.RootElement);
                    throw new InvalidOperationException(apiError ?? $"获取服务器详情失败，HTTP {(int)response.StatusCode}");
                }

                return ParseServerDetail(document.RootElement, serverId);
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }

        try
        {
            await GetServersViaWebSocketAsync(session, cancellationToken);
            if (TryBuildDetailFromCache(serverId, out var wsCachedDetail))
            {
                return wsCachedDetail;
            }
        }
        catch (Exception ex)
        {
            lastError = ex;
        }

        if (TryBuildDetailFromCache(serverId, out var fallbackCachedDetail))
        {
            return fallbackCachedDetail;
        }

        throw lastError ?? new InvalidOperationException("获取服务器详情失败");
    }

    public async Task<IReadOnlyList<NetworkMonitorSummary>> GetServerNetworkMonitorsAsync(ulong serverId, string period, CancellationToken cancellationToken)
    {
        var session = _authSessionService.CurrentSession ?? throw new InvalidOperationException("未登录");
        var client = BuildClient(session.DashboardUrl);

        var normalizedPeriod = string.IsNullOrWhiteSpace(period) ? "1d" : period.Trim().ToLowerInvariant();

        Exception? lastError = null;
        foreach (var template in ServerServicePathTemplates)
        {
            var path = template.Contains("{1}", StringComparison.Ordinal)
                ? string.Format(CultureInfo.InvariantCulture, template, serverId, normalizedPeriod)
                : string.Format(CultureInfo.InvariantCulture, template, serverId);

            try
            {
                using var response = await client.GetAsync(path, cancellationToken);
                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var apiError = TryReadApiError(document.RootElement);
                    throw new InvalidOperationException(apiError ?? $"获取网络监控失败，HTTP {(int)response.StatusCode}");
                }

                return ParseNetworkMonitors(document.RootElement);
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }

        throw lastError ?? new InvalidOperationException("获取网络监控失败");
    }

    private HttpClient BuildClient(string dashboardUrl)
    {
        var client = _httpClientFactory.CreateClient("NezhaApi");
        client.BaseAddress = new Uri(AuthApiClient.NormalizeDashboardUrl(dashboardUrl));
        return client;
    }

    private async Task<IReadOnlyList<ServerSummary>> GetServersViaWebSocketAsync(AuthSession session, CancellationToken cancellationToken)
    {
        using var ws = new ClientWebSocket();
        if (!string.IsNullOrWhiteSpace(session.Token))
        {
            ws.Options.SetRequestHeader("Authorization", $"{session.Scheme} {session.Token}");
        }

        var baseUri = new Uri(AuthApiClient.NormalizeDashboardUrl(session.DashboardUrl));
        var wsScheme = baseUri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? "wss" : "ws";
        var wsUri = new Uri($"{wsScheme}://{baseUri.Authority}{ServerListWebSocketPath}");

        await ws.ConnectAsync(wsUri, cancellationToken);

        var buffer = new byte[1024 * 64];
        using var messageBuffer = new MemoryStream();

        while (true)
        {
            var result = await ws.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                break;
            }

            messageBuffer.Write(buffer, 0, result.Count);
            if (!result.EndOfMessage)
            {
                continue;
            }

            var text = Encoding.UTF8.GetString(messageBuffer.ToArray());
            messageBuffer.SetLength(0);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            using var document = JsonDocument.Parse(text);
            UpdateServerSnapshotCache(document.RootElement);
            var list = ParseServerList(document.RootElement);
            if (list.Count > 0)
            {
                return list;
            }
        }

        throw new InvalidOperationException("公开面板 WebSocket 未返回服务器数据");
    }

    private void UpdateServerSnapshotCache(JsonElement root)
    {
        var payload = UnwrapData(root);
        JsonElement array;

        if (payload.ValueKind == JsonValueKind.Array)
        {
            array = payload;
        }
        else if (payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("value", out var valueArray) && valueArray.ValueKind == JsonValueKind.Array)
        {
            array = valueArray;
        }
        else if (payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("servers", out var serversArray) && serversArray.ValueKind == JsonValueKind.Array)
        {
            array = serversArray;
        }
        else
        {
            return;
        }

        foreach (var item in array.EnumerateArray())
        {
            var id = ReadUInt(item, "id");
            if (id == 0)
            {
                continue;
            }

            _serverSnapshotCache[id] = item.GetRawText();
        }
    }

    private bool TryBuildDetailFromCache(ulong serverId, out ServerDetail detail)
    {
        detail = new ServerDetail();

        if (!_serverSnapshotCache.TryGetValue(serverId, out var raw))
        {
            return false;
        }

        using var document = JsonDocument.Parse(raw);
        detail = ParseServerDetail(document.RootElement, serverId);
        return true;
    }

    private static IReadOnlyList<ServerSummary> ParseServerList(JsonElement root)
    {
        var payload = UnwrapData(root);
        JsonElement array;

        if (payload.ValueKind == JsonValueKind.Array)
        {
            array = payload;
        }
        else if (payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("value", out var valueArray) && valueArray.ValueKind == JsonValueKind.Array)
        {
            array = valueArray;
        }
        else if (payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("servers", out var serversArray) && serversArray.ValueKind == JsonValueKind.Array)
        {
            array = serversArray;
        }
        else
        {
            return Array.Empty<ServerSummary>();
        }

        var result = new List<ServerSummary>();
        foreach (var item in array.EnumerateArray())
        {
            var id = ReadUInt(item, "id");
            var name = ReadString(item, "name") ?? "-";
            var lastActive = ReadDateTimeOffset(item, "last_active");
            var ip = ReadNestedString(item, "host", "ip")
                ?? ReadNestedString(item, "host", "ipv4")
                ?? ReadString(item, "ip")
                ?? "-";
            var uptimeSeconds = ReadNestedDouble(item, "state", "uptime") ?? ReadDouble(item, "uptime");
            var resolvedStatus = ResolveServerStatus(item);
            if (string.Equals(resolvedStatus.Status, "Unknown", StringComparison.Ordinal))
            {
                var inferredOnline = InferOnlineFromLastActive(lastActive);
                if (inferredOnline.HasValue)
                {
                    resolvedStatus = (inferredOnline.Value ? "Online" : "Offline", inferredOnline.Value);
                }
                else
                {
                    resolvedStatus = ("Online", true);
                }
            }
            var status = resolvedStatus.Status;
            var cpu = ClampPercent(ReadNestedDouble(item, "state", "cpu") ?? ReadDouble(item, "cpu"));
            var memUsed = ReadNestedDouble(item, "state", "mem_used") ?? ReadDouble(item, "mem_used");
            var memTotal = ReadNestedDouble(item, "host", "mem_total") ?? ReadDouble(item, "mem_total");
            var diskUsed = ReadNestedDouble(item, "state", "disk_used") ?? ReadDouble(item, "disk_used");
            var diskTotal = ReadNestedDouble(item, "host", "disk_total") ?? ReadDouble(item, "disk_total");
            var memPercent = ClampPercent(CalculatePercent(memUsed, memTotal));
            var diskPercent = ClampPercent(CalculatePercent(diskUsed, diskTotal));
            var netOutSpeed = ReadNestedDouble(item, "state", "net_out_speed") ?? ReadDouble(item, "net_out_speed");
            var netInSpeed = ReadNestedDouble(item, "state", "net_in_speed") ?? ReadDouble(item, "net_in_speed");
            var netOutTransfer = ReadNestedDouble(item, "state", "net_out_transfer") ?? ReadDouble(item, "net_out_transfer");
            var netInTransfer = ReadNestedDouble(item, "state", "net_in_transfer") ?? ReadDouble(item, "net_in_transfer");
            var platform = ReadNestedString(item, "host", "platform");
            var platformVersion = ReadNestedString(item, "host", "platform_version");
            var arch = ReadNestedString(item, "host", "arch");
            var tags = ParsePublicNoteTags(ReadString(item, "public_note"));

            result.Add(new ServerSummary
            {
                Id = id,
                Name = name,
                Ip = ip,
                Status = status,
                Uptime = FormatUptime(uptimeSeconds),
                LastActiveUtc = lastActive,
                IsOnline = resolvedStatus.IsOnline,
                System = FormatSystem(platform, platformVersion, arch),
                CpuPercent = cpu,
                MemoryPercent = memPercent,
                DiskPercent = diskPercent,
                CpuText = $"{cpu:F2}%",
                MemoryText = $"{memPercent:F2}%",
                DiskText = $"{diskPercent:F2}%",
                UploadSpeed = FormatBytesPerSecond(netOutSpeed),
                DownloadSpeed = FormatBytesPerSecond(netInSpeed),
                UploadTotal = FormatBytes(netOutTransfer),
                DownloadTotal = FormatBytes(netInTransfer),
                Tags = tags,
            });
        }

        return result;
    }

    private static IReadOnlyList<NetworkMonitorSummary> ParseNetworkMonitors(JsonElement root)
    {
        var data = UnwrapData(root);
        if (data.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<NetworkMonitorSummary>();
        }

        var result = new List<NetworkMonitorSummary>();
        foreach (var item in data.EnumerateArray())
        {
            var monitorId = ReadUInt(item, "monitor_id");
            var monitorName = ReadString(item, "monitor_name") ?? "-";
            var displayIndex = ReadInt(item, "display_index");

            var createdAt = ReadLongArray(item, "created_at");
            var avgDelay = ReadDoubleArray(item, "avg_delay");
            var packetLoss = ReadDoubleArray(item, "packet_loss");

            var latest = avgDelay.Count > 0 ? avgDelay[^1] : (double?)null;
            var minDelay = avgDelay.Count > 0 ? avgDelay.Min() : (double?)null;
            var maxDelay = avgDelay.Count > 0 ? avgDelay.Max() : (double?)null;
            var avg = avgDelay.Count > 0 ? avgDelay.Average() : (double?)null;
            var avgLoss = packetLoss.Count > 0 ? packetLoss.Average() : (double?)null;

            var lastSample = createdAt.Count > 0 ? FormatUnixMilliseconds(createdAt[^1]) : "-";

            result.Add(new NetworkMonitorSummary
            {
                MonitorId = monitorId,
                DisplayIndex = displayIndex,
                MonitorName = monitorName,
                LatestDelay = FormatMilliseconds(latest),
                MinDelay = FormatMilliseconds(minDelay),
                MaxDelay = FormatMilliseconds(maxDelay),
                AverageDelay = FormatMilliseconds(avg),
                AverageLoss = avgLoss.HasValue ? $"{avgLoss.Value:F2}%" : "-",
                SampleCount = avgDelay.Count.ToString(CultureInfo.InvariantCulture),
                LastSampleTime = lastSample,
                SeriesColorHex = MonitorSeriesPalette[result.Count % MonitorSeriesPalette.Length],
                Timestamps = createdAt,
                DelaySeries = avgDelay,
            });
        }

        return result
            .OrderBy(x => x.DisplayIndex)
            .ThenBy(x => x.MonitorId)
            .ToArray();
    }

    private static IReadOnlyList<ServerGroupSummary> ParseServerGroups(JsonElement root)
    {
        var payload = UnwrapData(root);
        if (payload.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<ServerGroupSummary>();
        }

        var result = new List<ServerGroupSummary>();
        foreach (var item in payload.EnumerateArray())
        {
            if (!item.TryGetProperty("group", out var groupObj) || groupObj.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var name = ReadString(groupObj, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var serverIds = new List<ulong>();
            if (item.TryGetProperty("servers", out var serversArray) && serversArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var serverItem in serversArray.EnumerateArray())
                {
                    if (serverItem.ValueKind == JsonValueKind.Number && serverItem.TryGetUInt64(out var id))
                    {
                        serverIds.Add(id);
                    }
                    else if (serverItem.ValueKind == JsonValueKind.String && ulong.TryParse(serverItem.GetString(), out var sid))
                    {
                        serverIds.Add(sid);
                    }
                }
            }

            result.Add(new ServerGroupSummary
            {
                Name = name.Trim(),
                ServerIds = serverIds,
            });
        }

        return result;
    }

    private static ServerDetail ParseServerDetail(JsonElement root, ulong serverId)
    {
        var data = UnwrapData(root);
        JsonElement detail;
        if (data.ValueKind == JsonValueKind.Array)
        {
            detail = SelectServerFromArray(data, serverId);
        }
        else if (data.ValueKind == JsonValueKind.Object)
        {
            detail = data;
        }
        else
        {
            detail = root;
        }

        var name = ReadString(detail, "name") ?? "-";
        var ip = ReadNestedString(detail, "host", "ip")
            ?? ReadNestedString(detail, "host", "ipv4")
            ?? ReadNestedString(detail, "host", "IPv4")
            ?? ReadString(detail, "valid_ip")
            ?? ReadString(detail, "ipv4")
            ?? ReadString(detail, "ip")
            ?? "-";

        var lastActive = ReadDateTimeOffset(detail, "last_active");
        var resolvedStatus = ResolveServerStatus(detail);
        if (string.Equals(resolvedStatus.Status, "Unknown", StringComparison.Ordinal))
        {
            var inferredOnline = InferOnlineFromLastActive(lastActive);
            if (inferredOnline.HasValue)
            {
                resolvedStatus = (inferredOnline.Value ? "Online" : "Offline", inferredOnline.Value);
            }
            else
            {
                resolvedStatus = ("Online", true);
            }
        }
        var status = resolvedStatus.Status;

        var cpu = ReadNestedDouble(detail, "state", "cpu")
            ?? ReadNestedDouble(detail, "status", "CPU")
            ?? ReadDouble(detail, "cpu");
        var cpuInfo = ReadNestedFirstString(detail, "host", "cpu");
        if (string.IsNullOrWhiteSpace(cpuInfo))
        {
            cpuInfo = ReadNestedFirstString(detail, "host", "CPU");
        }

        var platform = ReadNestedString(detail, "host", "platform") ?? ReadNestedString(detail, "host", "Platform");
        var platformVersion = ReadNestedString(detail, "host", "platform_version") ?? ReadNestedString(detail, "host", "PlatformVersion");
        var arch = ReadNestedString(detail, "host", "arch") ?? ReadNestedString(detail, "host", "Arch");
        var memUsed = ReadNestedDouble(detail, "state", "mem_used")
            ?? ReadNestedDouble(detail, "status", "MemUsed")
            ?? ReadDouble(detail, "mem_used");
        var memTotal = ReadNestedDouble(detail, "host", "mem_total")
            ?? ReadNestedDouble(detail, "host", "MemTotal")
            ?? ReadDouble(detail, "mem_total");
        var diskUsed = ReadNestedDouble(detail, "state", "disk_used")
            ?? ReadNestedDouble(detail, "status", "DiskUsed")
            ?? ReadDouble(detail, "disk_used");
        var diskTotal = ReadNestedDouble(detail, "host", "disk_total")
            ?? ReadNestedDouble(detail, "host", "DiskTotal")
            ?? ReadDouble(detail, "disk_total");
        var netIn = ReadNestedDouble(detail, "state", "net_in_transfer")
            ?? ReadNestedDouble(detail, "status", "NetInTransfer")
            ?? ReadDouble(detail, "net_in_transfer");
        var netOut = ReadNestedDouble(detail, "state", "net_out_transfer")
            ?? ReadNestedDouble(detail, "status", "NetOutTransfer")
            ?? ReadDouble(detail, "net_out_transfer");
        var netInSpeed = ReadNestedDouble(detail, "state", "net_in_speed")
            ?? ReadNestedDouble(detail, "status", "NetInSpeed")
            ?? ReadDouble(detail, "net_in_speed");
        var netOutSpeed = ReadNestedDouble(detail, "state", "net_out_speed")
            ?? ReadNestedDouble(detail, "status", "NetOutSpeed")
            ?? ReadDouble(detail, "net_out_speed");
        var tcpConn = ReadNestedDouble(detail, "state", "tcp_conn_count")
            ?? ReadNestedDouble(detail, "state", "tcpConnCount")
            ?? ReadNestedDouble(detail, "status", "TcpConnCount")
            ?? ReadDouble(detail, "tcp_conn_count");
        var udpConn = ReadNestedDouble(detail, "state", "udp_conn_count")
            ?? ReadNestedDouble(detail, "state", "udpConnCount")
            ?? ReadNestedDouble(detail, "status", "UdpConnCount")
            ?? ReadDouble(detail, "udp_conn_count");
        var processCount = ReadNestedDouble(detail, "state", "process_count")
            ?? ReadNestedDouble(detail, "state", "processCount")
            ?? ReadNestedDouble(detail, "status", "ProcessCount")
            ?? ReadDouble(detail, "process_count");
        var load1 = ReadNestedDouble(detail, "state", "load1")
            ?? ReadNestedDouble(detail, "state", "load_1")
            ?? ReadNestedDouble(detail, "status", "Load1")
            ?? ReadDouble(detail, "load1");
        var load5 = ReadNestedDouble(detail, "state", "load5")
            ?? ReadNestedDouble(detail, "state", "load_5")
            ?? ReadNestedDouble(detail, "status", "Load5")
            ?? ReadDouble(detail, "load5");
        var load15 = ReadNestedDouble(detail, "state", "load15")
            ?? ReadNestedDouble(detail, "state", "load_15")
            ?? ReadNestedDouble(detail, "status", "Load15")
            ?? ReadDouble(detail, "load15");
        var uptimeSeconds = ReadNestedDouble(detail, "state", "uptime")
            ?? ReadNestedDouble(detail, "status", "Uptime")
            ?? ReadDouble(detail, "uptime");
        var bootTime = ReadNestedDouble(detail, "host", "boot_time")
            ?? ReadNestedDouble(detail, "host", "BootTime")
            ?? ReadDouble(detail, "boot_time");
        return new ServerDetail
        {
            Id = ReadUInt(detail, "id") == 0 ? serverId : ReadUInt(detail, "id"),
            Name = name,
            Status = status,
            Ip = ip,
            Uptime = FormatUptime(uptimeSeconds),
            Cpu = cpu.HasValue ? $"{cpu.Value:F1}%" : "-",
            CpuPercent = ClampPercent(cpu),
            System = FormatSystem(platform, platformVersion, arch),
            CpuInfo = string.IsNullOrWhiteSpace(cpuInfo) ? "-" : cpuInfo,
            Memory = FormatUsage(memUsed, memTotal),
            MemoryPercent = ClampPercent(CalculatePercent(memUsed, memTotal)),
            Disk = FormatUsage(diskUsed, diskTotal),
            DiskPercent = ClampPercent(CalculatePercent(diskUsed, diskTotal)),
            NetworkIn = FormatBytes(netIn),
            NetworkOut = FormatBytes(netOut),
            Upload = FormatBytes(netOut),
            Download = FormatBytes(netIn),
            UploadSpeed = FormatBytesPerSecond(netOutSpeed),
            UploadSpeedBytes = netOutSpeed ?? 0,
            DownloadSpeed = FormatBytesPerSecond(netInSpeed),
            DownloadSpeedBytes = netInSpeed ?? 0,
            UploadTotal = FormatBytes(netOut),
            DownloadTotal = FormatBytes(netIn),
            TcpConnections = FormatCount(tcpConn),
            TcpConnectionsValue = tcpConn ?? 0,
            UdpConnections = FormatCount(udpConn),
            UdpConnectionsValue = udpConn ?? 0,
            ProcessCount = FormatCount(processCount),
            ProcessCountValue = processCount ?? 0,
            LoadAverage = FormatLoad(load1, load5, load15),
            Load1Value = load1 ?? 0,
            BootTime = FormatUnixSeconds(bootTime),
            LastReportTime = lastActive.HasValue ? lastActive.Value.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss") : "-",
        };
    }

    private static JsonElement UnwrapData(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var data))
        {
            return data;
        }

        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("result", out var result))
        {
            return result;
        }

        return root;
    }

    private static JsonElement SelectServerFromArray(JsonElement array, ulong serverId)
    {
        foreach (var item in array.EnumerateArray())
        {
            if (ReadUInt(item, "id") == serverId)
            {
                return item;
            }
        }

        foreach (var item in array.EnumerateArray())
        {
            return item;
        }

        return array;
    }

    private static string? TryReadApiError(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
        {
            return error.GetString();
        }

        return null;
    }

    private static ulong ReadUInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return 0;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetUInt64(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String && ulong.TryParse(value.GetString(), out var textNumber))
        {
            return textNumber;
        }

        return 0;
    }

    private static string? ReadString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return value.GetString();
    }

    private static int ReadInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return int.MaxValue;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var textNumber))
        {
            return textNumber;
        }

        return int.MaxValue;
    }

    private static string? ReadNestedString(JsonElement element, string parent, string child)
    {
        if (!element.TryGetProperty(parent, out var parentValue) || parentValue.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return ReadString(parentValue, child);
    }

    private static string? ReadNestedFirstString(JsonElement element, string parent, string child)
    {
        if (!element.TryGetProperty(parent, out var parentValue) || parentValue.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!parentValue.TryGetProperty(child, out var childValue))
        {
            return null;
        }

        if (childValue.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in childValue.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                {
                    return item.GetString();
                }
            }
        }

        if (childValue.ValueKind == JsonValueKind.String)
        {
            return childValue.GetString();
        }

        return null;
    }

    private static bool? ReadBool(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when value.TryGetInt64(out var number) => number != 0,
            JsonValueKind.Number when value.TryGetDouble(out var decimalNumber) => Math.Abs(decimalNumber) > double.Epsilon,
            JsonValueKind.String => ParseBoolString(value.GetString()),
            _ => null,
        };
    }

    private static bool? ReadNestedBool(JsonElement element, string parent, string child)
    {
        if (!element.TryGetProperty(parent, out var parentValue) || parentValue.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return ReadBool(parentValue, child);
    }

    private static bool? ResolveOnlineState(JsonElement element)
    {
        return ReadNestedBool(element, "state", "online")
            ?? ReadNestedBool(element, "state", "is_online")
            ?? ReadNestedBool(element, "status", "online")
            ?? ReadNestedBool(element, "status", "is_online")
            ?? ReadBool(element, "online")
            ?? ReadBool(element, "is_online");
    }

    private static (string Status, bool IsOnline) ResolveServerStatus(JsonElement element)
    {
        var online = ResolveOnlineState(element);
        if (online.HasValue)
        {
            return (online.Value ? "Online" : "Offline", online.Value);
        }

        var statusText = ReadNestedString(element, "state", "status")
            ?? ReadNestedString(element, "status", "status")
            ?? ReadString(element, "status");

        var mapped = ParseOnlineFromStatusText(statusText);
        if (mapped.HasValue)
        {
            return (mapped.Value ? "Online" : "Offline", mapped.Value);
        }

        return ("Unknown", false);
    }

    private static bool? ParseBoolString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var raw = value.Trim();
        if (bool.TryParse(raw, out var boolValue))
        {
            return boolValue;
        }

        if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
        {
            return number != 0;
        }

        if (raw.Equals("online", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("up", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("alive", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (raw.Equals("offline", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("down", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("dead", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return null;
    }

    private static bool? ParseOnlineFromStatusText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var raw = value.Trim();

        if (raw.Equals("在线", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("up", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("running", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("active", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("ok", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (raw.Equals("离线", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("down", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("stopped", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("inactive", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return ParseBoolString(raw);
    }

    private static bool? InferOnlineFromLastActive(DateTimeOffset? lastActiveUtc)
    {
        if (!lastActiveUtc.HasValue)
        {
            return null;
        }

        var age = DateTimeOffset.UtcNow - lastActiveUtc.Value.ToUniversalTime();
        if (age.TotalSeconds < 0)
        {
            return null;
        }

        return age.TotalSeconds <= 600;
    }

    private static double? ReadDouble(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var textNumber))
        {
            return textNumber;
        }

        return null;
    }

    private static double? ReadNestedDouble(JsonElement element, string parent, string child)
    {
        if (!element.TryGetProperty(parent, out var parentValue) || parentValue.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return ReadDouble(parentValue, child);
    }

    private static DateTimeOffset? ReadDateTimeOffset(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var raw))
        {
            return null;
        }

        if (raw.ValueKind == JsonValueKind.Number && raw.TryGetInt64(out var unix))
        {
            try
            {
                return DateTimeOffset.FromUnixTimeSeconds(unix);
            }
            catch
            {
                return null;
            }
        }

        if (raw.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var value = raw.GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var result))
        {
            return result;
        }

        return null;
    }

    private static List<long> ReadLongArray(JsonElement element, string name)
    {
        var result = new List<long>();
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Number && item.TryGetInt64(out var number))
            {
                result.Add(number);
                continue;
            }

            if (item.ValueKind == JsonValueKind.String && long.TryParse(item.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var textNumber))
            {
                result.Add(textNumber);
            }
        }

        return result;
    }

    private static List<double> ReadDoubleArray(JsonElement element, string name)
    {
        var result = new List<double>();
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Number && item.TryGetDouble(out var number))
            {
                result.Add(number);
                continue;
            }

            if (item.ValueKind == JsonValueKind.String && double.TryParse(item.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var textNumber))
            {
                result.Add(textNumber);
            }
        }

        return result;
    }

    private static string FormatUptime(double? seconds)
    {
        if (!seconds.HasValue || seconds <= 0)
        {
            return "-";
        }

        var ts = TimeSpan.FromSeconds(seconds.Value);
        if (ts.TotalDays >= 1)
        {
            return $"{(int)ts.TotalDays}d {ts.Hours}h";
        }

        if (ts.TotalHours >= 1)
        {
            return $"{(int)ts.TotalHours}h {ts.Minutes}m";
        }

        return $"{ts.Minutes}m {ts.Seconds}s";
    }

    private static string FormatMilliseconds(double? value)
    {
        if (!value.HasValue || value.Value < 0)
        {
            return "-";
        }

        return $"{value.Value:F2} ms";
    }

    private static string FormatUsage(double? used, double? total)
    {
        if (!used.HasValue || !total.HasValue || total <= 0)
        {
            return "-";
        }

        var percent = used.Value / total.Value * 100;
        return $"{FormatBytes(used)} / {FormatBytes(total)} ({percent:F1}%)";
    }

    private static string FormatBytes(double? value)
    {
        if (!value.HasValue || value < 0)
        {
            return "-";
        }

        var size = value.Value;
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return $"{size:F1} {units[unit]}";
    }

    private static string FormatSystem(string? platform, string? platformVersion, string? arch)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(platform))
        {
            parts.Add(platform);
        }

        if (!string.IsNullOrWhiteSpace(platformVersion))
        {
            parts.Add(platformVersion);
        }

        if (!string.IsNullOrWhiteSpace(arch))
        {
            parts.Add(arch);
        }

        return parts.Count == 0 ? "-" : string.Join(" ", parts);
    }

    private static string FormatUnixSeconds(double? unixSeconds)
    {
        if (!unixSeconds.HasValue || unixSeconds.Value <= 0)
        {
            return "-";
        }

        try
        {
            var dt = DateTimeOffset.FromUnixTimeSeconds((long)unixSeconds.Value).LocalDateTime;
            return dt.ToString("yyyy-MM-dd HH:mm:ss");
        }
        catch
        {
            return "-";
        }
    }

    private static string FormatUnixMilliseconds(long unixMilliseconds)
    {
        if (unixMilliseconds <= 0)
        {
            return "-";
        }

        try
        {
            var dt = DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds).LocalDateTime;
            return dt.ToString("yyyy-MM-dd HH:mm:ss");
        }
        catch
        {
            return "-";
        }
    }

    private static double CalculatePercent(double? used, double? total)
    {
        if (!used.HasValue || !total.HasValue || total <= 0)
        {
            return 0;
        }

        return used.Value / total.Value * 100;
    }

    private static double ClampPercent(double? value)
    {
        if (!value.HasValue || double.IsNaN(value.Value) || double.IsInfinity(value.Value))
        {
            return 0;
        }

        return Math.Clamp(value.Value, 0, 100);
    }

    private static string FormatBytesPerSecond(double? value)
    {
        var text = FormatBytes(value);
        return text == "-" ? "-" : $"{text}/s";
    }

    private static string FormatCount(double? value)
    {
        if (!value.HasValue || value.Value < 0)
        {
            return "-";
        }

        return Math.Round(value.Value).ToString(CultureInfo.InvariantCulture);
    }

    private static string FormatLoad(double? load1, double? load5, double? load15)
    {
        if (!load1.HasValue && !load5.HasValue && !load15.HasValue)
        {
            return "-";
        }

        return $"{(load1 ?? 0):F2} / {(load5 ?? 0):F2} / {(load15 ?? 0):F2}";
    }

    private static IReadOnlyList<string> ParsePublicNoteTags(string? publicNote)
    {
        if (string.IsNullOrWhiteSpace(publicNote))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var doc = JsonDocument.Parse(publicNote);
            if (!doc.RootElement.TryGetProperty("planDataMod", out var plan) || plan.ValueKind != JsonValueKind.Object)
            {
                return Array.Empty<string>();
            }

            var tags = new List<string>();
            AddTag(plan, "bandwidth", tags);
            AddTag(plan, "trafficVol", tags);
            AddTag(plan, "IPv4", tags);
            AddTag(plan, "IPv6", tags);
            AddTag(plan, "networkRoute", tags);
            AddTag(plan, "extra", tags);
            return tags;
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static void AddTag(JsonElement obj, string key, List<string> tags)
    {
        if (!obj.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return;
        }

        var text = value.GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        foreach (var part in text.Split([',', '，'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!string.IsNullOrWhiteSpace(part))
            {
                tags.Add(part);
            }
        }
    }
}
