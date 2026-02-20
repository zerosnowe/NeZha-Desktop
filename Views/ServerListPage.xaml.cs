using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Navigation;
using NeZha_Desktop.Models;
using NeZha_Desktop.ViewModels;
using Windows.Foundation;

namespace NeZha_Desktop.Views
{
    public sealed partial class ServerListPage : Page
    {
        private bool _forceRefreshOnLoad;
        private readonly MenuFlyout _statusFilterFlyout = new();
        private readonly ToggleMenuFlyoutItem _showOnlineMenuItem = new() { Text = "在线", IsChecked = true };
        private readonly ToggleMenuFlyoutItem _showOfflineMenuItem = new() { Text = "离线", IsChecked = true };

        public ServerListViewModel ViewModel { get; }

        public ServerListPage()
        {
            ViewModel = App.HostContainer.Services.GetRequiredService<ServerListViewModel>();
            this.InitializeComponent();
            this.DataContext = ViewModel;

            this.Loaded += this.ServerListPage_Loaded;
            ViewModel.ServerSelected += this.ViewModel_ServerSelected;

            _showOnlineMenuItem.Click += ShowOnlineMenuItem_OnClick;
            _showOfflineMenuItem.Click += ShowOfflineMenuItem_OnClick;
            _statusFilterFlyout.Items.Add(_showOnlineMenuItem);
            _statusFilterFlyout.Items.Add(_showOfflineMenuItem);
        }

        private async void ServerListPage_Loaded(object sender, RoutedEventArgs e)
        {
            ViewModel.SelectedCategory = "全部";

            if (_forceRefreshOnLoad)
            {
                _forceRefreshOnLoad = false;
                await ViewModel.RefreshAsync(false);
            }
            else
            {
                await ViewModel.EnsureDataLoadedAsync();
            }
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _forceRefreshOnLoad = e.Parameter is not null;
        }

        private void ServerList_OnItemClick(object sender, ItemClickEventArgs e)
        {
            var server = e.ClickedItem as ServerSummary;
            if (server == null)
            {
                return;
            }

            if (this.Frame != null)
            {
                this.Frame.Navigate(typeof(ServerDetailPage), server.Id);
                return;
            }

            if (App.MainAppWindow != null)
            {
                App.MainAppWindow.Navigate(typeof(ServerDetailPage), server.Id);
            }
        }

        private void ViewModel_ServerSelected(object? sender, ulong serverId)
        {
            if (this.Frame != null)
            {
                this.Frame.Navigate(typeof(ServerDetailPage), serverId);
                return;
            }

            if (App.MainAppWindow != null)
            {
                App.MainAppWindow.Navigate(typeof(ServerDetailPage), serverId);
            }
        }

        public void SetSearchKeyword(string? keyword)
        {
            ViewModel.SetSearchKeyword(keyword);
        }

        public async Task ForceRefreshAsync()
        {
            await ViewModel.RefreshAsync(false);
        }

        private void ShowOnlineMenuItem_OnClick(object sender, RoutedEventArgs e)
        {
            ApplyStatusFilterFromMenu();
        }

        private void ShowOfflineMenuItem_OnClick(object sender, RoutedEventArgs e)
        {
            ApplyStatusFilterFromMenu();
        }

        private void ApplyStatusFilterFromMenu()
        {
            var showOnline = _showOnlineMenuItem.IsChecked;
            var showOffline = _showOfflineMenuItem.IsChecked;

            // Keep at least one option enabled to avoid accidental empty lock state.
            if (!showOnline && !showOffline)
            {
                _showOfflineMenuItem.IsChecked = true;
                showOffline = true;
            }

            ViewModel.ShowOnline = showOnline;
            ViewModel.ShowOffline = showOffline;
        }

        private void StatusFilterButton_OnClick(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element)
            {
                return;
            }

            _showOnlineMenuItem.IsChecked = ViewModel.ShowOnline;
            _showOfflineMenuItem.IsChecked = ViewModel.ShowOffline;

            _statusFilterFlyout.ShowAt(element, new FlyoutShowOptions
            {
                Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft,
                Position = new Point(0, element.ActualHeight)
            });
        }
    }
}
