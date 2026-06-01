using EngineDA.Helpers;
using System;
using System.Windows;
using System.Windows.Input;

namespace EngineDA.Views
{
    public partial class CommunicationConfigControl : Window
    {
        private readonly string iniPath;

        public CommunicationConfigControl(string iniPath)
        {
            InitializeComponent();
            this.iniPath = iniPath;
            LoadConfig();
        }

        // 允许按住顶部标题栏拖动窗口
        private void TopBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }

        private void LoadConfig()
        {
            txtLocalIP.Text = IniConfigHelper.ReadIniData("Engine", "Local", "192.168.25.182", iniPath);
            txtMulticastIP.Text = IniConfigHelper.ReadIniData("Engine", "IP", "224.0.1.63", iniPath);
            txtPort.Text = IniConfigHelper.ReadIniData("Engine", "PORT", "8063", iniPath);
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Views.ConfirmDialog("确定要保存当前配置吗？");
            dialog.ShowDialog();

            if (dialog.Result)
            {
                try
                {
                    IniConfigHelper.WriteIniData("Engine", "Local", txtLocalIP.Text, iniPath);
                    IniConfigHelper.WriteIniData("Engine", "IP", txtMulticastIP.Text, iniPath);
                    IniConfigHelper.WriteIniData("Engine", "PORT", txtPort.Text, iniPath);

                    // 保存成功后关闭自身窗口
                    Close();

                    var successDialog = new Views.ConfirmDialog("通信配置保存成功！");
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