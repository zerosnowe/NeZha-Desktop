using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using NeZha_Desktop.Contracts;
using NeZha_Desktop.Infrastructure.Runtime;
using NeZha_Desktop.Infrastructure.Settings;
using NeZha_Desktop.Models;
using System.Runtime.InteropServices;
using Windows.Foundation;
using Windows.Graphics;
using WinRT.Interop;

namespace NeZha_Desktop.Views;

public sealed partial class DesktopWidgetWindow : Window
{
    private const int DefaultWidth = 420;
    private const int DefaultHeight = 220;
    private const int MinWidth = 320;
    private const int MinHeight = 180;
    private const int MaxWidth = 900;
    private const int MaxHeight = 520;
    private const int ResizeStepWidth = 40;
    private const int ResizeStepHeight = 24;

    private readonly IServerListSnapshotService _snapshotService;
    private readonly DispatcherTimer _rotationTimer;
    private readonly List<ServerSummary> _servers = [];
    private readonly MenuFlyout _menu;
    private AppWindow? _appWindow;
    private int _currentWidth = DefaultWidth;
    private int _currentHeight = DefaultHeight;
    private int _currentIndex;
    private string _backdropMode;
    private string? _customBackgroundPath;
    private bool _isDragging;
    private uint _dragPointerId;
    private POINT _dragStartCursor;
    private PointInt32 _dragStartWindowPosition;

    public event EventHandler? ExitRequested;

    public DesktopWidgetWindow(IServerListSnapshotService snapshotService, string backdropMode, string? customBackgroundPath)
    {
        InitializeComponent();
        _snapshotService = snapshotService;
        _backdropMode = NormalizeMode(backdropMode);
        _customBackgroundPath = string.IsNullOrWhiteSpace(customBackgroundPath) ? null : customBackgroundPath;
        ApplyAppearance(_backdropMode, _customBackgroundPath);

        _rotationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _rotationTimer.Tick += (_, _) =>
        {
            RotateNext();
            EnsureBottomMost();
        };

        _menu = BuildContextMenu();

        Activated += DesktopWidgetWindow_Activated;
        Closed += DesktopWidgetWindow_Closed;

        _snapshotService.SnapshotChanged += SnapshotService_SnapshotChanged;

        TryConfigureAppWindow();
        ApplySnapshot(_snapshotService.GetSnapshot());
        ApplyAdaptiveLayout();
        _rotationTimer.Start();
    }

    public void ApplyAppearance(string mode, string? customBackgroundPath)
    {
        _backdropMode = NormalizeMode(mode);
        _customBackgroundPath = string.IsNullOrWhiteSpace(customBackgroundPath) ? null : customBackgroundPath;

        if (_backdropMode == "TextOnly")
        {
            SystemBackdrop = null;
            RootCard.Background = new SolidColorBrush(Colors.Transparent);
            RootCard.BorderBrush = new SolidColorBrush(Colors.Transparent);
            RootCard.BorderThickness = new Thickness(0);
            RootCard.CornerRadius = new CornerRadius(0);
            SetIconVisibility(Visibility.Collapsed);
            return;
        }

        SetIconVisibility(Visibility.Visible);
        RootCard.BorderThickness = new Thickness(1);
        RootCard.CornerRadius = new CornerRadius(16);
            RootCard.BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(0x40, 0xFF, 0xFF, 0xFF));

        if (_backdropMode == "Custom")
        {
            SystemBackdrop = null;
            RootCard.Background = BuildCustomBackgroundBrush(_customBackgroundPath);
            return;
        }

        RootCard.Background = new SolidColorBrush(ColorHelper.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
        try
        {
            SystemBackdrop = _backdropMode == "Acrylic"
                ? new DesktopAcrylicBackdrop()
                : new MicaBackdrop();
        }
        catch
        {
            // Fallback to tint when backdrop is unavailable.
        }
    }

    private static string NormalizeMode(string? mode)
    {
        if (string.Equals(mode, "Acrylic", StringComparison.OrdinalIgnoreCase))
        {
            return "Acrylic";
        }

        if (string.Equals(mode, "Custom", StringComparison.OrdinalIgnoreCase))
        {
            return "Custom";
        }

        if (string.Equals(mode, "TextOnly", StringComparison.OrdinalIgnoreCase))
        {
            return "TextOnly";
        }

        return "Mica";
    }

