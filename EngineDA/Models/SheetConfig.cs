using System.Collections.ObjectModel;

namespace EngineDA.Models;

public class SheetConfig
{
    public string SheetName { get; set; } = string.Empty;
    public ObservableCollection<SensorConfig> SensorConfigs { get; set; } = new();
}
