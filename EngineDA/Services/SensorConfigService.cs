using EngineDA.Helpers;
using EngineDA.Models;
using MiniExcelLibs;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace EngineDA.Services
{
    public class SensorConfigService
    {
        private readonly string ConfigPath;
        public SensorConfigService()
        {
            ConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sensors.xlsx");
        }

        public ObservableCollection<SensorConfig> LoadConfigs(string sheetName)
        {
            try
            {
                if (!File.Exists(ConfigPath))
                {
                    return new ObservableCollection<SensorConfig>();
                }
                var list = MiniExcel.Query<SensorConfig>(ConfigPath, sheetName).ToList();
                return new ObservableCollection<SensorConfig>(list);
            }
            catch (IOException)
            {
                System.Diagnostics.Debug.WriteLine($"无法加载配置：{ConfigPath} 正被其他程序占用。");
                return new ObservableCollection<SensorConfig>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"加载配置失败: {ex.Message}");
                return new ObservableCollection<SensorConfig>();
            }
        }

        public void SaveAllConfigs(IEnumerable<SheetConfig> sheets)
        {
            if (File.Exists(ConfigPath))
                File.Delete(ConfigPath);

            var sheetsDict = new Dictionary<string, object>();
            foreach (var sheet in sheets)
            {
                sheetsDict.Add(sheet.SheetName, sheet.SensorConfigs);
            }

            MiniExcel.SaveAs(ConfigPath, sheetsDict);
        }

        public List<string> GetSheetNames()
        {
            try
            {
                if (!File.Exists(ConfigPath)) return new List<string>();
                return MiniExcel.GetSheetNames(ConfigPath).ToList();
            }
            catch
            {
                return new List<string>();
            }
        }
    }
}