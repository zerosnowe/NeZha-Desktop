using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NeZha_Desktop.Contracts;
using NeZha_Desktop.Models;
using Serilog;
using System.Collections.ObjectModel;

namespace NeZha_Desktop.ViewModels
{
    public sealed class ServerDetailViewModel : ObservableObject
    {
        private readonly IServerApiClient _serverApiClient;

        private bool _isBusy;
        private bool _hideLoadingIndicator;
        private bool _isNetworkTab;
        private string _errorMessage = string.Empty;
        private ServerDetail _detail = new();
        private string _monitorPeriod = "1d";

        public ServerDetailViewModel(IServerApiClient serverApiClient)
        {
            _serverApiClient = serverApiClient;
            RefreshCommand = new AsyncRelayCommand(() => RefreshAsync(false), () => !IsBusy && ServerId > 0);
            ShowDetailTabCommand = new RelayCommand(() => IsNetworkTab = false);
            ShowNetworkTabCommand = new RelayCommand(() => IsNetworkTab = true);
            SetMonitorPeriod1dCommand = new AsyncRelayCommand(() => ChangeMonitorPeriodAsync("1d"), () => !IsBusy && ServerId > 0 && MonitorPeriod != "1d");
            SetMonitorPeriod7dCommand = new AsyncRelayCommand(() => ChangeMonitorPeriodAsync("7d"), () => !IsBusy && ServerId > 0 && MonitorPeriod != "7d");
            SetMonitorPeriod30dCommand = new AsyncRelayCommand(() => ChangeMonitorPeriodAsync("30d"), () => !IsBusy && ServerId > 0 && MonitorPeriod != "30d");
        }

        public ulong ServerId { get; private set; }

        public IAsyncRelayCommand RefreshCommand { get; }

        public IRelayCommand ShowDetailTabCommand { get; }

        public IRelayCommand ShowNetworkTabCommand { get; }

        public IAsyncRelayCommand SetMonitorPeriod1dCommand { get; }

        public IAsyncRelayCommand SetMonitorPeriod7dCommand { get; }

        public IAsyncRelayCommand SetMonitorPeriod30dCommand { get; }

        public ObservableCollection<NetworkMonitorSummary> NetworkMonitors { get; } = [];

        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    RefreshCommand.NotifyCanExecuteChanged();
                    SetMonitorPeriod1dCommand.NotifyCanExecuteChanged();
                    SetMonitorPeriod7dCommand.NotifyCanExecuteChanged();
                    SetMonitorPeriod30dCommand.NotifyCanExecuteChanged();
                    OnPropertyChanged(nameof(ShowLoadingIndicator));
                }
            }
        }

        public bool ShowLoadingIndicator => IsBusy && !_hideLoadingIndicator;

        public string ErrorMessage
        {
            get => _errorMessage;
            set => SetProperty(ref _errorMessage, value);
        }

        public bool IsNetworkTab
        {
            get => _isNetworkTab;
            set
            {
                if (SetProperty(ref _isNetworkTab, value))
                {
                    OnPropertyChanged(nameof(IsDetailTab));
                }
            }
        }

        public bool IsDetailTab => !IsNetworkTab;

        public ServerDetail Detail
        {
            get => _detail;
            set => SetProperty(ref _detail, value);
        }

        public string MonitorPeriod
        {
            get => _monitorPeriod;
            private set
            {
                if (SetProperty(ref _monitorPeriod, value))
                {
                    OnPropertyChanged(nameof(MonitorPeriodDisplay));
                    SetMonitorPeriod1dCommand.NotifyCanExecuteChanged();
                    SetMonitorPeriod7dCommand.NotifyCanExecuteChanged();
                    SetMonitorPeriod30dCommand.NotifyCanExecuteChanged();
                }
            }
        }

        public string MonitorPeriodDisplay => MonitorPeriod switch
        {
            "7d" => "7天",
            "30d" => "30天",
            _ => "1天",
        };

        public async Task LoadAsync(ulong serverId)
        {
            ServerId = serverId;
            IsNetworkTab = false;
            RefreshCommand.NotifyCanExecuteChanged();
            await RefreshAsync(false);
        }

        public async Task RefreshAsync(bool silentRefresh = false)
        {
            if (ServerId == 0 || IsBusy)
            {
                return;
            }

            try
            {
                _hideLoadingIndicator = silentRefresh;
                OnPropertyChanged(nameof(ShowLoadingIndicator));
                IsBusy = true;
                if (!silentRefresh)
                {
                    ErrorMessage = string.Empty;
                }

                var detailTask = _serverApiClient.GetServerDetailAsync(ServerId, CancellationToken.None);
                var monitorTask = _serverApiClient.GetServerNetworkMonitorsAsync(ServerId, MonitorPeriod, CancellationToken.None);

                Detail = await detailTask;

                IReadOnlyList<NetworkMonitorSummary> monitors = Array.Empty<NetworkMonitorSummary>();
                try
                {
                    monitors = await monitorTask;
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Load network monitor failed. ServerId={ServerId}, Period={Period}", ServerId, MonitorPeriod);
                }

                UpdateNetworkMonitors(monitors);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Load server detail failed. ServerId={ServerId}", ServerId);
                if (!silentRefresh)
                {
                    ErrorMessage = ex.Message;
                }
            }
            finally
            {
                IsBusy = false;
                _hideLoadingIndicator = false;
                OnPropertyChanged(nameof(ShowLoadingIndicator));
            }
        }

        private async Task ChangeMonitorPeriodAsync(string period)
        {
            if (string.Equals(MonitorPeriod, period, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            MonitorPeriod = period;
            await RefreshAsync(false);
        }

        private void UpdateNetworkMonitors(IReadOnlyList<NetworkMonitorSummary> monitors)
        {
            NetworkMonitors.Clear();
            foreach (var item in monitors)
            {
                NetworkMonitors.Add(item);
            }
        }
    }
}
