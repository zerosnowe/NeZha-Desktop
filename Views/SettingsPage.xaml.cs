using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NeZha_Desktop.Contracts;
using NeZha_Desktop.Infrastructure.Settings;
using System.Reflection;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace NeZha_Desktop.Views
{
    public sealed partial class SettingsPage : Page
    {
        private readonly IDesktopWidgetService _desktopWidgetService;
        private bool _isInitializing;

        public SettingsPage()
        {
            this.InitializeComponent();
            _desktopWidgetService = App.HostContainer.Services.GetRequiredService<IDesktopWidgetService>();
            this.Loaded += SettingsPage_Loaded;
        }

        private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
        {
            _isInitializing = true;
            try
            {
                var theme = ThemePreferenceStore.LoadTheme();
                ThemeComboBox.SelectedIndex = theme switch
                {
                    ElementTheme.Light => 1,
                    ElementTheme.Dark => 2,
                    _ => 0,
                };

                DesktopWidgetToggle.IsOn = _desktopWidgetService.IsEnabled;
                WidgetBackdropComboBox.SelectedIndex = _desktopWidgetService.BackdropMode switch
                {
                    "Acrylic" => 1,
                    "Custom" => 2,
                    "TextOnly" => 3,
                    _ => 0,
                };
                CustomBackgroundPathText.Text = string.IsNullOrWhiteSpace(_desktopWidgetService.CustomBackgroundPath)
                    ? "未选择背景文件"
                    : _desktopWidgetService.CustomBackgroundPath;
                CustomBackgroundRow.Visibility = string.Equals(_desktopWidgetService.BackdropMode, "Custom", StringComparison.OrdinalIgnoreCase)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
            finally
            {
                _isInitializing = false;
            }

            AboutDescriptionText.Text = $"NeZha Desktop · {GetAppVersionText()}";
        }

        private void ThemeComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing || ThemeComboBox.SelectedItem is not ComboBoxItem selected)
            {
                return;
            }

            var theme = selected.Tag?.ToString() switch
            {
                "Light" => ElementTheme.Light,
                "Dark" => ElementTheme.Dark,
                _ => ElementTheme.Default,
            };

            ThemePreferenceStore.SaveTheme(theme);
            App.MainAppWindow?.ApplyTheme(theme);
        }

        private async void DesktopWidgetToggle_OnToggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing)
            {
                return;
            }

            await _desktopWidgetService.SetEnabledAsync(DesktopWidgetToggle.IsOn);
        }

        private async void WidgetBackdropComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing || WidgetBackdropComboBox.SelectedItem is not ComboBoxItem selected)
            {
                return;
            }

            var mode = selected.Tag?.ToString() ?? "Mica";
            await _desktopWidgetService.SetBackdropModeAsync(mode);
            CustomBackgroundRow.Visibility = string.Equals(mode, "Custom", StringComparison.OrdinalIgnoreCase)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private async void SelectCustomBackgroundButton_OnClick(object sender, RoutedEventArgs e)
        {
            if (App.MainAppWindow is null)
            {
                return;
            }

            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".webp");
            picker.FileTypeFilter.Add(".bmp");

            var hwnd = WindowNative.GetWindowHandle(App.MainAppWindow);
            InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file is null)
            {
                return;
            }

            CustomBackgroundPathText.Text = file.Path;
            await _desktopWidgetService.SetCustomBackgroundPathAsync(file.Path);
        }

        private async void ClearCustomBackgroundButton_OnClick(object sender, RoutedEventArgs e)
        {
            CustomBackgroundPathText.Text = "未选择背景文件";
            await _desktopWidgetService.SetCustomBackgroundPathAsync(null);
        }

        private static string GetAppVersionText()
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            if (version == null)
            {
                return "v1.0.0";
            }

            return $"v{version.Major}.{version.Minor}.{version.Build}";
        }
    }
}
