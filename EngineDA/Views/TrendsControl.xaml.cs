using EngineDA.Models;
using EngineDA.ViewModels;
using ScottPlot;
using ScottPlot.Plottables;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
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

#pragma warning disable CS8618 // 在退出构造函数时，不可为 null 的字段必须包含非 null 值。请考虑添加 "required" 修饰符或声明为可为 null。
        public TrendsControl()
#pragma warning restore CS8618 // 在退出构造函数时，不可为 null 的字段必须包含非 null 值。请考虑添加 "required" 修饰符或声明为可为 null。
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

            _uiTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(100) };
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

                int plotIndex = i;
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

            foreach (var kvp in _activeChannels)
            {
                var ch = kvp.Value;
                var sensor = _dashboardVM.Sensors.FirstOrDefault(s => s.Channel == ch.Config.Channel);
                if (sensor != null)
                {
                    double val = sensor.Value;

                    ch.Logger.Add(currentNowOA, val);
                    ch.HistoryBuffer.Add(currentNowOA, val);

                    if (val > ch.PeakMax) ch.PeakMax = val;
                    if (val < ch.PeakMin) ch.PeakMin = val;

                    if (this.IsVisible)
                    {
                        ch.UpdateLabelUI(val, sensor.RawVoltage);
                    }
                }
            }

            if (!this.IsVisible) return;

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

                var channelsOnPlot = _activeChannels.Values.Where(c => c.ParentPlot == plot && c.Logger.IsVisible);

                foreach (var ch in channelsOnPlot)
                {
                    var buf = ch.HistoryBuffer;
                    int count = buf.Count;
                    for (int i = count - 1; i >= 0; i--)
                    {
                        var p = buf[i];
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
                        plot.Plot.Axes.SetLimitsY(minY - padding, Math.Max(minY + 0.001, maxY + padding));
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

            var channelsOnPlot = _activeChannels.Values.Where(c => c.ParentPlot == currentPlot && c.Logger.IsVisible);

            foreach (var ch in channelsOnPlot)
            {
                if (ch.HistoryBuffer.Count == 0) continue;

                int closestIdx = BinarySearchClosestRing(ch.HistoryBuffer, mouseTimeOA);
                if (closestIdx >= 0)
                {
                    var closest = ch.HistoryBuffer[closestIdx];
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
            }
            else
            {
                currentAnnotation.IsVisible = false;
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

        private int BinarySearchClosestRing(RingBuffer buffer, double targetOA)
        {
            int left = 0;
            int right = buffer.Count - 1;
            while (left <= right)
            {
                int mid = left + (right - left) / 2;
                double midVal = buffer[mid].TimeOA;

                if (midVal == targetOA) return mid;
                if (midVal < targetOA) left = mid + 1;
                else right = mid - 1;
            }

            if (left >= buffer.Count) return buffer.Count - 1;
            if (right < 0) return 0;

            return Math.Abs(buffer[left].TimeOA - targetOA) < Math.Abs(buffer[right].TimeOA - targetOA) ? left : right;
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
                System.Windows.Media.Colors.LightPink,
                System.Windows.Media.Colors.DodgerBlue,    
                System.Windows.Media.Colors.SpringGreen,   
                System.Windows.Media.Colors.Gold,          
                System.Windows.Media.Colors.Coral,         
                System.Windows.Media.Colors.HotPink,       
                System.Windows.Media.Colors.Turquoise,     
                System.Windows.Media.Colors.MediumPurple,  
                System.Windows.Media.Colors.White,         
                System.Windows.Media.Colors.Tomato         
            };
            var current = ((SolidColorBrush)BtnChangeColor.Background).Color;
            int idx = Array.IndexOf(colors, current);
            if (idx == -1) idx = 0;
            BtnChangeColor.Background = new SolidColorBrush(colors[(idx + 1) % colors.Length]);
        }

        private void RefreshInfoPanelLayout()
        {
            InfoPanel.Children.Clear();
            var sortedChannels = _activeChannels.Values
                .OrderByDescending(c => c.Config.IsImportant)
                .ThenBy(c => c.Config.Channel)
                .ToList();

            foreach (var ch in sortedChannels)
            {
                InfoPanel.Children.Add(ch.UIPanel);
            }
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

                var channel = new ActiveChannel(selectedSensor, logger, brush, targetPlot, targetPlotIndex + 1, 2000, RemoveSingleChannel);
                _activeChannels.Add(selectedSensor.Channel, channel);

                RefreshInfoPanelLayout();
            }
        }

        private void RemoveSingleChannel(ActiveChannel channel)
        {
            if (channel == null) return;

            channel.ParentPlot.Plot.Remove(channel.Logger);
            _activeChannels.Remove(channel.Config.Channel);

            RefreshInfoPanelLayout();
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
            }
            _activeChannels.Clear();
            InfoPanel.Children.Clear();
            foreach (var plot in _plots) plot.Refresh();
        }

        #endregion

        #region 布局保存与加载逻辑

        public class LayoutConfigItem
        {
            public int Channel { get; set; }
            public int PlotIndex { get; set; }
            public byte R { get; set; }
            public byte G { get; set; }
            public byte B { get; set; }
        }

        private readonly string LayoutFilePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TrendsLayout.json");

        private void BtnSaveLayout_Click(object sender, RoutedEventArgs e)
        {
            if (_activeChannels.Count == 0)
            {
                MessageBox.Show("当前没有添加任何曲线，无法保存布局！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            try
            {
                var layoutList = _activeChannels.Values.Select(ch =>
                {
                    var color = ((SolidColorBrush)((System.Windows.Shapes.Ellipse)((Grid)((Grid)ch.UIPanel.Child).Children[0]).Children[0]).Fill).Color;
                    return new LayoutConfigItem
                    {
                        Channel = ch.Config.Channel,
                        PlotIndex = Array.IndexOf(_plots, ch.ParentPlot),
                        R = color.R,
                        G = color.G,
                        B = color.B
                    };
                }).ToList();

                string json = JsonSerializer.Serialize(layoutList);
                File.WriteAllText(LayoutFilePath, json);

                MessageBox.Show("当前视图布局已成功保存！", "保存成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存布局失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnLoadLayout_Click(object sender, RoutedEventArgs e)
        {
            if (!File.Exists(LayoutFilePath))
            {
                MessageBox.Show("未找到保存的布局文件！请先保存一次布局。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            try
            {
                string json = File.ReadAllText(LayoutFilePath);
                var layoutList = JsonSerializer.Deserialize<List<LayoutConfigItem>>(json);
                if (layoutList == null || _dashboardVM == null) return;

                BtnClear_Click(null, null);

                foreach (var item in layoutList)
                {
                    var sensor = _dashboardVM.Sensors.FirstOrDefault(s => s.Channel == item.Channel);
                    if (sensor == null) continue;

                    var targetPlot = _plots[item.PlotIndex];
                    var sColor = new ScottPlot.Color(item.R, item.G, item.B);
                    var brush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(item.R, item.G, item.B));

                    var logger = targetPlot.Plot.Add.DataLogger();
                    logger.Color = sColor;
                    logger.LineStyle.Width = 1.5f;
                    logger.ManageAxisLimits = false;

                    var channel = new ActiveChannel(sensor, logger, brush, targetPlot, item.PlotIndex + 1, 2000, RemoveSingleChannel);
                    _activeChannels.Add(sensor.Channel, channel);
                }

                RefreshInfoPanelLayout();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载布局失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        public class RingBuffer
        {
            private readonly (double TimeOA, double Value)[] _buffer;
            private int _head;
            public int Count { get; private set; }
            public int Capacity { get; }

            public RingBuffer(int capacity)
            {
                Capacity = capacity;
                _buffer = new (double, double)[capacity];
            }

            public void Add(double timeOA, double value)
            {
                _buffer[_head] = (timeOA, value);
                _head = (_head + 1) % Capacity;
                if (Count < Capacity) Count++;
            }

            public (double TimeOA, double Value) this[int i]
            {
                get
                {
                    if (Count < Capacity) return _buffer[i];
                    return _buffer[(_head + i) % Capacity];
                }
            }
        }

        private class ActiveChannel
        {
            public SensorDisplay Config { get; }
            public DataLogger Logger { get; }
            public ScottPlot.WPF.WpfPlot ParentPlot { get; }
            public RingBuffer HistoryBuffer { get; }
            public Border UIPanel { get; }

            private TextBlock TitleText { get; }
            private TextBlock CurrentValueText { get; }
            private TextBlock StatsText { get; }

            private int PlotIndex { get; }

            public double PeakMax { get; set; } = double.MinValue;
            public double PeakMin { get; set; } = double.MaxValue;

            public ActiveChannel(SensorDisplay config, DataLogger logger, SolidColorBrush color, ScottPlot.WPF.WpfPlot parentPlot, int plotIndex, int bufferSize, Action<ActiveChannel> onDelete)
            {
                Config = config;
                Logger = logger;
                ParentPlot = parentPlot;
                PlotIndex = plotIndex;
                HistoryBuffer = new RingBuffer(bufferSize);

                bool isImp = config.IsImportant;
                var borderColor = isImp ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 215, 0)) : color;
                var thickness = isImp ? new Thickness(2) : new Thickness(4, 0, 0, 0);

                UIPanel = new Border
                {
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(37, 37, 45)),
                    BorderBrush = borderColor,
                    BorderThickness = thickness,
                    CornerRadius = new CornerRadius(6),
                    Margin = new Thickness(0, 0, 0, 12),
                    Padding = new Thickness(15, 12, 15, 12)
                };

                if (isImp)
                {
                    UIPanel.Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        Color = System.Windows.Media.Color.FromRgb(255, 215, 0),
                        BlurRadius = 15,
                        ShadowDepth = 0,
                        Opacity = 0.35
                    };
                }

                Grid mainGrid = new Grid();
                mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                Grid headerGrid = new Grid();
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); 
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                System.Windows.Shapes.Ellipse colorIndicator = new System.Windows.Shapes.Ellipse
                {
                    Width = 12,
                    Height = 12,
                    Fill = color,
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = System.Windows.VerticalAlignment.Center,
                    ToolTip = "该通道在图表中的实际曲线颜色"
                };
                Grid.SetColumn(colorIndicator, 0);
                headerGrid.Children.Add(colorIndicator);

                string star = isImp ? "⭐ " : "";
                TitleText = new TextBlock
                {
                    Foreground = Brushes.White,
                    FontFamily = new FontFamily("Microsoft YaHei, Segoe UI"),
                    FontSize = 14,
                    FontWeight = System.Windows.FontWeights.Bold,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center,
                    Text = $"{star}[图{PlotIndex}] CH{config.Channel} {config.Name}"
                };
                Grid.SetColumn(TitleText, 1);
                headerGrid.Children.Add(TitleText);

                Button btnToggle = new Button
                {
                    Content = "👁️",
                    Width = 24,
                    Height = 24,
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 100, 110)),
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center,
                    Margin = new Thickness(10, 0, 5, 0),
                    ToolTip = "临时隐藏/显示该曲线"
                };

                bool isCurveVisible = true;
                btnToggle.Click += (s, e) =>
                {
                    isCurveVisible = !isCurveVisible;
                    Logger.IsVisible = isCurveVisible;
                    btnToggle.Opacity = isCurveVisible ? 1.0 : 0.4;
                    btnToggle.Content = isCurveVisible ? "👁️" : "❌";
                    ParentPlot.Refresh();
                };
                Grid.SetColumn(btnToggle, 2);
                headerGrid.Children.Add(btnToggle);

                Button btnDelete = new Button
                {
                    Content = "X",
                    Width = 24,
                    Height = 24,
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 50, 50)),
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center,
                    ToolTip = "删除此曲线"
                };
                btnDelete.MouseEnter += (s, e) => btnDelete.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 50, 50));
                btnDelete.MouseLeave += (s, e) => btnDelete.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 50, 50));
                btnDelete.Click += (s, e) => onDelete(this);

                Grid.SetColumn(btnDelete, 3);
                headerGrid.Children.Add(btnDelete);

                Grid.SetRow(headerGrid, 0);
                mainGrid.Children.Add(headerGrid);

                CurrentValueText = new TextBlock
                {
                    Foreground = isImp ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 215, 0)) : new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 230, 118)),
                    FontFamily = new FontFamily("Microsoft YaHei, Segoe UI"),
                    FontSize = 32,
                    FontWeight = System.Windows.FontWeights.Black,
                    Margin = new Thickness(0, 10, 0, 10),
                    Text = "--"
                };
                Grid.SetRow(CurrentValueText, 1);
                mainGrid.Children.Add(CurrentValueText);

                Viewbox statsViewbox = new Viewbox
                {
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                    StretchDirection = StretchDirection.DownOnly
                };

                StatsText = new TextBlock
                {
                    Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(170, 170, 170)),
                    FontFamily = new FontFamily("Microsoft YaHei, Segoe UI"),
                    FontSize = 13,
                    Text = "峰值: --   谷底: --   原始: -- V"
                };

                statsViewbox.Child = StatsText;
                Grid.SetRow(statsViewbox, 2);
                mainGrid.Children.Add(statsViewbox);

                UIPanel.Child = mainGrid;
            }

            public void UpdateLabelUI(double physVal, double rawVal)
            {
                CurrentValueText.Text = $"{physVal:F3} {Config.Unit}";
                StatsText.Text = $"峰值: {PeakMax:F3}   谷底: {PeakMin:F3}   原始: {rawVal:F3} V";
            }
        }
    }
}