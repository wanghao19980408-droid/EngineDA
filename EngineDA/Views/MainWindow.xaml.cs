using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using EngineDA.ViewModels;
using EngineDA.Views;

namespace EngineDA
{
    public partial class MainWindow : Window
    {
        private readonly DashboardViewModel dashboardVM = new();
        private readonly ConfigViewModel configVM = new();

        private readonly RealTimeDataControl realTimeDataControl;
        private readonly HistoryControl historyControl;
        private readonly ConfigControl configControl;

        private readonly List<TrendsControl> dynamicTrends = new();

        private Point dragStartPoint;
        private bool isDragging = false;

        public MainWindow()
        {
            InitializeComponent();

            realTimeDataControl = new RealTimeDataControl { DataContext = dashboardVM };
            historyControl = new HistoryControl();
            configControl = new ConfigControl { DataContext = configVM };

            MainContentGrid.Children.Add(realTimeDataControl);
            MainContentGrid.Children.Add(historyControl);
            MainContentGrid.Children.Add(configControl);


            var trendsControl = new TrendsControl();

            dynamicTrends.Add(trendsControl);
            TrendsContainer.Children.Add(trendsControl);

            this.DataContext = dashboardVM;
            dashboardVM.InitializeUdp();

            ShowPage(realTimeDataControl);
            StartClock();
        }

        private void ToggleSidebar_Click(object sender, RoutedEventArgs e)
        {
            double targetWidth = SidebarBorder.Width > 0 ? 0 : 200;
            DoubleAnimation animation = new DoubleAnimation
            {
                To = targetWidth,
                Duration = TimeSpan.FromMilliseconds(250),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };
            SidebarBorder.BeginAnimation(WidthProperty, animation);
        }

        private void NavRadioButton_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is not RadioButton rb) return;
            if (realTimeDataControl == null || historyControl == null || configControl == null || TrendsPageGrid == null) return;

            realTimeDataControl.Visibility = Visibility.Collapsed;
            historyControl.Visibility = Visibility.Collapsed;
            configControl.Visibility = Visibility.Collapsed;
            TrendsPageGrid.Visibility = Visibility.Collapsed;

            switch (rb.Content?.ToString())
            {
                case "实时数据":
                    realTimeDataControl.Visibility = Visibility.Visible;
                    break;
                case "实时曲线":
                    TrendsPageGrid.Visibility = Visibility.Visible;
                    break;
                case "历史数据":
                    historyControl.Visibility = Visibility.Visible;
                    break;
                case "配置文件":
                    configControl.Visibility = Visibility.Visible;
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
                dashboardVM.Dispose();
                Close();
            }
        }
        private void OpenAdvancedConfig_Click(object sender, RoutedEventArgs e)
        {
            var pwdDialog = new PasswordDialog();
            pwdDialog.ShowDialog();

            if (pwdDialog.IsAuthenticated)
            {
                new FilterConfigWindow().ShowDialog();
            }
        }
        private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                isDragging = false;
                Maximize_Click(sender, e);
                return;
            }

            if (e.LeftButton == MouseButtonState.Pressed)
            {
                dragStartPoint = e.GetPosition(this);
                isDragging = true;

                if (this.WindowState == WindowState.Normal)
                {
                    this.DragMove();
                    isDragging = false;
                }
            }
        }

        private void Border_MouseMove(object sender, MouseEventArgs e)
        {
            if (!isDragging || e.LeftButton != MouseButtonState.Pressed)
            {
                isDragging = false;
                return;
            }

            if (this.WindowState == WindowState.Maximized)
            {
                Point currentPoint = e.GetPosition(this);

                if (Math.Abs(currentPoint.X - dragStartPoint.X) > 15 ||
                    Math.Abs(currentPoint.Y - dragStartPoint.Y) > 15)
                {
                    Point physicalScreenPos = this.PointToScreen(e.GetPosition(this));

                    this.WindowState = WindowState.Normal;

                    PresentationSource source = PresentationSource.FromVisual(this);
                    if (source?.CompositionTarget != null)
                    {
                        Point logicalScreenPos = source.CompositionTarget.TransformFromDevice.Transform(physicalScreenPos);
                        this.Left = logicalScreenPos.X - (this.Width / 2);
                        this.Top = logicalScreenPos.Y - 20;
                    }
                    else
                    {
                        this.Left = physicalScreenPos.X - (this.Width / 2);
                        this.Top = physicalScreenPos.Y - 20;
                    }

                    if (this.Left < 0) this.Left = 0;
                    if (this.Top < 0) this.Top = 0;

                    isDragging = false;

                    this.DragMove();
                }
            }
        }

        private void Border_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            isDragging = false;
        }

        private void OpenKBCalculator_Click(object sender, RoutedEventArgs e)
        {
            new KBCalculatorWindow().ShowDialog();
        }

        private void OpenComm_Click(object sender, RoutedEventArgs e)
        {
            var iniPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config.ini");
            new CommunicationConfigControl(iniPath).ShowDialog();
        }

        public void StartClock()
        {
            var clockTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
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