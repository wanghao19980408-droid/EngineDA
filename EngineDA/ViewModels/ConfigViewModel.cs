using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using EngineDA.Models;
using EngineDA.Services;
using System.Collections.ObjectModel;

namespace EngineDA.ViewModels
{
    public partial class ConfigViewModel : ObservableObject
    {
        public ObservableCollection<SheetConfig> Sheets { get; set; } = new();

        [ObservableProperty]
        private SensorConfig? selectedSensorConfig;

        private readonly SensorConfigService _service;

        public ConfigViewModel()
        {
            _service = new SensorConfigService();
            LoadConfigs();
        }

        public void LoadConfigs()
        {
            Sheets.Clear();
            var sheetNames = _service.GetSheetNames();

            if (sheetNames.Count == 0)
            {
                Sheets.Add(new SheetConfig { SheetName = "发动机", SensorConfigs = new ObservableCollection<SensorConfig>() });
                return;
            }

            foreach (var sheet in sheetNames)
            {
                var sheetConfig = new SheetConfig
                {
                    SheetName = sheet,
                    SensorConfigs = _service.LoadConfigs(sheet)
                };
                Sheets.Add(sheetConfig);
            }
        }

        public IRelayCommand SaveAndRestartCommand => new RelayCommand(SaveAndRestart);


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
                catch (System.IO.IOException)
                {
                    var errorDialog = new Views.ConfirmDialog("保存失败：配置文件正被占用！\n请检查是否在 Excel 中打开了 sensors.xlsx。");
                    errorDialog.ShowDialog();
                }
                catch (System.Exception ex)
                {
                    var errorDialog = new Views.ConfirmDialog($"保存失败：{ex.Message}");
                    errorDialog.ShowDialog();
                }
            }
        }
    }
}