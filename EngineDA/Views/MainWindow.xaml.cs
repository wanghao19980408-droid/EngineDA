using EngineDA.ViewModels;
using EngineDA.Views;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace EngineDA
{
    public partial class MainWindow : Window
    {
        private readonly DashboardViewModel _dashboardVM = new();
        private readonly ConfigViewModel _configVM = new();
        private readonly RealTimeDataControl _realTimeDataControl;
        private readonly HistoryControl _historyControl;
        private readonly ConfigControl _configControl;

        private readonly List<TrendsControl> _dynamicTrends = new();

        public MainWindow()
        {
            InitializeComponent();

            _realTimeDataControl = new RealTimeDataControl { DataContext = _dashboardVM };
            _historyControl = new HistoryControl();
            _configControl = new ConfigControl { DataContext = _configVM };

            MainContentGrid.Children.Add(_realTimeDataControl);
            MainContentGrid.Children.Add(_historyControl);
            MainContentGrid.Children.Add(_configControl);

            this.DataContext = _dashboardVM;
            _dashboardVM.InitializeUdp();
            ShowPage(_realTimeDataControl);
            StartClock();
        }

        /// <summary>
        /// 添加新的趋势图控件
        /// </summary>
        private void AddTrendControl_Click(object sender, RoutedEventArgs e)
        {
            if (_dashboardVM?.Sensors == null) return;

            try
            {
                // 创建新的趋势图ViewModel和控件
                var trendsViewModel = new TrendsViewModel(_dashboardVM.Sensors);
                var trendsControl = new TrendsControl
                {
                    DataContext = trendsViewModel
                };

                // 创建一个容器Grid，包含趋势图和关闭按钮
                var containerGrid = new Grid
                {
                    Margin = new Thickness(8)
                };

                // 先添加趋势图控件到容器
                containerGrid.Children.Add(trendsControl);

                // 创建圆形关闭按钮
                var closeButtonBorder = new Border
                {
                    Width = 36,
                    Height = 36,
                    CornerRadius = new CornerRadius(18),
                    Background = new SolidColorBrush(Color.FromArgb(230, 255, 255, 255)),
                    BorderThickness = new Thickness(2),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(220, 53, 69)),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 20, 20, 0),
                    Cursor = Cursors.Hand,
                    Child = new TextBlock
                    {
                        Text = "✕",
                        FontSize = 18,
                        FontWeight = FontWeights.Bold,
                        Foreground = new SolidColorBrush(Color.FromRgb(220, 53, 69)),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };

                // 添加阴影效果
                closeButtonBorder.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black,
                    Direction = 270,
                    ShadowDepth = 2,
                    BlurRadius = 8,
                    Opacity = 0.3
                };

                // 鼠标悬停效果
                closeButtonBorder.MouseEnter += (s, args) =>
                {
                    closeButtonBorder.Background = new SolidColorBrush(Color.FromRgb(220, 53, 69));
                    if (closeButtonBorder.Child is TextBlock tb)
                        tb.Foreground = Brushes.White;
                };

                closeButtonBorder.MouseLeave += (s, args) =>
                {
                    closeButtonBorder.Background = new SolidColorBrush(Color.FromArgb(230, 255, 255, 255));
                    if (closeButtonBorder.Child is TextBlock tb)
                        tb.Foreground = new SolidColorBrush(Color.FromRgb(220, 53, 69));
                };

                // 点击关闭事件
                closeButtonBorder.MouseLeftButtonDown += (s, args) =>
                {
                    // 从容器中移除
                    TrendsContainer.Children.Remove(containerGrid);
                    _dynamicTrends.Remove(trendsControl);

                    // 防止事件冒泡
                    args.Handled = true;
                };

                // 设置关闭按钮的层级（确保在最上层）
                Panel.SetZIndex(closeButtonBorder, 1000);
                containerGrid.Children.Add(closeButtonBorder);

                // 最后添加到主容器
                TrendsContainer.Children.Add(containerGrid);
                _dynamicTrends.Add(trendsControl);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"添加趋势图失败: {ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void NavRadioButton_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is not RadioButton rb) return;

            _realTimeDataControl.Visibility = Visibility.Collapsed;
            _historyControl.Visibility = Visibility.Collapsed;
            _configControl.Visibility = Visibility.Collapsed;
            TrendsPageGrid.Visibility = Visibility.Collapsed;

            switch (rb.Content?.ToString())
            {
                case "实时数据":
                    _realTimeDataControl.Visibility = Visibility.Visible;
                    break;
                case "实时曲线":
                    TrendsPageGrid.Visibility = Visibility.Visible;
                    break;
                case "历史数据":
                    _historyControl.Visibility = Visibility.Visible;
                    break;
                case "配置文件":
                    _configControl.Visibility = Visibility.Visible;
                    break;
            }
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
            => WindowState = WindowState.Minimized;

        private void Maximize_Click(object sender, RoutedEventArgs e)
            => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ConfirmDialog("确定要关闭程序吗？");
            dialog.ShowDialog();
            if (dialog.Result)
            {
                _dashboardVM.Dispose();
                Close();
            }
        }

        private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }

        private void OpenKBCalculator_Click(object sender, RoutedEventArgs e)
        {
            var kbWindow = new KBCalculatorWindow();
            kbWindow.ShowDialog();
        }

        private void OpenComm_Click(object sender, RoutedEventArgs e)
        {
            var iniPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config.ini");
            var commWindow = new CommunicationConfigControl(iniPath);
            commWindow.ShowDialog();
        }

        public void StartClock()
        {
            var clockTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            clockTimer.Start();
        }

        private void ShowPage(UIElement page)
        {
            foreach (UIElement child in MainContentGrid.Children)
                child.Visibility = Visibility.Collapsed;

            page.Visibility = Visibility.Visible;
        }
    }
}