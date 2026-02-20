using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NeZha_Desktop.Contracts;
using NeZha_Desktop.Models;

namespace NeZha_Desktop.Views
{
    public sealed partial class ShellPage : Page
    {
        private readonly IPanelProfileStore _panelProfileStore;
        private readonly IAuthSessionService _authSessionService;
        private readonly Dictionary<string, NavigationViewItem> _profileItemsById = new(StringComparer.Ordinal);
        private readonly Dictionary<string, DashboardProfile> _profilesById = new(StringComparer.Ordinal);

        private bool _isInternalSelectionUpdate;
        private string _currentSearchKeyword = string.Empty;

        public ShellPage()
        {
            _panelProfileStore = App.HostContainer.Services.GetRequiredService<IPanelProfileStore>();
            _authSessionService = App.HostContainer.Services.GetRequiredService<IAuthSessionService>();
            this.InitializeComponent();
            this.Loaded += this.ShellPage_Loaded;
            this.ContentFrame.Navigated += this.ContentFrame_Navigated;
        }

        public void TogglePane()
        {
            AppNavigationView.IsPaneOpen = !AppNavigationView.IsPaneOpen;
        }

        public void GoBackInContent()
        {
            if (ContentFrame.CanGoBack)
            {
                ContentFrame.GoBack();
            }
        }

        public void ApplyGlobalSearch(string? keyword)
        {
            _currentSearchKeyword = keyword?.Trim() ?? string.Empty;
            if (ContentFrame.Content is ServerListPage page)
            {
                page.SetSearchKeyword(_currentSearchKeyword);
            }
        }

        private async void ShellPage_Loaded(object sender, RoutedEventArgs e)
        {
            LocalizeBuiltInSettingsItem();
            await LoadProfileMenuAsync();

            if (ContentFrame.Content == null)
            {
                ContentFrame.Navigate(typeof(ServerListPage));
            }

            SelectCurrentProfileItem();
            MonitorRootItem.IsExpanded = true;
            App.MainAppWindow?.UpdateShellBackState(ContentFrame.CanGoBack);
        }

        private async Task LoadProfileMenuAsync()
        {
            try
            {
                var profiles = await _panelProfileStore.GetProfilesAsync();
                var distinctProfiles = DeduplicateProfilesByAddress(profiles);
                RebuildProfileSubMenu(distinctProfiles.OrderByDescending(p => p.LastUsedUtc));
            }
            catch
            {
                RebuildProfileSubMenu(Array.Empty<DashboardProfile>());
            }
        }

        private static IReadOnlyList<DashboardProfile> DeduplicateProfilesByAddress(IReadOnlyList<DashboardProfile> profiles)
        {
            var map = new Dictionary<string, DashboardProfile>(StringComparer.OrdinalIgnoreCase);

            foreach (var profile in profiles)
            {
                var key = NormalizeAddressKey(profile.DashboardUrl);
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                if (!map.TryGetValue(key, out var existing) || profile.LastUsedUtc > existing.LastUsedUtc)
                {
                    map[key] = profile;
                }
            }

            return map.Values.ToList();
        }

        private void RebuildProfileSubMenu(IEnumerable<DashboardProfile> profiles)
        {
            _isInternalSelectionUpdate = true;
            try
            {
                AppNavigationView.SelectedItem = null;
            }
            finally
            {
                _isInternalSelectionUpdate = false;
            }

            MonitorRootItem.MenuItems.Clear();
            _profileItemsById.Clear();
            _profilesById.Clear();

            foreach (var profile in profiles)
            {
                if (string.IsNullOrWhiteSpace(profile.Id))
                {
                    continue;
                }

                var child = new NavigationViewItem
                {
                    Content = BuildProfileLabel(profile),
                    Tag = $"profile:{profile.Id}",
                    Icon = new SymbolIcon(Symbol.Globe),
                };

                _profileItemsById[profile.Id] = child;
                _profilesById[profile.Id] = profile;
                MonitorRootItem.MenuItems.Add(child);
            }

            MonitorRootItem.IsExpanded = true;
        }

        private void AppNavigationView_OnItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
        {
            if (args.InvokedItemContainer?.Tag is string tag
                && string.Equals(tag, "monitor_root", StringComparison.Ordinal))
            {
                ToggleMonitorRoot();
                return;
            }

            if (args.InvokedItem is string invokedText
                && string.Equals(invokedText.Trim(), "哪吒监控", StringComparison.Ordinal))
            {
                ToggleMonitorRoot();
            }
        }

        private void AppNavigationView_OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (_isInternalSelectionUpdate)
            {
                return;
            }

            if (args.IsSettingsSelected)
            {
                NavigateTo(typeof(SettingsPage));
                return;
            }

            var tag = args.SelectedItemContainer?.Tag?.ToString();
            if (string.IsNullOrWhiteSpace(tag))
            {
                return;
            }

            if (string.Equals(tag, "uptime_kuma", StringComparison.Ordinal))
            {
                NavigateTo(typeof(UptimeKumaPage));
                return;
            }

            if (tag.StartsWith("profile:", StringComparison.Ordinal))
            {
                var profileId = tag["profile:".Length..];
                _ = SwitchProfileAsync(profileId);
            }
        }

