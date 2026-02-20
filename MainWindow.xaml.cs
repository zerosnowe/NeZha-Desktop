using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NeZha_Desktop.Infrastructure.Settings;
using NeZha_Desktop.Views;
using WinRT.Interop;

namespace NeZha_Desktop
{
    public sealed partial class MainWindow : Window
    {
        private bool _canGoBackInShell;
        private bool _allowClose;
        private AppWindow? _appWindow;

        public MainWindow()
        {
            this.InitializeComponent();
            ApplyTheme(ThemePreferenceStore.LoadTheme());
            TryApplyBackdrop();
            TryConfigureTitleBar();
            this.RootFrame.Navigated += RootFrame_Navigated;
            this.RootFrame.Navigate(typeof(LoginPage));
            ConfigureCloseBehavior();
            UpdateTitleBarContext();
        }

        public bool Navigate(Type pageType, object? parameter = null)
        {
            if (parameter == null)
            {
                return this.RootFrame.Navigate(pageType);
            }

            return this.RootFrame.Navigate(pageType, parameter);
        }

        public void ApplyTheme(ElementTheme theme)
        {
            if (this.Content is FrameworkElement root)
            {
                root.RequestedTheme = theme;
            }
        }

        public void UpdateShellBackState(bool canGoBack)
        {
            _canGoBackInShell = canGoBack;
            BackButton.IsEnabled = canGoBack;
            BackButton.Opacity = canGoBack ? 1.0 : 0.4;
        }

        public void ShowFromTray()
        {
            try
            {
                _appWindow?.Show();
            }
            catch
            {
                // ignore
            }

            this.Activate();
        }

        public void ExitApplication()
        {
            _allowClose = true;
            try
            {
                this.Close();
            }
            catch
            {
                // ignore
            }

            App.Current.Exit();
        }

        private void RootFrame_Navigated(object sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            UpdateTitleBarContext();
        }

        private void GlobalSearchBox_OnTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (RootFrame.Content is ShellPage shellPage)
            {
                shellPage.ApplyGlobalSearch(sender.Text);
            }
        }

        private void GlobalSearchBox_OnQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            if (RootFrame.Content is ShellPage shellPage)
            {
                shellPage.ApplyGlobalSearch(sender.Text);
            }
        }

        private void BackButton_OnClick(object sender, RoutedEventArgs e)
        {
            if (!_canGoBackInShell)
            {
                return;
            }

            if (RootFrame.Content is ShellPage shellPage)
            {
                shellPage.GoBackInContent();
            }
        }

        private void UpdateTitleBarContext()
        {
            var isShell = RootFrame.Content is ShellPage;
            BackButton.Visibility = isShell ? Visibility.Visible : Visibility.Collapsed;
            GlobalSearchBox.Visibility = isShell ? Visibility.Visible : Visibility.Collapsed;

            if (!isShell)
            {
                UpdateShellBackState(false);
            }
        }

        private void ConfigureCloseBehavior()
        {
            try
            {
                var hwnd = WindowNative.GetWindowHandle(this);
                var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
                _appWindow = AppWindow.GetFromWindowId(windowId);
                TrySetWindowIcon();
                _appWindow.Closing += AppWindow_Closing;
            }
            catch
            {
                // ignore
            }
        }

        private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            if (_allowClose)
            {
                return;
            }

            args.Cancel = true;
            _appWindow?.Hide();
        }

        private void TryApplyBackdrop()
        {
            try
            {
                this.SystemBackdrop = new MicaBackdrop();
            }
            catch
            {
                // Ignore on unsupported unpackaged runtime.
            }
        }

        private void TryConfigureTitleBar()
        {
            try
            {
                this.ExtendsContentIntoTitleBar = true;
                this.SetTitleBar(AppTitleBar);
            }
            catch
            {
                try
                {
                    this.ExtendsContentIntoTitleBar = false;
                }
                catch
                {
                    // ignore
                }
            }
        }

        private void TrySetWindowIcon()
        {
            try
            {
                if (_appWindow is null)
                {
                    return;
                }

                var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
                if (File.Exists(iconPath))
                {
                    _appWindow.SetIcon(iconPath);
                    return;
                }

                var processPath = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(processPath) && File.Exists(processPath))
                {
                    _appWindow.SetIcon(processPath);
                }
            }
            catch
            {
                // ignore
            }
        }
    }
}
