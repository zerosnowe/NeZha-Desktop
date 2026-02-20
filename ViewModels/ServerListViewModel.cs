using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NeZha_Desktop.Contracts;
using NeZha_Desktop.Models;
using Serilog;

namespace NeZha_Desktop.ViewModels
{
    public sealed class ServerListViewModel : ObservableObject
    {
        private const string AllCategoryName = "全部";

        private readonly IServerApiClient _serverApiClient;
        private readonly ITileNotificationService _tileNotificationService;
        private readonly IServerListSnapshotService _snapshotService;
        private readonly List<ServerSummary> _allServers = [];
        private readonly Dictionary<string, HashSet<ulong>> _groupServerMap = new(StringComparer.OrdinalIgnoreCase);

        private bool _isBusy;
        private string _errorMessage = string.Empty;
        private string _statusMessage = "准备就绪";
        private ServerSummary? _selectedServer;
        private string _selectedCategory = AllCategoryName;
        private string _searchKeyword = string.Empty;
        private bool _showOnline = true;
        private bool _showOffline = true;

        public ServerListViewModel(
            IServerApiClient serverApiClient,
            ITileNotificationService tileNotificationService,
            IServerListSnapshotService snapshotService)
        {
            _serverApiClient = serverApiClient;
            _tileNotificationService = tileNotificationService;
            _snapshotService = snapshotService;
            Servers = new ObservableCollection<ServerSummary>();
            Categories = new ObservableCollection<string> { AllCategoryName };
            RefreshCommand = new AsyncRelayCommand(() => RefreshAsync(false), () => !IsBusy);
            OpenDetailCommand = new RelayCommand(OpenDetail, () => SelectedServer != null);
        }

        public event EventHandler<ulong>? ServerSelected;

        public ObservableCollection<ServerSummary> Servers { get; }

        public ObservableCollection<string> Categories { get; }

        public IAsyncRelayCommand RefreshCommand { get; }

