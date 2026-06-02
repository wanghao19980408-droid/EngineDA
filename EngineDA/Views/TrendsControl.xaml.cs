using EngineDA.Models;
using EngineDA.ViewModels;
using ScottPlot;
using ScottPlot.Plottables;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace EngineDA.Views
{
    public partial class TrendsControl : UserControl
    {
        private DashboardViewModel? _dashboardVM;
        private readonly Dictionary<int, ActiveChannel> _activeChannels = new();

        private ScottPlot.WPF.WpfPlot[] _plots;
        private Crosshair[] _crosshairs;
        private Annotation[] _tooltipAnnotations;

        private DispatcherTimer _uiTimer;
        private bool _isAutoY = true;
        private bool _isFrozen = false;

        private const double ViewWindowSeconds = 15.0;
        private int _renderTickCount = 0;
        private long _lastMouseMoveRender = 0;

        public TrendsControl()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (Application.Current.MainWindow.DataContext is DashboardViewModel vm)
            {
                _dashboardVM = vm;

                var sortedSensors = _dashboardVM.Sensors.OrderBy(s => s.Channel).ToList();
                CmbSensors.ItemsSource = sortedSensors;
                if (sortedSensors.Count > 0) CmbSensors.SelectedIndex = 0;
            }

            _plots = new[] { WpfPlot1, WpfPlot2, WpfPlot3, WpfPlot4 };
            _crosshairs = new Crosshair[4];
            _tooltipAnnotations = new Annotation[4];

            SetupPlots();

            _uiTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(50) };
            _uiTimer.Tick += UiTimer_Tick;
            _uiTimer.Start();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _uiTimer?.Stop();
        }

        private void SetupPlots()
        {
            for (int i = 0; i < _plots.Length; i++)
            {
                var plot = _plots[i];
                plot.Plot.FigureBackground.Color = ScottPlot.Color.FromHex("#1E1E24");
                plot.Plot.DataBackground.Color = ScottPlot.Color.FromHex("#1E1E24");
                plot.Plot.Grid.MajorLineColor = ScottPlot.Color.FromHex("#33333C");

                plot.Plot.Axes.Bottom.TickLabelStyle.FontName = "微软雅黑";
                plot.Plot.Axes.Bottom.TickLabelStyle.ForeColor = ScottPlot.Colors.LightGray;
                plot.Plot.Axes.Left.TickLabelStyle.FontName = "微软雅黑";
                plot.Plot.Axes.Left.TickLabelStyle.ForeColor = ScottPlot.Colors.LightGray;

                var timeTickGen = new ScottPlot.TickGenerators.NumericAutomatic();
                timeTickGen.LabelFormatter = (double x) =>
                {
                    try { if (x > 0) return DateTime.FromOADate(x).ToString("HH:mm:ss"); } catch { }
                    return "";
                };
                plot.Plot.Axes.Bottom.TickGenerator = timeTickGen;

                _crosshairs[i] = plot.Plot.Add.Crosshair(0, 0);
                _crosshairs[i].LineColor = ScottPlot.Colors.Yellow;
                _crosshairs[i].IsVisible = false;

                _tooltipAnnotations[i] = plot.Plot.Add.Annotation("");
                _tooltipAnnotations[i].LabelFontSize = 14;
                _tooltipAnnotations[i].LabelFontName = "微软雅黑";
                _tooltipAnnotations[i].LabelBackgroundColor = ScottPlot.Color.FromHex("#D9141414");
                _tooltipAnnotations[i].LabelFontColor = ScottPlot.Colors.White;
                _tooltipAnnotations[i].LabelBorderColor = ScottPlot.Colors.Yellow;
                _tooltipAnnotations[i].IsVisible = false;

                int plotIndex = i; // 闭包捕获
                plot.MouseMove += (s, e) => Plot_MouseMove(s, e, plotIndex);
                plot.MouseLeave += (s, e) => { _crosshairs[plotIndex].IsVisible = false; _tooltipAnnotations[plotIndex].IsVisible = false; plot.Refresh(); };
                plot.MouseUp += (s, e) => { if (_isFrozen && _isAutoY) AutoFitY(true); };
                plot.MouseWheel += (s, e) => { if (_isFrozen && _isAutoY) AutoFitY(true); };
            }
        }

        private void UiTimer_Tick(object? sender, EventArgs e)
        {
            if (_isFrozen || _dashboardVM == null || _activeChannels.Count == 0) return;

            _renderTickCount++;
            double currentNowOA = DateTime.Now.ToOADate();
            double windowDays = TimeSpan.FromSeconds(ViewWindowSeconds).TotalDays;
            double cutoffTime = currentNowOA - windowDays - TimeSpan.FromSeconds(5).TotalDays;

            foreach (var kvp in _activeChannels)
            {
                var ch = kvp.Value;
                var sensor = _dashboardVM.Sensors.FirstOrDefault(s => s.Channel == ch.Config.Channel);
                if (sensor != null)
                {
                    double val = sensor.Value;
                    ch.Logger.Add(currentNowOA, val);
                    ch.History.Add((currentNowOA, val));

                    if (ch.History.Count > 2000)
                    {
                        int removeIdx = ch.History.FindIndex(p => p.TimeOA >= cutoffTime);
                        if (removeIdx > 0) ch.History.RemoveRange(0, removeIdx);
                    }

                    if (val > ch.PeakMax) ch.PeakMax = val;
                    if (val < ch.PeakMin) ch.PeakMin = val;

                    if (_renderTickCount % 5 == 0)
                    {
                        ch.UpdateLabelUI(val, sensor.RawVoltage);
                    }
                }
            }

            // 更新所有图表的时间轴
            foreach (var plot in _plots)
            {
                plot.Plot.Axes.SetLimitsX(currentNowOA - windowDays, currentNowOA);
            }

            if (_isAutoY && _renderTickCount % 10 == 0)
            {
                AutoFitY(false);
            }

            foreach (var plot in _plots)
            {
                plot.Refresh();
            }
        }

        private void AutoFitY(bool force)
        {
            foreach (var plot in _plots)
            {
                var limits = plot.Plot.Axes.GetLimits();
                double minX = limits.Left;
                double maxX = limits.Right;

                double minY = double.MaxValue;
                double maxY = double.MinValue;
                bool found = false;

                // 仅自适应挂载在当前 plot 上的频道
                var channelsOnPlot = _activeChannels.Values.Where(c => c.ParentPlot == plot);

                foreach (var ch in channelsOnPlot)
                {
                    for (int i = ch.History.Count - 1; i >= 0; i--)
                    {
                        var p = ch.History[i];
                        if (p.TimeOA < minX) break;

                        if (p.TimeOA <= maxX)
                        {
                            if (p.Value < minY) minY = p.Value;
                            if (p.Value > maxY) maxY = p.Value;
                            found = true;
                        }
                    }
                }

                if (found)
                {
                    double padding = (maxY - minY) * 0.1;
                    if (padding == 0) padding = 1.0;

                    double currentYMin = limits.Bottom;
                    double currentYMax = limits.Top;

                    bool needsUpdate = force ||
                                       (minY - padding < currentYMin) ||
                                       (maxY + padding > currentYMax) ||
                                       (currentYMax - currentYMin > (maxY - minY + padding * 2) * 1.5);

                    if (needsUpdate)
                    {
                        plot.Plot.Axes.SetLimitsY(minY - padding, maxY + padding);
                    }
                }
            }
        }

        private void Plot_MouseMove(object sender, MouseEventArgs e, int plotIndex)
        {
            if (_activeChannels.Count == 0) return;

            var currentPlot = _plots[plotIndex];
            var currentCrosshair = _crosshairs[plotIndex];
            var currentAnnotation = _tooltipAnnotations[plotIndex];

            Point position = e.GetPosition(currentPlot);
            var dpi = VisualTreeHelper.GetDpi(this);
            float scaledX = (float)(position.X * dpi.DpiScaleX);
            float scaledY = (float)(position.Y * dpi.DpiScaleY);

            Pixel mousePixel = new Pixel(scaledX, scaledY);
            Coordinates mouseLocation = currentPlot.Plot.GetCoordinates(mousePixel);

            double mouseTimeOA = mouseLocation.X;

            currentCrosshair.Position = new Coordinates(mouseLocation.X, mouseLocation.Y);
            currentCrosshair.IsVisible = true;

            bool hasData = false;
            double closestTimeOA = 0;
            string tooltipText = "";

            var channelsOnPlot = _activeChannels.Values.Where(c => c.ParentPlot == currentPlot);

            foreach (var ch in channelsOnPlot)
            {
                if (ch.History.Count == 0) continue;

                int closestIdx = BinarySearchClosest(ch.History, mouseTimeOA);
                if (closestIdx >= 0 && closestIdx < ch.History.Count)
                {
                    var closest = ch.History[closestIdx];
                    double timeDiffDays = Math.Abs(closest.TimeOA - mouseTimeOA);

                    if (timeDiffDays < TimeSpan.FromSeconds(0.5).TotalDays)
                    {
                        closestTimeOA = closest.TimeOA;
                        tooltipText += $"■ {ch.Config.Name}: {closest.Value:F3} {ch.Config.Unit}\n";
                        hasData = true;
                    }
                }
            }

            if (hasData)
            {
                DateTime ptTime = DateTime.FromOADate(closestTimeOA);
                currentAnnotation.Text = $"时间: {ptTime:HH:mm:ss.fff}\n---------------------\n{tooltipText.Trim()}";

                currentAnnotation.OffsetX = (float)(position.X > currentPlot.ActualWidth - 150 ? position.X - 160 : position.X + 15);
                currentAnnotation.OffsetY = (float)(position.Y + 15);
                currentAnnotation.IsVisible = true;

                currentCrosshair.Position = new Coordinates(closestTimeOA, mouseLocation.Y);
                currentCrosshair.IsVisible = true;
            }
            else
            {
                currentAnnotation.IsVisible = false;
                currentCrosshair.Position = new Coordinates(mouseLocation.X, mouseLocation.Y);
                currentCrosshair.IsVisible = true;
            }

            if (_isFrozen)
            {
                long now = Environment.TickCount64;
                if (now - _lastMouseMoveRender > 30)
                {
                    currentPlot.Refresh();
                    _lastMouseMoveRender = now;
                }
            }
        }

        private int BinarySearchClosest(List<(double TimeOA, double Value)> list, double targetOA)
        {
            int left = 0;
            int right = list.Count - 1;
            while (left <= right)
            {
                int mid = left + (right - left) / 2;
                if (list[mid].TimeOA == targetOA) return mid;
                if (list[mid].TimeOA < targetOA) left = mid + 1;
                else right = mid - 1;
            }

            if (left >= list.Count) return list.Count - 1;
            if (right < 0) return 0;

            return Math.Abs(list[left].TimeOA - targetOA) < Math.Abs(list[right].TimeOA - targetOA) ? left : right;
        }

        #region 按钮事件

        private void BtnChangeColor_Click(object sender, RoutedEventArgs e)
        {
            var colors = new[] {
                System.Windows.Media.Colors.Lime,
                System.Windows.Media.Colors.Cyan,
                System.Windows.Media.Colors.Yellow,
                System.Windows.Media.Colors.Magenta,
                System.Windows.Media.Colors.Orange,
                System.Windows.Media.Colors.LightPink
            };
            var current = ((SolidColorBrush)BtnChangeColor.Background).Color;
            int idx = Array.IndexOf(colors, current);
            BtnChangeColor.Background = new SolidColorBrush(colors[(idx + 1) % colors.Length]);
        }

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            if (CmbSensors.SelectedItem is SensorDisplay selectedSensor)
            {
                if (_activeChannels.ContainsKey(selectedSensor.Channel)) return;

                int targetPlotIndex = CmbTargetPlot.SelectedIndex;
                if (targetPlotIndex < 0 || targetPlotIndex >= _plots.Length) targetPlotIndex = 0;

                var targetPlot = _plots[targetPlotIndex];

                var brush = (SolidColorBrush)BtnChangeColor.Background;
                var sColor = new ScottPlot.Color(brush.Color.R, brush.Color.G, brush.Color.B);

                var logger = targetPlot.Plot.Add.DataLogger();
                logger.Color = sColor;
                logger.LineStyle.Width = 1.5f;
                logger.ManageAxisLimits = false;

                var channel = new ActiveChannel(selectedSensor, logger, brush, targetPlot, targetPlotIndex + 1, RemoveSingleChannel);
                _activeChannels.Add(selectedSensor.Channel, channel);

                InfoPanel.Children.Add(channel.UIPanel);
            }
        }

        private void RemoveSingleChannel(ActiveChannel channel)
        {
            if (channel == null) return;

            channel.ParentPlot.Plot.Remove(channel.Logger);
            InfoPanel.Children.Remove(channel.UIPanel);
            _activeChannels.Remove(channel.Config.Channel);
            channel.ParentPlot.Refresh();
        }

        private void BtnAutoY_Click(object sender, RoutedEventArgs e)
        {
            _isAutoY = !_isAutoY;
            BtnAutoY.Content = _isAutoY ? "✅ 自适应Y轴" : "❌ 固定Y轴";
            BtnAutoY.Background = new SolidColorBrush(_isAutoY ? System.Windows.Media.Colors.DarkOliveGreen : System.Windows.Media.Colors.DimGray);
            if (_isAutoY && _isFrozen)
            {
                AutoFitY(true);
                foreach (var plot in _plots) plot.Refresh();
            }
        }

        private void BtnFreeze_Click(object sender, RoutedEventArgs e)
        {
            _isFrozen = !_isFrozen;
            BtnFreeze.Content = _isFrozen ? "▶ 恢复滚动" : "⏸ 画面冻结";
            BtnFreeze.Background = new SolidColorBrush(_isFrozen ? System.Windows.Media.Colors.Green : System.Windows.Media.Colors.DarkOrange);
            BtnFreeze.Foreground = new SolidColorBrush(_isFrozen ? System.Windows.Media.Colors.White : System.Windows.Media.Colors.Black);
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            foreach (var ch in _activeChannels.Values)
            {
                ch.ParentPlot.Plot.Remove(ch.Logger);
                InfoPanel.Children.Remove(ch.UIPanel);
            }
            _activeChannels.Clear();
            foreach (var plot in _plots) plot.Refresh();
        }

        #endregion

        private class ActiveChannel
        {
            public SensorDisplay Config { get; }
            public DataLogger Logger { get; }
            public ScottPlot.WPF.WpfPlot ParentPlot { get; }
            public List<(double TimeOA, double Value)> History { get; } = new();
            public Border UIPanel { get; }
            private TextBlock InfoText { get; }
            private int PlotIndex { get; }

            public double PeakMax { get; set; } = double.MinValue;
            public double PeakMin { get; set; } = double.MaxValue;

            public ActiveChannel(SensorDisplay config, DataLogger logger, SolidColorBrush color, ScottPlot.WPF.WpfPlot parentPlot, int plotIndex, Action<ActiveChannel> onDelete)
            {
                Config = config;
                Logger = logger;
                ParentPlot = parentPlot;
                PlotIndex = plotIndex;

                UIPanel = new Border
                {
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(37, 37, 45)),
                    BorderBrush = color,
                    BorderThickness = new Thickness(3, 0, 0, 0),
                    Margin = new Thickness(0, 0, 0, 10),
                    Padding = new Thickness(10)
                };

                Grid layoutContainer = new Grid();
                layoutContainer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                layoutContainer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                Button btnDelete = new Button
                {
                    Content = "X",
                    Width = 24,
                    Height = 24,
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 50, 50)),
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand,
                    VerticalAlignment = System.Windows.VerticalAlignment.Top,
                    ToolTip = "删除此曲线"
                };
                btnDelete.MouseEnter += (s, e) => btnDelete.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 50, 50));
                btnDelete.MouseLeave += (s, e) => btnDelete.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 50, 50));
                btnDelete.Click += (s, e) => onDelete(this);

                Grid.SetColumn(btnDelete, 1);
                layoutContainer.Children.Add(btnDelete);

                InfoText = new TextBlock
                {
                    Foreground = Brushes.White,
                    FontFamily = new FontFamily("Microsoft YaHei, Segoe UI"),
                    FontSize = 13,
                    LineHeight = 20,
                    Text = $"[图表{PlotIndex}] [{config.Channel}] {config.Name}\n 当前: --\n 峰值: --\n 谷底: --\n 原始: -- V"
                };

                Grid.SetColumn(InfoText, 0);
                layoutContainer.Children.Add(InfoText);

                UIPanel.Child = layoutContainer;
            }

            public void UpdateLabelUI(double physVal, double rawVal)
            {
                InfoText.Text = $"[图表{PlotIndex}] [{Config.Channel}] {Config.Name}\n 当前: {physVal:F3} {Config.Unit}\n 峰值: {PeakMax:F3}\n 谷底: {PeakMin:F3}\n 原始: {rawVal:F3} V";
            }
        }
    }
}