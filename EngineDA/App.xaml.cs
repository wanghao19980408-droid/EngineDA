using System.Configuration;
using System.Data;
using System.Windows;

namespace EngineDA
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            string iniPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config.ini");
            EngineDA.Helpers.IniConfigHelper.FilePath = iniPath;
        }
    }

}
