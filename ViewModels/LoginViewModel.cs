using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NeZha_Desktop.Contracts;
using NeZha_Desktop.Models;
using Serilog;

namespace NeZha_Desktop.ViewModels
{
    public sealed class LoginViewModel : ObservableObject
    {
        private readonly IAuthSessionService _authSessionService;
        private readonly IPanelProfileStore _panelProfileStore;

        private string _dashboardUrl = string.Empty;
        private string _username = string.Empty;
        private string _password = string.Empty;
        private string _apiToken = string.Empty;
        private bool _publicMode;
        private bool _rememberLogin = true;
        private bool _isBusy;
        private string _errorMessage = string.Empty;
        private string _infoMessage = string.Empty;
        private DashboardProfile? _selectedProfile;

        public LoginViewModel(IAuthSessionService authSessionService, IPanelProfileStore panelProfileStore)
        {
            _authSessionService = authSessionService;
            _panelProfileStore = panelProfileStore;
            Profiles = new ObservableCollection<DashboardProfile>();

            LoginCommand = new AsyncRelayCommand(LoginAsync, CanLogin);
            InitializeCommand = new AsyncRelayCommand(InitializeAsync);
        }

        public event EventHandler? LoginSucceeded;

        public ObservableCollection<DashboardProfile> Profiles { get; }

        public IAsyncRelayCommand LoginCommand { get; }

        public IAsyncRelayCommand InitializeCommand { get; }

        public string DashboardUrl
        {
            get => _dashboardUrl;
            set
            {
                if (SetProperty(ref _dashboardUrl, value))
                {
                    LoginCommand.NotifyCanExecuteChanged();
                }
            }
        }

        public string Username
        {
            get => _username;
            set
            {
                if (SetProperty(ref _username, value))
                {
                    LoginCommand.NotifyCanExecuteChanged();
                }
            }
        }

        public string Password
        {
            get => _password;
            set
            {
                if (SetProperty(ref _password, value))
                {
                    LoginCommand.NotifyCanExecuteChanged();
                }
            }
        }

        public string ApiToken
        {
            get => _apiToken;
            set => SetProperty(ref _apiToken, value);
        }

        public bool PublicMode
        {
            get => _publicMode;
            set
            {
                if (SetProperty(ref _publicMode, value))
                {
                    OnPropertyChanged(nameof(ConnectButtonText));
                    LoginCommand.NotifyCanExecuteChanged();
                }
            }
        }

        public string ConnectButtonText => PublicMode ? "进入公开面板" : "登录";

        public bool RememberLogin
        {
            get => _rememberLogin;
            set => SetProperty(ref _rememberLogin, value);
        }

        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    LoginCommand.NotifyCanExecuteChanged();
                }
            }
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            set => SetProperty(ref _errorMessage, value);
        }

        public string InfoMessage
        {
            get => _infoMessage;
            set => SetProperty(ref _infoMessage, value);
        }

        public DashboardProfile? SelectedProfile
        {
            get => _selectedProfile;
            set
            {
                if (!SetProperty(ref _selectedProfile, value) || value == null)
                {
                    return;
                }

                DashboardUrl = value.DashboardUrl;
                PublicMode = value.PublicMode;
                Username = value.PublicMode ? string.Empty : value.Username;
                RememberLogin = value.RememberLogin;
            }
        }

        private bool CanLogin()
        {
            if (IsBusy || string.IsNullOrWhiteSpace(DashboardUrl))
            {
                return false;
            }

            if (PublicMode)
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password);
        }

        private async Task InitializeAsync()
        {
            if (IsBusy)
            {
                return;
            }

            try
            {
                IsBusy = true;
                ErrorMessage = string.Empty;
                InfoMessage = "正在加载面板配置...";
                await LoadProfilesAsync();
                InfoMessage = "请手动登录或使用公开模式连接面板";
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Initialize login view failed");
                ErrorMessage = ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task LoginAsync()
        {
            if (!CanLogin())
            {
                return;
            }

            try
            {
                IsBusy = true;
                ErrorMessage = string.Empty;
                InfoMessage = PublicMode ? "正在连接公开面板..." : "正在登录...";

                AuthSession session;
                if (PublicMode)
                {
                    session = await _authSessionService.EnterPublicModeAsync(
                        DashboardUrl,
                        ApiToken,
                        RememberLogin,
                        CancellationToken.None);
                }
                else
                {
                    session = await _authSessionService.SignInAsync(
                        DashboardUrl,
                        Username,
                        Password,
                        RememberLogin,
                        CancellationToken.None);
                    Password = string.Empty;
                }

                InfoMessage = PublicMode
                    ? $"已连接公开面板：{session.DashboardUrl}"
                    : $"登录成功：{session.Username}@{session.DashboardUrl}";
                LoginSucceeded?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Login/connect failed");
                ErrorMessage = ex.Message;
                InfoMessage = string.Empty;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task LoadProfilesAsync()
        {
            var profiles = await _panelProfileStore.GetProfilesAsync();
            Profiles.Clear();
            foreach (var profile in profiles)
            {
                Profiles.Add(profile);
            }

            var last = await _panelProfileStore.GetLastUsedAsync();
            if (last == null)
            {
                return;
            }

            var selected = Profiles.FirstOrDefault(p => p.Id == last.Id) ?? Profiles.FirstOrDefault();
            SelectedProfile = selected;
        }
    }
}
