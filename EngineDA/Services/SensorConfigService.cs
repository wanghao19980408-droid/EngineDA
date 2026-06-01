using EngineDA.Models;
using MiniExcelLibs;
using System.Collections.ObjectModel;
using System.IO;

namespace EngineDA.Services;

public class SensorConfigService
{
    private readonly string _configPath;

    public SensorConfigService()
    {
        _configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sensors.xlsx");
    }

    public ObservableCollection<SensorConfig> LoadConfigs(string sheetName)
    {
        if (!File.Exists(_configPath))
        {
            MiniExcel.SaveAs(_configPath, new List<SensorConfig>());
            return new ObservableCollection<SensorConfig>();
        }

        var list = MiniExcel.Query<SensorConfig>(_configPath, sheetName).ToList();
        return new ObservableCollection<SensorConfig>(list);
    }

    public void SaveConfigs(ObservableCollection<SensorConfig> configs, string sheetName)
    {
        if (File.Exists(_configPath))
            File.Delete(_configPath);

        MiniExcel.SaveAs(_configPath, configs, sheetName: sheetName);
    }

    public List<string> GetSheetNames()
    {
        if (!File.Exists(_configPath)) return new List<string>();
        return MiniExcel.GetSheetNames(_configPath).ToList();
    }
}
