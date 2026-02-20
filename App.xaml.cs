using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using NeZha_Desktop.Contracts;
using NeZha_Desktop.Infrastructure.Api;
using NeZha_Desktop.Infrastructure.Logging;
using NeZha_Desktop.Infrastructure.Security;
using NeZha_Desktop.Infrastructure.Settings;
using NeZha_Desktop.Services;
using NeZha_Desktop.ViewModels;
using Serilog;
using System.Runtime.InteropServices;

namespace NeZha_Desktop
{
    public partial class App : Application
    {
        public static IHost HostContainer { get; private set; } = null!;
        public static MainWindow? MainAppWindow { get; private set; }

        private readonly IHost _host;
        private Window? _window;

        public App()
        {
            LoggingConfigurator.Configure();

            _host = Host.CreateDefaultBuilder()
                .UseSerilog()
                .ConfigureServices((_, services) =>
                {
                    services.AddSingleton<ITokenStore, PasswordVaultTokenStore>();
                    services.AddSingleton<IPanelProfileStore, PanelProfileStore>();
                    services.AddSingleton<IAuthApiClient, AuthApiClient>();
                    services.AddSingleton<IServerApiClient, ServerApiClient>();
                    services.AddSingleton<IServerListSnapshotService, ServerListSnapshotService>();
                    services.AddSingleton<IAuthSessionService, AuthSessionService>();
                    services.AddSingleton<ITileNotificationService, TileNotificationService>();
                    services.AddSingleton<IDesktopWidgetService, DesktopWidgetService>();
                    services.AddSingleton<ITrayIconService, TrayIconService>();

                    services.AddTransient<AuthHeaderHandler>();

                    services.AddHttpClient("NezhaAuth", client =>
                    {
                        client.Timeout = TimeSpan.FromSeconds(20);
                    });

                    services.AddHttpClient("NezhaApi", client =>
                    {
                        client.Timeout = TimeSpan.FromSeconds(20);
                    }).AddHttpMessageHandler<AuthHeaderHandler>();

                    services.AddTransient<LoginViewModel>();
                    services.AddTransient<ServerListViewModel>();
                    services.AddTransient<ServerDetailViewModel>();
                })
                .Build();

            HostContainer = _host;

            this.InitializeComponent();
            this.UnhandledException += this.App_UnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        }

        protected override async void OnLaunched(LaunchActivatedEventArgs args)
        {
            try
            {
                await _host.StartAsync();
                MainAppWindow = new MainWindow();
                _window = MainAppWindow;
                _window.Activate();

                try
                {
                    var trayService = _host.Services.GetRequiredService<ITrayIconService>();
                    trayService.Initialize(MainAppWindow);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Tray icon initialization failed. Continue without tray.");
                }

                try
                {
                    var widgetService = _host.Services.GetRequiredService<IDesktopWidgetService>();
                    await widgetService.RestoreAsync();
                }
                catch (COMException ex)
                {
                    Log.Warning(ex, "Desktop widget restore COM failure in unpackaged mode. Disabled for this startup.");
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Desktop widget restore failed. Continue without widget.");
                }
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Application failed during launch.");
                return;
            }
        }

        private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            Log.Error(e.Exception, "Unhandled exception");
        }

        private static void CurrentDomain_UnhandledException(object sender, System.UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                Log.Error(ex, "AppDomain unhandled exception");
                return;
            }

            Log.Error("AppDomain unhandled non-exception object: {Object}", e.ExceptionObject);
        }

        private static void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            Log.Error(e.Exception, "Unobserved task exception");
            e.SetObserved();
        }
    }
}

