using EngineDA.Models;
using MiniExcelLibs;
using System.Collections.ObjectModel;
using System.IO;

namespace EngineDA.Services;

public class SensorConfigService
{
    private readonly string ConfigPath;

    public SensorConfigService()
    {
        ConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sensors.xlsx");
    }

    public ObservableCollection<SensorConfig> LoadConfigs(string sheetName)
    {
        if (!File.Exists(ConfigPath))
        {
            MiniExcel.SaveAs(ConfigPath, new List<SensorConfig>());
            return new ObservableCollection<SensorConfig>();
        }

        var list = MiniExcel.Query<SensorConfig>(ConfigPath, sheetName).ToList();
        return new ObservableCollection<SensorConfig>(list);
    }

    public void SaveConfigs(ObservableCollection<SensorConfig> configs, string sheetName)
    {
        if (File.Exists(ConfigPath))
            File.Delete(ConfigPath);

        MiniExcel.SaveAs(ConfigPath, configs, sheetName: sheetName);
    }

    public List<string> GetSheetNames()
    {
        if (!File.Exists(ConfigPath)) return new List<string>();
        return MiniExcel.GetSheetNames(ConfigPath).ToList();
    }
}
