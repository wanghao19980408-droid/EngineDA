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
            if (!File.Exists(ConfigPath))
            {
                return new ObservableCollection<SensorConfig>(); 
            }

            var list = MiniExcel.Query<SensorConfig>(ConfigPath, sheetName).ToList();
            return new ObservableCollection<SensorConfig>(list);
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
            if (!File.Exists(ConfigPath)) return new List<string>();
            return MiniExcel.GetSheetNames(ConfigPath).ToList();
        }
    }
}