        private void LocalizeBuiltInSettingsItem()
        {
            if (AppNavigationView.SettingsItem is NavigationViewItem settingsItem)
            {
                settingsItem.Content = "设置";
            }
        }

        private async Task SwitchProfileAsync(string profileId)
        {
            if (_profilesById.TryGetValue(profileId, out var profile) && IsCurrentSessionForProfile(profile))
            {
                await ReloadServerListPageAsync();
                return;
            }

            try
            {
                await _panelProfileStore.SetLastUsedAsync(profileId);
                await _authSessionService.TryAutoSignInAsync(CancellationToken.None);
            }
            catch
            {
                // Keep current session on switch failure.
            }

            await ReloadServerListPageAsync();
        }

        private async Task RefreshServerListAsync()
        {
            if (ContentFrame.Content is ServerListPage page)
            {
                await page.ForceRefreshAsync();
                page.SetSearchKeyword(_currentSearchKeyword);
                return;
            }

            ContentFrame.Navigate(typeof(ServerListPage), true);
            ApplyGlobalSearch(_currentSearchKeyword);
        }

        private Task ReloadServerListPageAsync()
        {
            // Use a unique parameter to force Frame to create a fresh page instance.
            ContentFrame.Navigate(typeof(ServerListPage), Guid.NewGuid().ToString("N"));
            ApplyGlobalSearch(_currentSearchKeyword);
            return Task.CompletedTask;
        }

        private void ContentFrame_Navigated(object sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            App.MainAppWindow?.UpdateShellBackState(ContentFrame.CanGoBack);

            if (ContentFrame.Content is ServerListPage page)
            {
                page.SetSearchKeyword(_currentSearchKeyword);
            }

            _isInternalSelectionUpdate = true;
            try
            {
                if (e.SourcePageType == typeof(UptimeKumaPage))
                {
                    AppNavigationView.SelectedItem = UptimeKumaItem;
                }
                else if (e.SourcePageType == typeof(SettingsPage))
                {
                    AppNavigationView.SelectedItem = AppNavigationView.SettingsItem;
                }
                else if (e.SourcePageType == typeof(ServerListPage) || e.SourcePageType == typeof(ServerDetailPage))
                {
                    MonitorRootItem.IsExpanded = true;
                    SelectCurrentProfileItem();
                }
            }
            finally
            {
                _isInternalSelectionUpdate = false;
            }
        }

        private bool IsCurrentSessionForProfile(DashboardProfile profile)
        {
            var session = _authSessionService.CurrentSession;
            if (session == null)
            {
                return false;
            }

            return string.Equals(
                NormalizeAddressKey(session.DashboardUrl),
                NormalizeAddressKey(profile.DashboardUrl),
                StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeAddressKey(string? rawUrl)
        {
            if (string.IsNullOrWhiteSpace(rawUrl))
            {
                return string.Empty;
            }

            if (!Uri.TryCreate(rawUrl.Trim(), UriKind.Absolute, out var uri))
            {
                return rawUrl.Trim().TrimEnd('/').ToLowerInvariant();
            }

            var path = uri.AbsolutePath.TrimEnd('/');
            return $"{uri.Scheme.ToLowerInvariant()}://{uri.Host.ToLowerInvariant()}:{uri.Port}{path}";
        }

        private static string BuildProfileLabel(DashboardProfile profile)
        {
            if (string.IsNullOrWhiteSpace(profile.DashboardUrl))
            {
                return "未命名地址";
            }

            if (!Uri.TryCreate(profile.DashboardUrl, UriKind.Absolute, out var uri))
            {
                return profile.DashboardUrl;
            }

            return profile.PublicMode ? $"{uri.Host} (公开)" : uri.Host;
        }

        private void SelectCurrentProfileItem()
        {
            var session = _authSessionService.CurrentSession;
            if (session == null)
            {
                ClearSelection();
                return;
            }

            var sessionKey = NormalizeAddressKey(session.DashboardUrl);
            foreach (var profile in _profilesById.Values)
            {
                if (!string.Equals(NormalizeAddressKey(profile.DashboardUrl), sessionKey, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (_profileItemsById.TryGetValue(profile.Id, out var item))
                {
                    AppNavigationView.SelectedItem = item;
                    MonitorRootItem.IsExpanded = true;
                    return;
                }
            }

            ClearSelection();
        }

        private void ClearSelection()
        {
            _isInternalSelectionUpdate = true;
            try
            {
                AppNavigationView.SelectedItem = null;
            }
            finally
            {
                _isInternalSelectionUpdate = false;
            }
        }

        private void NavigateTo(Type pageType)
        {
            if (ContentFrame.CurrentSourcePageType == pageType)
            {
                return;
            }

            ContentFrame.Navigate(pageType);
        }

        private void ToggleMonitorRoot()
        {
            MonitorRootItem.IsExpanded = !MonitorRootItem.IsExpanded;

            if (ReferenceEquals(AppNavigationView.SelectedItem, MonitorRootItem))
            {
                ClearSelection();
            }
        }
    }
}
