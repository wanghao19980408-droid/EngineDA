using System;
using System.Threading;
using System.Windows;

namespace EngineDA
{
    public partial class App : Application
    {
        private static Mutex? _appMutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            const string appName = "EngineDA_Unique_App_Mutex";
            bool createdNew;

            _appMutex = new Mutex(true, appName, out createdNew);

            if (!createdNew)
            {
                MessageBox.Show("程序已经在运行中，请勿重复打开！", "系统提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                Application.Current.Shutdown();
                return;
            }

            base.OnStartup(e);

            string iniPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config.ini");
            EngineDA.Helpers.IniConfigHelper.FilePath = iniPath;

            string dzStr = EngineDA.Helpers.IniConfigHelper.ReadIniData("Display", "DeadZone", "0.015", iniPath);
            if (double.TryParse(dzStr, out double dz))
            {
                EngineDA.Models.SensorDisplay.DeadZone = dz;
            }
        }
    }
}