    private static Brush BuildCustomBackgroundBrush(string? customBackgroundPath)
    {
        if (!string.IsNullOrWhiteSpace(customBackgroundPath) && File.Exists(customBackgroundPath))
        {
            return new ImageBrush
            {
                ImageSource = new BitmapImage(new Uri(customBackgroundPath)),
                Stretch = Stretch.UniformToFill,
                Opacity = 0.95
            };
        }

        return new SolidColorBrush(ColorHelper.FromArgb(0x66, 0xFF, 0xFF, 0xFF));
    }

    private void SetIconVisibility(Visibility visibility)
    {
        HeaderStatusDot.Visibility = visibility;
        StatusIcon.Visibility = visibility;
        IpIcon.Visibility = visibility;
        RuntimeIcon.Visibility = visibility;
        UsageIcon.Visibility = visibility;
    }

    private void DesktopWidgetWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        try
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            Win32WindowStyler.ApplyWidgetWindowStyle(hwnd);
            ApplyAppearance(_backdropMode, _customBackgroundPath);
            EnsureBottomMost();
        }
        catch
        {
            // Ignore style errors on unsupported environments.
        }
    }

    private void DesktopWidgetWindow_Closed(object sender, WindowEventArgs args)
    {
        _snapshotService.SnapshotChanged -= SnapshotService_SnapshotChanged;
        if (_appWindow is not null)
        {
            _appWindow.Changed -= AppWindow_Changed;
        }
        _rotationTimer.Stop();
    }

    private void TryConfigureAppWindow()
    {
        try
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            _appWindow = AppWindow.GetFromWindowId(windowId);

            if (_appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.SetBorderAndTitleBar(true, false);
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = false;
                presenter.IsResizable = true;
                presenter.IsAlwaysOnTop = false;
            }

            _appWindow.Changed += AppWindow_Changed;

            var saved = DesktopWidgetPreferenceStore.LoadBounds();
            if (saved is { } bounds)
            {
                ApplyWindowSize(bounds.Width, bounds.Height);
                _appWindow.Move(new PointInt32(bounds.X, bounds.Y));
            }
            else
            {
                ApplyWindowSize(DefaultWidth, DefaultHeight);
            }

            EnsureBottomMost();
        }
        catch
        {
            // Ignore app window config failures.
        }
    }

    private void SnapshotService_SnapshotChanged(object? sender, EventArgs e)
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            ApplySnapshot(_snapshotService.GetSnapshot());
        });
    }

    private void ApplySnapshot(IReadOnlyList<ServerSummary> servers)
    {
        _servers.Clear();
        _servers.AddRange(servers);

        if (_servers.Count == 0)
        {
            _currentIndex = 0;
            ShowEmptyState();
            return;
        }

        if (_currentIndex >= _servers.Count)
        {
            _currentIndex = 0;
        }

        ShowServer(_servers[_currentIndex], animate: false);
    }

    private void RotateNext()
    {
        if (_servers.Count == 0)
        {
            return;
        }

        _currentIndex = (_currentIndex + 1) % _servers.Count;
        ShowServer(_servers[_currentIndex], animate: true);
    }

    private void ShowServer(ServerSummary server, bool animate)
    {
        ServerNameText.Text = string.IsNullOrWhiteSpace(server.Name) ? "Unknown" : server.Name;
        StatusText.Text = $"状态：{(server.IsOnline ? "在线" : "离线")}";
        IpText.Text = $"IP：{(string.IsNullOrWhiteSpace(server.Ip) ? "--" : server.Ip)}";
        RuntimeText.Text = $"运行时间：{(string.IsNullOrWhiteSpace(server.Uptime) ? "--" : server.Uptime)}";
        UsageText.Text = $"CPU {server.CpuText} / 内存 {server.MemoryText} / 磁盘 {server.DiskText}";

        if (animate)
        {
            PlayPushUpAnimation();
        }
    }

    private void ShowEmptyState()
    {
        ServerNameText.Text = "等待服务器列表同步";
        StatusText.Text = "状态：请先刷新服务器列表";
        IpText.Text = "IP：--";
        RuntimeText.Text = "运行时间：--";
        UsageText.Text = "CPU -- / 内存 -- / 磁盘 --";
    }

    private void PlayPushUpAnimation()
    {
        ContentTranslateTransform.Y = 16;
        ContentHost.Opacity = 0;

        var storyboard = new Storyboard();

        var yAnimation = new DoubleAnimation
        {
            From = 16,
            To = 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(260)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        Storyboard.SetTarget(yAnimation, ContentTranslateTransform);
        Storyboard.SetTargetProperty(yAnimation, "Y");

        var opacityAnimation = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = new Duration(TimeSpan.FromMilliseconds(220)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        Storyboard.SetTarget(opacityAnimation, ContentHost);
        Storyboard.SetTargetProperty(opacityAnimation, "Opacity");

        storyboard.Children.Add(yAnimation);
        storyboard.Children.Add(opacityAnimation);
        storyboard.Begin();
    }

    private MenuFlyout BuildContextMenu()
    {
        var menu = new MenuFlyout();

        var enlargeItem = new MenuFlyoutItem { Text = "放大" };
        enlargeItem.Click += (_, _) => ResizeBy(ResizeStepWidth, ResizeStepHeight);
        menu.Items.Add(enlargeItem);

        var shrinkItem = new MenuFlyoutItem { Text = "缩小" };
        shrinkItem.Click += (_, _) => ResizeBy(-ResizeStepWidth, -ResizeStepHeight);
        menu.Items.Add(shrinkItem);

        var resetItem = new MenuFlyoutItem { Text = "重置大小" };
        resetItem.Click += (_, _) => ApplyWindowSize(DefaultWidth, DefaultHeight);
        menu.Items.Add(resetItem);

        menu.Items.Add(new MenuFlyoutSeparator());

        var exitItem = new MenuFlyoutItem { Text = "退出桌面组件" };
        exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(exitItem);
        return menu;
    }

    private void RootCard_OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(RootCard);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (!GetCursorPos(out _dragStartCursor) || _appWindow is null)
        {
            return;
        }

        _isDragging = true;
        _dragPointerId = e.Pointer.PointerId;
        _dragStartWindowPosition = _appWindow.Position;
        RootCard.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void RootCard_OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging || e.Pointer.PointerId != _dragPointerId || _appWindow is null)
        {
            return;
        }

        if (!GetCursorPos(out var currentCursor))
        {
            return;
        }

        var dx = currentCursor.X - _dragStartCursor.X;
        var dy = currentCursor.Y - _dragStartCursor.Y;
        _appWindow.Move(new PointInt32(_dragStartWindowPosition.X + dx, _dragStartWindowPosition.Y + dy));
        e.Handled = true;
    }

    private void RootCard_OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging)
        {
            return;
        }

        _isDragging = false;
        RootCard.ReleasePointerCaptures();
        EnsureBottomMost();
        e.Handled = true;
    }

    private void RootCard_OnRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        var point = e.GetPosition(RootCard);
        _menu.ShowAt(RootCard, new FlyoutShowOptions { Position = new Point(point.X, point.Y) });
    }

    private void RootCard_OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        App.MainAppWindow?.Activate();
    }

    private void RootCard_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyAdaptiveLayout();
    }

    private void RootCard_OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var delta = e.GetCurrentPoint(RootCard).Properties.MouseWheelDelta;
        if (delta == 0)
        {
            return;
        }

        ResizeBy(delta > 0 ? ResizeStepWidth : -ResizeStepWidth, delta > 0 ? ResizeStepHeight : -ResizeStepHeight);
    }

    private void ResizeBy(int widthDelta, int heightDelta)
    {
        ApplyWindowSize(_currentWidth + widthDelta, _currentHeight + heightDelta);
    }

    private void ApplyWindowSize(int width, int height)
    {
        _currentWidth = Math.Clamp(width, MinWidth, MaxWidth);
        _currentHeight = Math.Clamp(height, MinHeight, MaxHeight);

        try
        {
            _appWindow?.Resize(new SizeInt32(_currentWidth, _currentHeight));
        }
        catch
        {
            // ignore
        }
    }

    private void EnsureBottomMost()
    {
        try
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            Win32WindowStyler.SendToBottom(hwnd);
        }
        catch
        {
            // ignore
        }
    }

    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!args.DidPositionChange && !args.DidSizeChange)
        {
            return;
        }

        try
        {
            var p = sender.Position;
            var s = sender.Size;
            DesktopWidgetPreferenceStore.SaveBounds(p.X, p.Y, s.Width, s.Height);
        }
        catch
        {
            // ignore
        }
    }

    private void ApplyAdaptiveLayout()
    {
        var width = RootCard.ActualWidth;
        if (width <= 0)
        {
            return;
        }

        var compact = width < 420;
        ServerNameText.FontSize = compact ? 17 : 20;
        StatusText.FontSize = compact ? 14 : 16;
        IpText.FontSize = compact ? 13 : 15;
        RuntimeText.FontSize = compact ? 13 : 15;
        UsageText.FontSize = compact ? 13 : 15;
        RootCard.Padding = compact ? new Thickness(12) : new Thickness(16);
        InfoGrid.RowSpacing = compact ? 6 : 8;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }
}
