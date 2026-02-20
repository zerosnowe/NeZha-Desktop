using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using NeZha_Desktop.Models;
using NeZha_Desktop.ViewModels;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Windows.UI;

namespace NeZha_Desktop.Views
{
    public sealed partial class ServerDetailPage : Page
    {
        private DispatcherTimer? _pollingTimer;
        private bool _isChartDrawQueued;
        private bool _isMetricChartDrawQueued;
        private readonly List<RenderedSeries> _renderedSeries = new();
        private readonly List<UIElement> _hoverOverlayElements = new();
        private Windows.Foundation.Point? _hoverPosition;
        private readonly List<double> _cpuHistory = new();
        private readonly List<double> _memoryHistory = new();
        private readonly List<double> _diskHistory = new();
        private readonly List<double> _uploadHistory = new();
        private readonly List<double> _downloadHistory = new();
        private readonly List<double> _loadHistory = new();
        private readonly List<double> _statusHistory = new();

        public ServerDetailViewModel ViewModel { get; }

        public ServerDetailPage()
        {
            ViewModel = App.HostContainer.Services.GetRequiredService<ServerDetailViewModel>();
            InitializeComponent();
            DataContext = ViewModel;

            Loaded += ServerDetailPage_Loaded;
            Unloaded += ServerDetailPage_Unloaded;
            ViewModel.NetworkMonitors.CollectionChanged += NetworkMonitors_CollectionChanged;
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is ulong id)
            {
                await ViewModel.LoadAsync(id);
                SyncModeSelection();
                EnsurePolling();
                QueueDrawNetworkChart();
                QueueDrawMetricCards();
            }
        }

        private void ServerDetailPage_Loaded(object sender, RoutedEventArgs e)
        {
            QueueDrawNetworkChart();
            QueueDrawMetricCards();
        }

        private void EnsurePolling()
        {
            if (_pollingTimer != null)
            {
                _pollingTimer.Start();
                return;
            }

            _pollingTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };

