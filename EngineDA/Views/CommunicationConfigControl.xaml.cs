using EngineDA.Helpers;
using EngineDA.Models;
using CommunityToolkit.Mvvm.Messaging;
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

        private void TopBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }

        private void LoadConfig()
        {
            txtLocalIP.Text = "0.0.0.0";
            txtLocalIP.IsEnabled = false;

            txtMulticastIP.Text = IniConfigHelper.ReadIniData("Engine", "IP", "224.0.1.63", iniPath);
            txtPort.Text = IniConfigHelper.ReadIniData("Engine", "PORT", "8063", iniPath);
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Views.ConfirmDialog("确定要保存当前配置并重新连接网络吗？");
            dialog.ShowDialog();

            if (dialog.Result)
            {
                try
                {
                    IniConfigHelper.WriteIniData("Engine", "IP", txtMulticastIP.Text.Trim(), iniPath);
                    IniConfigHelper.WriteIniData("Engine", "PORT", txtPort.Text.Trim(), iniPath);

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