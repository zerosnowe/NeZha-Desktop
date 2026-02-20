using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NeZha_Desktop.ViewModels;

namespace NeZha_Desktop.Views
{
    public sealed partial class LoginPage : Page
    {
        public LoginViewModel ViewModel { get; }

        public LoginPage()
        {
            ViewModel = App.HostContainer.Services.GetRequiredService<LoginViewModel>();
            this.InitializeComponent();
            this.DataContext = ViewModel;
            this.Loaded += this.LoginPage_Loaded;
            ViewModel.LoginSucceeded += this.ViewModel_LoginSucceeded;
        }

        private async void LoginPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (ViewModel.InitializeCommand.CanExecute(null))
            {
                await ViewModel.InitializeCommand.ExecuteAsync(null);
            }
        }

        private void PasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
        {
            var box = sender as PasswordBox;
            if (box != null)
            {
                ViewModel.Password = box.Password;
            }
        }

        private void ViewModel_LoginSucceeded(object? sender, EventArgs e)
        {
            if (App.MainAppWindow != null)
            {
                App.MainAppWindow.Navigate(typeof(ShellPage));
                return;
            }

            if (this.Frame != null)
            {
                this.Frame.Navigate(typeof(ShellPage));
            }
        }
    }
}