            _pollingTimer.Tick += async (_, _) =>
            {
                await ViewModel.RefreshAsync(true);
                QueueDrawNetworkChart();
                QueueDrawMetricCards();
            };
            _pollingTimer.Start();
        }

        private void ServerDetailPage_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_pollingTimer != null)
            {
                _pollingTimer.Stop();
            }


            ViewModel.NetworkMonitors.CollectionChanged -= NetworkMonitors_CollectionChanged;
            ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        }

        private void DetailModeNav_OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            var tag = args.SelectedItemContainer?.Tag?.ToString();
            if (tag == "network")
            {
                ViewModel.ShowNetworkTabCommand.Execute(null);
                QueueDrawNetworkChart();
                return;
            }

            ViewModel.ShowDetailTabCommand.Execute(null);
            QueueDrawMetricCards();
        }

        private void SyncModeSelection()
        {
            DetailModeNav.SelectedItem = ViewModel.IsNetworkTab ? NetworkModeNavItem : DetailModeNavItem;
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ServerDetailViewModel.IsNetworkTab))
            {
                QueueDrawNetworkChart();
            }
            else if (e.PropertyName == nameof(ServerDetailViewModel.Detail))
            {
                AppendMetricHistory();
                QueueDrawMetricCards();
            }
        }


        private void NetworkMonitors_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            QueueDrawNetworkChart();
        }

        private void NetworkChartCanvas_OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            QueueDrawNetworkChart();
        }

        private void MetricCardCanvas_OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            QueueDrawMetricCards();
        }

        private void NetworkChartCanvas_OnPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!ViewModel.IsNetworkTab || NetworkChartCanvas == null)
            {
                return;
            }

            var point = e.GetCurrentPoint(NetworkChartCanvas).Position;
            _hoverPosition = point;
            RenderHoverOverlay(point);
        }

        private void NetworkChartCanvas_OnPointerExited(object sender, PointerRoutedEventArgs e)
        {
            _hoverPosition = null;
            ClearHoverOverlay();
        }

        private void QueueDrawNetworkChart()
        {
            if (_isChartDrawQueued)
            {
                return;
            }

            _isChartDrawQueued = true;
            DispatcherQueue.TryEnqueue(() =>
            {
                _isChartDrawQueued = false;
                DrawNetworkChart();
            });
        }

        private void QueueDrawMetricCards()
        {
            if (_isMetricChartDrawQueued)
            {
                return;
            }

            _isMetricChartDrawQueued = true;
            DispatcherQueue.TryEnqueue(() =>
            {
                _isMetricChartDrawQueued = false;
                DrawMetricCards();
            });
        }

        private void DrawMetricCards()
        {
            DrawSparkline(CpuCardCanvas, _cpuHistory, Colors.DodgerBlue, 100);
            DrawSparkline(MemoryCardCanvas, _memoryHistory, Colors.MediumPurple, 100);
            DrawSparkline(DiskCardCanvas, _diskHistory, Colors.MediumSeaGreen, 100);
            DrawSparkline(SystemCardCanvas, _statusHistory, Colors.DeepSkyBlue, 1);
            DrawSparkline(NetworkCardCanvas, _uploadHistory, Colors.DeepSkyBlue, 1, _downloadHistory, Colors.MediumPurple);

            var runtimeAxis = Math.Max(1d, _loadHistory.DefaultIfEmpty(0).Max() * 1.3);
            DrawSparkline(RuntimeCardCanvas, _loadHistory, Colors.HotPink, runtimeAxis);
        }

        private static void DrawSparkline(
            Canvas? canvas,
            IReadOnlyList<double> primarySeries,
            Color primaryColor,
            double axisMax,
            IReadOnlyList<double>? secondarySeries = null,
            Color? secondaryColor = null)
        {
            if (canvas == null)
            {
                return;
            }

            var width = canvas.ActualWidth;
            var height = canvas.ActualHeight;
            if (width < 40 || height < 24)
            {
                return;
            }

            canvas.Children.Clear();

            var gridLine = new Line
            {
                X1 = 0,
                X2 = width,
                Y1 = height * 0.5,
                Y2 = height * 0.5,
                Stroke = new SolidColorBrush(Color.FromArgb(45, 180, 180, 180)),
                StrokeThickness = 1,
            };
            canvas.Children.Add(gridLine);

            DrawAreaLineSeries(canvas, primarySeries, primaryColor, axisMax, height, width);

            if (secondarySeries != null && secondarySeries.Count > 1)
            {
                DrawLineSeries(canvas, secondarySeries, secondaryColor ?? Colors.Orange, axisMax, height, width, 1.3, 0.95);
            }
        }

        private static void DrawAreaLineSeries(Canvas canvas, IReadOnlyList<double> series, Color color, double axisMax, double height, double width)
        {
            if (series.Count == 0)
            {
                return;
            }

            var polygon = new Polygon
            {
                Fill = new SolidColorBrush(Color.FromArgb(65, color.R, color.G, color.B)),
                StrokeThickness = 0,
            };

            polygon.Points.Add(new Windows.Foundation.Point(0, height));
            if (series.Count == 1)
            {
                var y = ValueToY(series[0], axisMax, height);
                polygon.Points.Add(new Windows.Foundation.Point(0, y));
                polygon.Points.Add(new Windows.Foundation.Point(width, y));
            }
            else
            {
                for (var i = 0; i < series.Count; i++)
                {
                    var x = i / (double)(series.Count - 1) * width;
                    var y = ValueToY(series[i], axisMax, height);
                    polygon.Points.Add(new Windows.Foundation.Point(x, y));
                }
            }
            polygon.Points.Add(new Windows.Foundation.Point(width, height));
            canvas.Children.Add(polygon);

            DrawLineSeries(canvas, series, color, axisMax, height, width, 1.4, 0.95);
        }

        private static void DrawLineSeries(Canvas canvas, IReadOnlyList<double> series, Color color, double axisMax, double height, double width, double thickness, double opacity)
        {
            if (series.Count == 0)
            {
                return;
            }

            var polyline = new Polyline
            {
                Stroke = new SolidColorBrush(color),
                StrokeThickness = thickness,
                Opacity = opacity,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            };

            if (series.Count == 1)
            {
                var y = ValueToY(series[0], axisMax, height);
                polyline.Points.Add(new Windows.Foundation.Point(0, y));
                polyline.Points.Add(new Windows.Foundation.Point(width, y));
            }
            else
            {
                for (var i = 0; i < series.Count; i++)
                {
                    var x = i / (double)(series.Count - 1) * width;
                    var y = ValueToY(series[i], axisMax, height);
                    polyline.Points.Add(new Windows.Foundation.Point(x, y));
                }
            }

            canvas.Children.Add(polyline);
        }

        private static double ValueToY(double value, double axisMax, double height)
        {
            if (axisMax <= 0)
            {
                axisMax = 1;
            }

            var ratio = Math.Clamp(value / axisMax, 0, 1);
            return (1 - ratio) * (height - 2) + 1;
        }

        private void AppendMetricHistory()
        {
            AddHistoryPoint(_cpuHistory, ViewModel.Detail.CpuPercent);
            AddHistoryPoint(_memoryHistory, ViewModel.Detail.MemoryPercent);
            AddHistoryPoint(_diskHistory, ViewModel.Detail.DiskPercent);
            AddHistoryPoint(_uploadHistory, ViewModel.Detail.UploadSpeedBytes);
            AddHistoryPoint(_downloadHistory, ViewModel.Detail.DownloadSpeedBytes);
            AddHistoryPoint(_loadHistory, ViewModel.Detail.Load1Value);
            AddHistoryPoint(_statusHistory, IsOnline(ViewModel.Detail.Status) ? 1 : 0);
        }

        private static bool IsOnline(string status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return false;
            }

            var s = status.ToLowerInvariant();
            return s.Contains("online") || s.Contains("up") || s.Contains("运行") || s.Contains("在线");
        }

        private static void AddHistoryPoint(List<double> history, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                value = 0;
            }

            history.Add(Math.Max(0, value));
            const int maxPoints = 60;
            if (history.Count > maxPoints)
            {
                history.RemoveRange(0, history.Count - maxPoints);
            }
        }

        private void DrawNetworkChart()
        {
            if (!ViewModel.IsNetworkTab)
            {
                return;
            }

            if (NetworkChartCanvas == null)
            {
                return;
            }

            var width = NetworkChartCanvas.ActualWidth;
            var height = NetworkChartCanvas.ActualHeight;
            if (width <= 80 || height <= 60)
            {
                return;
            }

            NetworkChartCanvas.Children.Clear();
            _renderedSeries.Clear();
            _hoverOverlayElements.Clear();

            var series = ViewModel.NetworkMonitors
                .Where(x => x.Timestamps.Count > 1 && x.DelaySeries.Count > 1)
                .ToList();

            if (series.Count == 0)
            {
                var emptyText = new TextBlock
                {
                    Text = "暂无可绘制的网络监控数据",
                    Foreground = new SolidColorBrush(Colors.Gray),
                    Opacity = 0.8,
                };
                Canvas.SetLeft(emptyText, 12);
                Canvas.SetTop(emptyText, 12);
                NetworkChartCanvas.Children.Add(emptyText);
                return;
            }

            const double leftPadding = 52;
            const double topPadding = 8;
            const double rightPadding = 10;
            const double bottomPadding = 24;

            var chartWidth = Math.Max(10, width - leftPadding - rightPadding);
            var chartHeight = Math.Max(10, height - topPadding - bottomPadding);

            var allTimestamps = series.SelectMany(x => x.Timestamps).ToList();
            var minTs = allTimestamps.Min();
            var maxTs = allTimestamps.Max();
            if (minTs == maxTs)
            {
                maxTs += 60_000;
            }

            var maxDelayRaw = series.SelectMany(x => x.DelaySeries).DefaultIfEmpty(0).Max();
            var axisMax = CalculateAxisMax(maxDelayRaw);

            DrawHorizontalAxisGrid(leftPadding, topPadding, chartWidth, chartHeight, axisMax);
            DrawTimeTicks(leftPadding, topPadding, chartWidth, chartHeight, minTs, maxTs);

            foreach (var monitor in series)
            {
                var count = Math.Min(monitor.Timestamps.Count, monitor.DelaySeries.Count);
                if (count < 2)
                {
                    continue;
                }

                var renderedSeries = new RenderedSeries
                {
                    MonitorName = monitor.MonitorName,
                    Color = ParseColor(monitor.SeriesColorHex),
                };

                var polyline = new Polyline
                {
                    Stroke = new SolidColorBrush(renderedSeries.Color),
                    StrokeThickness = 1.2,
                    Opacity = 0.85,
                    StrokeLineJoin = PenLineJoin.Round,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                };

                for (var i = 0; i < count; i++)
                {
                    var x = leftPadding + ((monitor.Timestamps[i] - minTs) / (double)(maxTs - minTs)) * chartWidth;
                    var normalized = Math.Clamp(monitor.DelaySeries[i] / axisMax, 0, 1);
                    var y = topPadding + (1 - normalized) * chartHeight;
                    polyline.Points.Add(new Windows.Foundation.Point(x, y));
                    renderedSeries.Points.Add(new RenderedPoint(x, y, monitor.Timestamps[i], monitor.DelaySeries[i]));
                }

                NetworkChartCanvas.Children.Add(polyline);
                _renderedSeries.Add(renderedSeries);
            }

            if (_hoverPosition.HasValue)
            {
                RenderHoverOverlay(_hoverPosition.Value);
            }
        }

        private void RenderHoverOverlay(Windows.Foundation.Point pointerPosition)
        {
            if (NetworkChartCanvas == null || _renderedSeries.Count == 0)
            {
                return;
            }

            ClearHoverOverlay();

            var anchorPoint = FindAnchorPoint(pointerPosition.X);
            if (anchorPoint == null)
            {
                return;
            }

            var hoverItems = new List<HoverItem>();
            foreach (var series in _renderedSeries)
            {
                var nearest = FindNearestPointByTimestamp(series.Points, anchorPoint.Value.Timestamp);
                if (nearest == null)
                {
                    continue;
                }

                hoverItems.Add(new HoverItem(series.MonitorName, nearest.Value, series.Color));
            }

            if (hoverItems.Count == 0)
            {
                return;
            }

            foreach (var item in hoverItems)
            {
                var marker = new Ellipse
                {
                    Width = 8,
                    Height = 8,
                    Fill = new SolidColorBrush(item.Color),
                    Stroke = new SolidColorBrush(Colors.White),
                    StrokeThickness = 1.5,
                    IsHitTestVisible = false,
                };

                Canvas.SetLeft(marker, item.Point.X - 4);
                Canvas.SetTop(marker, item.Point.Y - 4);
                NetworkChartCanvas.Children.Add(marker);
                _hoverOverlayElements.Add(marker);
            }

            var orderedItems = hoverItems.OrderByDescending(x => x.Point.Delay).ToList();
            var tooltip = BuildHoverTooltip(anchorPoint.Value.Timestamp, orderedItems);
            NetworkChartCanvas.Children.Add(tooltip);
            _hoverOverlayElements.Add(tooltip);

            tooltip.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            var tooltipWidth = tooltip.DesiredSize.Width;
            var tooltipHeight = tooltip.DesiredSize.Height;

            var x = pointerPosition.X + 14;
            var y = pointerPosition.Y + 14;
            x = Math.Max(8, Math.Min(x, Math.Max(8, NetworkChartCanvas.ActualWidth - tooltipWidth - 8)));
            y = Math.Max(8, Math.Min(y, Math.Max(8, NetworkChartCanvas.ActualHeight - tooltipHeight - 8)));

            Canvas.SetLeft(tooltip, x);
            Canvas.SetTop(tooltip, y);
        }

        private Border BuildHoverTooltip(long timestamp, IReadOnlyList<HoverItem> orderedItems)
        {
            var stackPanel = new StackPanel
            {
                Spacing = 4,
            };

            stackPanel.Children.Add(new TextBlock
            {
                Text = DateTimeOffset.FromUnixTimeMilliseconds(timestamp).LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                HorizontalAlignment = HorizontalAlignment.Center,
                Opacity = 0.78,
            });

            foreach (var item in orderedItems)
            {
                var row = new Grid
                {
                    ColumnSpacing = 12,
                };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var name = new TextBlock
                {
                    Text = item.MonitorName,
                    MaxLines = 1,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Opacity = 0.95,
                };

                var value = new TextBlock
                {
                    Text = $"{item.Point.Delay:F2}ms",
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(item.Color),
                };

                Grid.SetColumn(name, 0);
                Grid.SetColumn(value, 1);
                row.Children.Add(name);
                row.Children.Add(value);

                stackPanel.Children.Add(row);
            }

            return new Border
            {
                Padding = new Thickness(12, 10, 12, 10),
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(Color.FromArgb(220, 245, 245, 245)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(100, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                IsHitTestVisible = false,
                Child = stackPanel,
            };
        }

        private RenderedPoint? FindAnchorPoint(double x)
        {
            RenderedPoint? nearest = null;
            var minDelta = double.MaxValue;

            foreach (var series in _renderedSeries)
            {
                foreach (var point in series.Points)
                {
                    var delta = Math.Abs(point.X - x);
                    if (delta < minDelta)
                    {
                        minDelta = delta;
                        nearest = point;
                    }
                }
            }

            return nearest;
        }

        private static RenderedPoint? FindNearestPointByTimestamp(IReadOnlyList<RenderedPoint> points, long timestamp)
        {
            if (points.Count == 0)
            {
                return null;
            }

            RenderedPoint nearest = points[0];
            var minDelta = Math.Abs(points[0].Timestamp - timestamp);

            for (var i = 1; i < points.Count; i++)
            {
                var delta = Math.Abs(points[i].Timestamp - timestamp);
                if (delta < minDelta)
                {
                    minDelta = delta;
                    nearest = points[i];
                }
            }

            return nearest;
        }

        private void ClearHoverOverlay()
        {
            if (NetworkChartCanvas == null || _hoverOverlayElements.Count == 0)
            {
                return;
            }

            foreach (var element in _hoverOverlayElements)
            {
                NetworkChartCanvas.Children.Remove(element);
            }

            _hoverOverlayElements.Clear();
        }

        private void DrawHorizontalAxisGrid(double left, double top, double width, double height, double axisMax)
        {
            var marks = new[] { 0d, axisMax * 0.25, axisMax * 0.5, axisMax };
            foreach (var mark in marks)
            {
                var y = top + (1 - (mark / axisMax)) * height;

                var line = new Line
                {
                    X1 = left,
                    X2 = left + width,
                    Y1 = y,
                    Y2 = y,
                    Stroke = new SolidColorBrush(Color.FromArgb(60, 160, 160, 160)),
                    StrokeThickness = 1,
                };
                NetworkChartCanvas.Children.Add(line);

                var label = new TextBlock
                {
                    Text = $"{mark:F0}ms",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Colors.Gray),
                };

                Canvas.SetLeft(label, 0);
                Canvas.SetTop(label, y - 9);
                NetworkChartCanvas.Children.Add(label);
            }
        }

        private void DrawTimeTicks(double left, double top, double width, double height, long minTs, long maxTs)
        {
            const int tickCount = 6;
            for (var i = 0; i <= tickCount; i++)
            {
                var ratio = i / (double)tickCount;
                var x = left + ratio * width;
                var ts = minTs + (long)((maxTs - minTs) * ratio);

                var tick = new Line
                {
                    X1 = x,
                    X2 = x,
                    Y1 = top + height,
                    Y2 = top + height + 4,
                    Stroke = new SolidColorBrush(Color.FromArgb(120, 160, 160, 160)),
                    StrokeThickness = 1,
                };
                NetworkChartCanvas.Children.Add(tick);

                var label = new TextBlock
                {
                    Text = DateTimeOffset.FromUnixTimeMilliseconds(ts).LocalDateTime.ToString("HH:mm", CultureInfo.InvariantCulture),
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Colors.Gray),
                };

                Canvas.SetLeft(label, x - 14);
                Canvas.SetTop(label, top + height + 4);
                NetworkChartCanvas.Children.Add(label);
            }
        }

        private static double CalculateAxisMax(double maxDelayRaw)
        {
            if (maxDelayRaw <= 450)
            {
                return 450;
            }

            if (maxDelayRaw <= 900)
            {
                return 900;
            }

            if (maxDelayRaw <= 1800)
            {
                return 1800;
            }

            if (maxDelayRaw <= 3600)
            {
                return 3600;
            }

            return Math.Ceiling(maxDelayRaw / 1000) * 1000;
        }

        private static Color ParseColor(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex) || !hex.StartsWith('#'))
            {
                return Colors.DeepSkyBlue;
            }

            var payload = hex[1..];
            if (payload.Length == 6 && uint.TryParse(payload, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            {
                return Color.FromArgb(255, (byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF));
            }

            if (payload.Length == 8 && uint.TryParse(payload, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var argb))
            {
                return Color.FromArgb((byte)((argb >> 24) & 0xFF), (byte)((argb >> 16) & 0xFF), (byte)((argb >> 8) & 0xFF), (byte)(argb & 0xFF));
            }

            return Colors.DeepSkyBlue;
        }

        private sealed class RenderedSeries
        {
            public string MonitorName { get; set; } = string.Empty;
            public Color Color { get; set; }
            public List<RenderedPoint> Points { get; } = new();
        }

        private readonly struct RenderedPoint
        {
            public RenderedPoint(double x, double y, long timestamp, double delay)
            {
                X = x;
                Y = y;
                Timestamp = timestamp;
                Delay = delay;
            }

            public double X { get; }
            public double Y { get; }
            public long Timestamp { get; }
            public double Delay { get; }
        }

        private readonly struct HoverItem
        {
            public HoverItem(string monitorName, RenderedPoint point, Color color)
            {
                MonitorName = monitorName;
                Point = point;
                Color = color;
            }

            public string MonitorName { get; }
            public RenderedPoint Point { get; }
            public Color Color { get; }
        }
    }
}

