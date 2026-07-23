using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using EngineDA.Helpers;
using EngineDA.Models;
using EngineDA.Services;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace EngineDA.ViewModels
{
    public partial class ConfigViewModel : ObservableObject
    {
        public ObservableCollection<SheetConfig> Sheets { get; set; } = new();

        private bool enableIpc1;
        private bool enableIpc2;

        [ObservableProperty]
        private SensorConfig? selectedSensorConfig;

        private readonly SensorConfigService _service;

        public ConfigViewModel()
        {
            string iniPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.ini");

            enableIpc1 = IniConfigHelper.ReadIniData("IPC1", "Enable", "True", iniPath).Equals("True", StringComparison.OrdinalIgnoreCase);
            enableIpc2 = IniConfigHelper.ReadIniData("IPC2", "Enable", "False", iniPath).Equals("True", StringComparison.OrdinalIgnoreCase);

            _service = new SensorConfigService();
            LoadConfigs();
        }

        public void LoadConfigs()
        {
            Sheets.Clear();
            var sheetNames = _service.GetSheetNames();

            if (sheetNames == null || sheetNames.Count == 0)
            {
                return;
            }

            foreach (var sheet in sheetNames)
            {
                if (sheet == "工控机1" && !enableIpc1) continue;
                if (sheet == "工控机2" && !enableIpc2) continue;

                var sheetConfig = new SheetConfig
                {
                    SheetName = sheet,
                    SensorConfigs = _service.LoadConfigs(sheet)
                };
                Sheets.Add(sheetConfig);
            }
        }

        [RelayCommand]
        private void SaveAndRestart()
        {
            var dialog = new Views.ConfirmDialog("确定要保存当前配置并立即生效吗？");
            dialog.ShowDialog();

            if (dialog.Result)
            {
                try
                {
                    _service.SaveAllConfigs(Sheets);

                    WeakReferenceMessenger.Default.Send(new ConfigReloadMessage());

                    var successDialog = new Views.ConfirmDialog("配置保存成功，已实时生效！");
                    successDialog.ShowDialog();
                }
                catch (IOException)
                {
                    var errorDialog = new Views.ConfirmDialog("保存失败：配置文件正被占用！\n请检查是否在 Excel 中打开了 sensors.xlsx。");
                    errorDialog.ShowDialog();
                }
                catch (Exception ex)
                {
                    var errorDialog = new Views.ConfirmDialog($"保存失败：{ex.Message}");
                    errorDialog.ShowDialog();
                }
            }
        }
    }
}