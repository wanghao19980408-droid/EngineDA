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

        private Crosshair _crosshair;
        private Annotation _tooltipAnnotation;

        private DispatcherTimer _uiTimer;
        private bool _isAutoY = true;
        private bool _isFrozen = false;

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
                CmbSensors.ItemsSource = _dashboardVM.Sensors;
                if (_dashboardVM.Sensors.Count > 0) CmbSensors.SelectedIndex = 0;
            }

            SetupPlot();

            _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
            _uiTimer.Tick += UiTimer_Tick;
            _uiTimer.Start();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _uiTimer?.Stop();
        }

        private void SetupPlot()
        {
            WpfPlot1.Plot.FigureBackground.Color = ScottPlot.Color.FromHex("#1E1E24");
            WpfPlot1.Plot.DataBackground.Color = ScottPlot.Color.FromHex("#1E1E24");
            WpfPlot1.Plot.Grid.MajorLineColor = ScottPlot.Color.FromHex("#33333C");
            WpfPlot1.Plot.Axes.Bottom.TickLabelStyle.ForeColor = ScottPlot.Colors.LightGray;
            WpfPlot1.Plot.Axes.Left.TickLabelStyle.ForeColor = ScottPlot.Colors.LightGray;

            var timeTickGen = new ScottPlot.TickGenerators.NumericAutomatic();
            timeTickGen.LabelFormatter = (double x) =>
            {
                try { if (x > 0) return DateTime.FromOADate(x).ToString("HH:mm:ss"); } catch { }
                return "";
            };
            WpfPlot1.Plot.Axes.Bottom.TickGenerator = timeTickGen;

            // 明确使用 ScottPlot.Colors
            _crosshair = WpfPlot1.Plot.Add.Crosshair(0, 0);
            _crosshair.LineColor = ScottPlot.Colors.Yellow;
            _crosshair.IsVisible = false;

            _tooltipAnnotation = WpfPlot1.Plot.Add.Annotation("");
            _tooltipAnnotation.LabelFontSize = 14;
            _tooltipAnnotation.LabelBackgroundColor = ScottPlot.Color.FromHex("#D9141414");
            _tooltipAnnotation.LabelFontColor = ScottPlot.Colors.White;
            _tooltipAnnotation.LabelBorderColor = ScottPlot.Colors.Yellow;
            _tooltipAnnotation.IsVisible = false;

            WpfPlot1.MouseMove += WpfPlot1_MouseMove;
            WpfPlot1.MouseLeave += (s, e) => { _crosshair.IsVisible = false; _tooltipAnnotation.IsVisible = false; if (_isFrozen) WpfPlot1.Refresh(); };
            WpfPlot1.MouseUp += (s, e) => { if (_isFrozen && _isAutoY) AutoFitY(); };
            WpfPlot1.MouseWheel += (s, e) => { if (_isFrozen && _isAutoY) AutoFitY(); };
        }

        private void UiTimer_Tick(object? sender, EventArgs e)
        {
            if (_isFrozen || _dashboardVM == null || _activeChannels.Count == 0) return;

            double currentNowOA = DateTime.Now.ToOADate();

            foreach (var kvp in _activeChannels)
            {
                var ch = kvp.Value;
                var sensor = _dashboardVM.Sensors.FirstOrDefault(s => s.Channel == ch.Config.Channel);
                if (sensor != null)
                {
                    double val = sensor.Value;
                    ch.Logger.Add(currentNowOA, val);
                    ch.History.Add((currentNowOA, val));

                    if (val > ch.PeakMax) ch.PeakMax = val;
                    if (val < ch.PeakMin) ch.PeakMin = val;

                    ch.UpdateLabelUI(val, sensor.RawVoltage);
                }
            }

            double windowDays = TimeSpan.FromSeconds(15).TotalDays;
            WpfPlot1.Plot.Axes.SetLimitsX(currentNowOA - windowDays, currentNowOA);

            if (_isAutoY) AutoFitY();

            WpfPlot1.Refresh();
        }

        private void AutoFitY()
        {
            var limits = WpfPlot1.Plot.Axes.GetLimits();
            double minX = limits.Left;
            double maxX = limits.Right;

            double minY = double.MaxValue;
            double maxY = double.MinValue;
            bool found = false;

            foreach (var ch in _activeChannels.Values)
            {
                var visiblePoints = ch.History.Where(p => p.TimeOA >= minX && p.TimeOA <= maxX).ToList();
                if (visiblePoints.Any())
                {
                    double localMin = visiblePoints.Min(p => p.Value);
                    double localMax = visiblePoints.Max(p => p.Value);
                    if (localMin < minY) minY = localMin;
                    if (localMax > maxY) maxY = localMax;
                    found = true;
                }
            }

            if (found)
            {
                double padding = (maxY - minY) * 0.1;
                if (padding == 0) padding = 1.0;
                WpfPlot1.Plot.Axes.SetLimitsY(minY - padding, maxY + padding);
            }
        }

        private void WpfPlot1_MouseMove(object sender, MouseEventArgs e)
        {
            if (_activeChannels.Count == 0) return;

            Point position = e.GetPosition(WpfPlot1);

            // 【修复 1】：WPF 的 Point X/Y 是 double，ScottPlot 的 Pixel 要求 float，必须显式强转
            Pixel mousePixel = new Pixel((float)position.X, (float)position.Y);
            Coordinates mouseLocation = WpfPlot1.Plot.GetCoordinates(mousePixel);

            double mouseTimeOA = mouseLocation.X;

            bool hasData = false;
            double closestTimeOA = 0;
            string tooltipText = "";

            foreach (var ch in _activeChannels.Values)
            {
                if (ch.History.Count == 0) continue;

                var closest = ch.History.OrderBy(p => Math.Abs(p.TimeOA - mouseTimeOA)).First();
                double timeDiffDays = Math.Abs(closest.TimeOA - mouseTimeOA);

                if (timeDiffDays < TimeSpan.FromSeconds(0.5).TotalDays)
                {
                    closestTimeOA = closest.TimeOA;
                    tooltipText += $"■ {ch.Config.Name}: {closest.Value:F3} {ch.Config.Unit}\n";
                    hasData = true;
                }
            }

            if (hasData)
            {
                DateTime ptTime = DateTime.FromOADate(closestTimeOA);
                _tooltipAnnotation.Text = $"时间: {ptTime:HH:mm:ss.fff}\n---------------------\n{tooltipText.Trim()}";

                // 【修复 2】：OffsetX 和 OffsetY 在 ScottPlot 中是 float，必须强转
                _tooltipAnnotation.OffsetX = (float)(position.X > WpfPlot1.ActualWidth - 150 ? position.X - 160 : position.X + 15);
                _tooltipAnnotation.OffsetY = (float)(position.Y + 15);

                _crosshair.Position = new Coordinates(closestTimeOA, mouseLocation.Y);
                _crosshair.IsVisible = true;
                _tooltipAnnotation.IsVisible = true;
            }
            else
            {
                _crosshair.IsVisible = false;
                _tooltipAnnotation.IsVisible = false;
            }

            if (_isFrozen) WpfPlot1.Refresh();
        }

        #region 按钮事件

        private void BtnChangeColor_Click(object sender, RoutedEventArgs e)
        {
            // 【修复 3】：明确使用 System.Windows.Media.Colors
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

                var brush = (SolidColorBrush)BtnChangeColor.Background;
                var sColor = new ScottPlot.Color(brush.Color.R, brush.Color.G, brush.Color.B);

                var logger = WpfPlot1.Plot.Add.DataLogger();
                logger.Color = sColor;
                logger.LineStyle.Width = 1.5f;
                logger.ManageAxisLimits = false;

                var channel = new ActiveChannel(selectedSensor, logger, brush);
                _activeChannels.Add(selectedSensor.Channel, channel);

                InfoPanel.Children.Add(channel.UIPanel);
            }
        }

        private void BtnAutoY_Click(object sender, RoutedEventArgs e)
        {
            _isAutoY = !_isAutoY;
            BtnAutoY.Content = _isAutoY ? "✅ 自适应Y轴" : "❌ 固定Y轴";

            // 【修复 4】：明确使用 System.Windows.Media.Colors
            BtnAutoY.Background = new SolidColorBrush(_isAutoY ? System.Windows.Media.Colors.DarkOliveGreen : System.Windows.Media.Colors.DimGray);
            if (_isAutoY && _isFrozen)
            {
                AutoFitY();
                WpfPlot1.Refresh();
            }
        }

        private void BtnFreeze_Click(object sender, RoutedEventArgs e)
        {
            _isFrozen = !_isFrozen;
            BtnFreeze.Content = _isFrozen ? "▶ 恢复滚动" : "⏸ 画面冻结";

            // 【修复 5】：明确使用 System.Windows.Media.Colors
            BtnFreeze.Background = new SolidColorBrush(_isFrozen ? System.Windows.Media.Colors.Green : System.Windows.Media.Colors.DarkOrange);
            BtnFreeze.Foreground = new SolidColorBrush(_isFrozen ? System.Windows.Media.Colors.White : System.Windows.Media.Colors.Black);
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            foreach (var ch in _activeChannels.Values)
            {
                WpfPlot1.Plot.Remove(ch.Logger);
                InfoPanel.Children.Remove(ch.UIPanel);
            }
            _activeChannels.Clear();
            WpfPlot1.Refresh();
        }

        #endregion

        private class ActiveChannel
        {
            public SensorDisplay Config { get; }
            public DataLogger Logger { get; }
            public List<(double TimeOA, double Value)> History { get; } = new();
            public Border UIPanel { get; }
            private TextBlock InfoText { get; }

            public double PeakMax { get; set; } = double.MinValue;
            public double PeakMin { get; set; } = double.MaxValue;

            public ActiveChannel(SensorDisplay config, DataLogger logger, SolidColorBrush color)
            {
                Config = config;
                Logger = logger;

                UIPanel = new Border
                {
                    // 【修复 6】：明确使用 System.Windows.Media.Color
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(37, 37, 45)),
                    BorderBrush = color,
                    BorderThickness = new Thickness(3, 0, 0, 0),
                    Margin = new Thickness(0, 0, 0, 10),
                    Padding = new Thickness(10)
                };

                InfoText = new TextBlock
                {
                    Foreground = Brushes.White,
                    FontSize = 13,
                    LineHeight = 20,
                    Text = $"[{config.Channel}] {config.Name}\n 当前: --\n 峰值: --\n 谷底: --\n 原始: -- V"
                };

                UIPanel.Child = InfoText;
            }

            public void UpdateLabelUI(double physVal, double rawVal)
            {
                InfoText.Text = $"[{Config.Channel}] {Config.Name}\n 当前: {physVal:F3} {Config.Unit}\n 峰值: {PeakMax:F3}\n 谷底: {PeakMin:F3}\n 原始: {rawVal:F3} V";
            }
        }
    }
}