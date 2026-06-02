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

            txtWindowSize.Text = IniConfigHelper.ReadIniData("HistoryFilter", "WindowSize", "10", iniPath);
            txtTrimCount.Text = IniConfigHelper.ReadIniData("HistoryFilter", "TrimCount", "2", iniPath);
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(txtWindowSize.Text, out int windowSize) || !int.TryParse(txtTrimCount.Text, out int trimCount))
            {
                MessageBox.Show("参数必须为整数！");
                return;
            }
            if (windowSize <= trimCount * 2)
            {
                MessageBox.Show("窗口大小必须大于剔除个数的 2 倍！");
                return;
            }

            IniConfigHelper.WriteIniData("HistoryFilter", "Enabled", chkEnableFilter.IsChecked == true ? "True" : "False", iniPath);
            IniConfigHelper.WriteIniData("HistoryFilter", "WindowSize", windowSize.ToString(), iniPath);
            IniConfigHelper.WriteIniData("HistoryFilter", "TrimCount", trimCount.ToString(), iniPath);

            var dialog = new ConfirmDialog("滤波配置已保存！下次加载历史文件时将自动生效。");
            dialog.ShowDialog();
            Close();
        }
    }
}