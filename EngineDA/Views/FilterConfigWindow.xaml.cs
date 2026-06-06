using EngineDA.Helpers;
using System;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace EngineDA.Views
{
    public partial class FilterConfigWindow : Window
    {
        private readonly string iniPath;

        public FilterConfigWindow()
        {
            InitializeComponent();
            iniPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config.ini");
            LoadConfig();
        }

        private void TopBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) this.DragMove();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        private void LoadConfig()
        {
            string enabledStr = IniConfigHelper.ReadIniData("HistoryFilter", "Enabled", "False", iniPath);
            chkEnableFilter.IsChecked = enabledStr.Equals("True", StringComparison.OrdinalIgnoreCase);

            txtWindowSize20k.Text = IniConfigHelper.ReadIniData("HistoryFilter", "WindowSize20k", "1000", iniPath);
            txtWindowSize1k.Text = IniConfigHelper.ReadIniData("HistoryFilter", "WindowSize1k", "500", iniPath);

            txtDeadZone.Text = IniConfigHelper.ReadIniData("Display", "DeadZone", "0.015", iniPath);
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(txtWindowSize20k.Text, out int win20k) || !int.TryParse(txtWindowSize1k.Text, out int win1k))
            {
                MessageBox.Show("滤波窗口参数必须为有效整数！");
                return;
            }

            if (!double.TryParse(txtDeadZone.Text, out double deadZone))
            {
                MessageBox.Show("死区参数必须为有效数字！(例如 0.015)");
                return;
            }

            IniConfigHelper.WriteIniData("HistoryFilter", "Enabled", chkEnableFilter.IsChecked == true ? "True" : "False", iniPath);

            IniConfigHelper.WriteIniData("HistoryFilter", "WindowSize20k", win20k.ToString(), iniPath);
            IniConfigHelper.WriteIniData("HistoryFilter", "WindowSize1k", win1k.ToString(), iniPath);

            IniConfigHelper.WriteIniData("Display", "DeadZone", deadZone.ToString(), iniPath);

            EngineDA.Models.SensorDisplay.DeadZone = deadZone;

            var dialog = new ConfirmDialog("配置已保存！");
            dialog.ShowDialog();
            Close();
        }
    }
}