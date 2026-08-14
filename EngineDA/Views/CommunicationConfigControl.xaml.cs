using EngineDA.Helpers;
using EngineDA.Models;
using CommunityToolkit.Mvvm.Messaging;
using System;
using System.Windows;
using System.Windows.Input;
using EngineDA.ViewModels;

namespace EngineDA.Views
{
    public partial class CommunicationConfigControl : Window
    {
        private readonly string iniPath;
        ConfigViewModel viewModel;
        HistoryControl historyControl;

        public CommunicationConfigControl(string iniPath)
        {
            InitializeComponent();
            viewModel = new ConfigViewModel();
            historyControl = new HistoryControl();
            this.iniPath = iniPath;
            LoadConfig();
        }

        private void TopBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }

        private void LoadConfig()
        {
            // 加载 工控机1 配置
            string enableIpc1 = IniConfigHelper.ReadIniData("IPC1", "Enable", "True", iniPath);
            chkEnableIpc1.IsChecked = enableIpc1.Equals("True", StringComparison.OrdinalIgnoreCase);
            txtIpc1IP.Text = IniConfigHelper.ReadIniData("IPC1", "IP", "192.168.1.100", iniPath);
            txtIpc1Port.Text = IniConfigHelper.ReadIniData("IPC1", "PORT", "8063", iniPath);

            // 加载 工控机2 配置
            string enableIpc2 = IniConfigHelper.ReadIniData("IPC2", "Enable", "False", iniPath);
            chkEnableIpc2.IsChecked = enableIpc2.Equals("True", StringComparison.OrdinalIgnoreCase);
            txtIpc2IP.Text = IniConfigHelper.ReadIniData("IPC2", "IP", "192.168.1.101", iniPath);
            txtIpc2Port.Text = IniConfigHelper.ReadIniData("IPC2", "PORT", "8064", iniPath);
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Views.ConfirmDialog("确定要保存当前配置并重新连接网络吗？");
            dialog.ShowDialog();

            if (dialog.Result)
            {
                try
                {
                    IniConfigHelper.WriteIniData("IPC1", "Enable", chkEnableIpc1.IsChecked == true ? "True" : "False", iniPath);
                    IniConfigHelper.WriteIniData("IPC1", "IP", txtIpc1IP.Text.Trim(), iniPath);
                    IniConfigHelper.WriteIniData("IPC1", "PORT", txtIpc1Port.Text.Trim(), iniPath);

                    IniConfigHelper.WriteIniData("IPC2", "Enable", chkEnableIpc2.IsChecked == true ? "True" : "False", iniPath);
                    IniConfigHelper.WriteIniData("IPC2", "IP", txtIpc2IP.Text.Trim(), iniPath);
                    IniConfigHelper.WriteIniData("IPC2", "PORT", txtIpc2Port.Text.Trim(), iniPath);

                    viewModel.LoadConfigs();
                    historyControl.AutoLoadSystemConfigs();
                    Close();

                    WeakReferenceMessenger.Default.Send(new CommConfigChangedMessage());

                    var successDialog = new Views.ConfirmDialog("通信配置保存成功，正在重新连接！");
                    successDialog.ShowDialog();
                }
                catch (Exception ex)
                {
                    var errorDialog = new Views.ConfirmDialog($"通信配置保存失败: {ex.Message}");
                    errorDialog.ShowDialog();
                }
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}