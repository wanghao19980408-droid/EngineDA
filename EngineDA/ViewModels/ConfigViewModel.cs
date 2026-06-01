using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using EngineDA.Models;
using EngineDA.Services;
using System.Collections.ObjectModel;

namespace EngineDA.ViewModels;

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
        var dialog = new Views.ConfirmDialog("确定要保存当前配置吗？");
        dialog.ShowDialog();

        if (dialog.Result)
        {
            foreach (var sheet in Sheets)
                _service.SaveConfigs(sheet.SensorConfigs, sheet.SheetName);

            var successDialog = new Views.ConfirmDialog("保存成功！");
            successDialog.ShowDialog();
        }
    }
}