        public IRelayCommand OpenDetailCommand { get; }

        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    RefreshCommand.NotifyCanExecuteChanged();
                }
            }
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            set => SetProperty(ref _errorMessage, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public string SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                var normalized = string.IsNullOrWhiteSpace(value) ? AllCategoryName : value;
                if (SetProperty(ref _selectedCategory, normalized))
                {
                    ApplyFilter();
                }
            }
        }

        public ServerSummary? SelectedServer
        {
            get => _selectedServer;
            set
            {
                if (SetProperty(ref _selectedServer, value))
                {
                    OpenDetailCommand.NotifyCanExecuteChanged();
                }
            }
        }

        public string SearchKeyword
        {
            get => _searchKeyword;
            private set => SetProperty(ref _searchKeyword, value);
        }

        public bool ShowOnline
        {
            get => _showOnline;
            set
            {
                if (SetProperty(ref _showOnline, value))
                {
                    ApplyFilter();
                }
            }
        }

        public bool ShowOffline
        {
            get => _showOffline;
            set
            {
                if (SetProperty(ref _showOffline, value))
                {
                    ApplyFilter();
                }
            }
        }

        public async Task EnsureDataLoadedAsync()
        {
            if (Servers.Count > 0)
            {
                return;
            }

            await RefreshAsync(false);
        }

        public void SetSearchKeyword(string? keyword)
        {
            SearchKeyword = keyword?.Trim() ?? string.Empty;
            ApplyFilter();
        }

        public async Task RefreshAsync(bool isBackgroundRefresh = false)
        {
            if (IsBusy)
            {
                return;
            }

            try
            {
                IsBusy = true;
                if (!isBackgroundRefresh)
                {
                    ErrorMessage = string.Empty;
                    StatusMessage = "正在同步服务器状态...";
                }

                var list = await _serverApiClient.GetServersAsync(CancellationToken.None);
                _allServers.Clear();
                _allServers.AddRange(list.OrderByDescending(s => s.Status == "Online"));
                _snapshotService.UpdateSnapshot(_allServers);
                await _tileNotificationService.UpdateServerTilesAsync(_allServers);

                if (!isBackgroundRefresh)
                {
                    IReadOnlyList<ServerGroupSummary> groups = Array.Empty<ServerGroupSummary>();
                    try
                    {
                        groups = await _serverApiClient.GetServerGroupsAsync(CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Load server groups failed");
                    }

                    RebuildCategories(groups);
                }

                ApplyFilter(!isBackgroundRefresh);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Refresh server list failed");
                if (!isBackgroundRefresh)
                {
                    ErrorMessage = ex.Message;
                    StatusMessage = "刷新失败";
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void RebuildCategories(IReadOnlyList<ServerGroupSummary> groups)
        {
            _groupServerMap.Clear();

            var currentSelectionExists = false;
            Categories.Clear();
            Categories.Add(AllCategoryName);

            foreach (var group in groups)
            {
                if (string.IsNullOrWhiteSpace(group.Name))
                {
                    continue;
                }

                if (_groupServerMap.ContainsKey(group.Name))
                {
                    continue;
                }

                _groupServerMap[group.Name] = new HashSet<ulong>(group.ServerIds);
                Categories.Add(group.Name);

                if (string.Equals(group.Name, SelectedCategory, StringComparison.OrdinalIgnoreCase))
                {
                    currentSelectionExists = true;
                }
            }

            if (!string.Equals(SelectedCategory, AllCategoryName, StringComparison.OrdinalIgnoreCase) && !currentSelectionExists)
            {
                SelectedCategory = AllCategoryName;
            }
        }

        private void ApplyFilter(bool updateStatusMessage = true)
        {
            var currentCategory = string.IsNullOrWhiteSpace(SelectedCategory) ? AllCategoryName : SelectedCategory;
            IEnumerable<ServerSummary> filtered = _allServers;

            if (!string.Equals(currentCategory, AllCategoryName, StringComparison.OrdinalIgnoreCase)
                && _groupServerMap.TryGetValue(currentCategory, out var idSet)
                && idSet.Count > 0)
            {
                filtered = _allServers.Where(x => idSet.Contains(x.Id));
            }

            if (!string.IsNullOrWhiteSpace(SearchKeyword))
            {
                filtered = filtered.Where(MatchesKeyword);
            }

            if (!(ShowOnline && ShowOffline))
            {
                if (ShowOnline)
                {
                    filtered = filtered.Where(x => x.IsOnline);
                }
                else if (ShowOffline)
                {
                    filtered = filtered.Where(x => !x.IsOnline);
                }
                else
                {
                    filtered = Enumerable.Empty<ServerSummary>();
                }
            }

            var target = filtered.ToList();
            SyncServersCollection(target);

            if (updateStatusMessage)
            {
                StatusMessage = $"最近刷新：{DateTime.Now:HH:mm:ss}，分类：{currentCategory}，显示 {Servers.Count}/{_allServers.Count} 台";
            }
        }

        private bool MatchesKeyword(ServerSummary server)
        {
            var keyword = SearchKeyword;
            if (string.IsNullOrWhiteSpace(keyword))
            {
                return true;
            }

            return (server.Name?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false)
                || (server.Ip?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false)
                || (server.System?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false)
                || (server.Status?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false)
                || server.Tags.Any(t => t.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        private void OpenDetail()
        {
            if (SelectedServer == null)
            {
                return;
            }

            ServerSelected?.Invoke(this, SelectedServer.Id);
        }

        private void SyncServersCollection(IReadOnlyList<ServerSummary> target)
        {
            var i = 0;
            for (; i < target.Count; i++)
            {
                if (i >= Servers.Count)
                {
                    Servers.Add(target[i]);
                    continue;
                }

                if (Servers[i].Id != target[i].Id)
                {
                    var existingIndex = FindServerIndexById(target[i].Id, i + 1);
                    if (existingIndex >= 0)
                    {
                        Servers.Move(existingIndex, i);
                    }
                    else
                    {
                        Servers.Insert(i, target[i]);
                    }
                }

                if (!ReferenceEquals(Servers[i], target[i]))
                {
                    Servers[i] = target[i];
                }
            }

            while (Servers.Count > target.Count)
            {
                Servers.RemoveAt(Servers.Count - 1);
            }
        }

        private int FindServerIndexById(ulong id, int startIndex)
        {
            for (var i = startIndex; i < Servers.Count; i++)
            {
                if (Servers[i].Id == id)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
