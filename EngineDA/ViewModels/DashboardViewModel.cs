using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using EngineDA.Converts;
using EngineDA.Helpers;
using EngineDA.Models;
using EngineDA.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace EngineDA.ViewModels
{
    public partial class DashboardViewModel : ObservableObject, IDisposable
    {
        #region 核心数据与状态

        public ObservableCollection<SensorDisplay> Sensors { get; } = new();

        private readonly CollectionView _filteredSensorsView;
        public ICollectionView FilteredSensorsView => _filteredSensorsView;

        private UdpDataService? _udpService;
        private UdpDataService? _udpServiceGase;
        private readonly SensorConfigService _configService;

        private readonly Stopwatch _processStopwatch = new();
        private DispatcherTimer? _uiRefreshTimer; 
        private DispatcherTimer? _clockTimer;     

        private bool _isDisposed = false;

        private readonly HashSet<string> _specialSensors = new() { "Gpb3", "Gpb8", "Cpb15", "Cpb17" };

        #endregion

        #region 可绑定属性 (Observable Properties)

        [ObservableProperty]
        private string searchText = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ConnectionStatusText))]
        [NotifyPropertyChangedFor(nameof(ConnectionStatusColor))]
        private bool isConnected;

        [ObservableProperty]
        private string currentTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");


        public string ConnectionStatusText => IsConnected ? "已连接" : "未连接";
        public Brush ConnectionStatusColor => IsConnected ? Brushes.LimeGreen : Brushes.Red;

        #endregion

        #region 构造与初始化
        public DashboardViewModel()
        {
            _configService = new SensorConfigService();

            LoadSensorConfigs();

            _filteredSensorsView = (CollectionView)CollectionViewSource.GetDefaultView(Sensors);
            _filteredSensorsView.Filter = FilterSensor;
            SetupGrouping();

            StartTimers();

            InitializeUdp();

            WeakReferenceMessenger.Default.Register<DashboardViewModel, ConfigReloadMessage>(this, (r, m) =>
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    r.LoadSensorConfigs();

                    r._filteredSensorsView?.Refresh();
                });
            });
        }

        private void SetupGrouping()
        {
            if (_filteredSensorsView == null) return;

            _filteredSensorsView.GroupDescriptions.Clear();
            _filteredSensorsView.GroupDescriptions.Add(new PropertyGroupDescription("DisplayGroup"));

            if (_filteredSensorsView is ListCollectionView liveView)
            {
                liveView.IsLiveGrouping = true;
                liveView.LiveGroupingProperties.Add("DisplayGroup");

                liveView.IsLiveSorting = true;
                liveView.LiveSortingProperties.Add("IsImportant");
                liveView.LiveSortingProperties.Add("Unit");
                liveView.LiveSortingProperties.Add("Channel");
                liveView.LiveSortingProperties.Add("Name");
            }

            _filteredSensorsView.SortDescriptions.Clear();

            _filteredSensorsView.SortDescriptions.Add(new SortDescription("IsImportant", ListSortDirection.Descending));

            _filteredSensorsView.SortDescriptions.Add(new SortDescription("Unit", ListSortDirection.Ascending));

            _filteredSensorsView.SortDescriptions.Add(new SortDescription("Channel", ListSortDirection.Ascending));
        }

        private void StartTimers()
        {
            _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clockTimer.Tick += (_, _) => CurrentTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            _clockTimer.Start();

            _uiRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        }

        #endregion

        #region 命令 (Commands)

        partial void OnSearchTextChanged(string value)
        {
            _filteredSensorsView.Refresh();
        }

        private bool FilterSensor(object obj)
        {
            if (string.IsNullOrWhiteSpace(SearchText)) return true;
            if (obj is SensorDisplay sensor)
            {
                return sensor.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
            }
            return true;
        }

        #endregion

        #region UDP 通信逻辑

        public void InitializeUdp()
        {
            if (_udpService != null) return;

            try
            {
                _udpService = new UdpDataService();
                _udpServiceGase = new UdpDataService();

                string iniPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.ini");
                IniConfigHelper.FilePath = iniPath;

                string localIp = IniConfigHelper.ReadIniData("Engine", "Local", "0.0.0.0");
                string multicastIp = IniConfigHelper.ReadIniData("Engine", "IP", "239.0.0.1");
                int port = int.Parse(IniConfigHelper.ReadIniData("Engine", "PORT", "12345"));

                string gaseLocalIp = IniConfigHelper.ReadIniData("Gase", "Local", "0.0.0.0");
                string gaseMulticastIp = IniConfigHelper.ReadIniData("Gase", "IP", "239.0.0.1");
                int gasePort = int.Parse(IniConfigHelper.ReadIniData("Gase", "PORT", "12345"));
                int[] gaseHeader = { 246, 10, 26, 8, 1, 216, 0 };

                _udpService.Initialize(localIp, multicastIp, port);
                _udpService.DataReceived += OnGeneralUdpDataReceived;

                _udpServiceGase.Initialize(gaseLocalIp, gaseMulticastIp, gasePort, gaseHeader);
                _udpServiceGase.StructuredDataReceived += OnGaseUdpDataReceived;

                UpdateConnectionStatus();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"UDP Init Error: {ex.Message}");
            }
        }

        private void OnGaseUdpDataReceived(object? sender, UDPValues e)
        {
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                foreach (var sensor in Sensors)
                {
                    if (!_specialSensors.Contains(sensor.Name)) continue;

                    ApplySpecialFormula(sensor, e);
                    CheckSensorAbnormal(sensor);
                }

                NotifyDataUpdated();
            }, DispatcherPriority.DataBind);
        }

        private void OnGeneralUdpDataReceived(object? sender, short[] data)
        {
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                foreach (var sensor in Sensors)
                {
                    if (_specialSensors.Contains(sensor.Name)) continue;
                    if (sensor.Channel < 0 || sensor.Channel >= data.Length) continue;

                    sensor.RawVoltage = data[sensor.Channel] / 1000f;
                    CheckSensorAbnormal(sensor);
                }

                NotifyDataUpdated();
            }, DispatcherPriority.DataBind);
        }

        private void ApplySpecialFormula(SensorDisplay sensor, UDPValues e)
        {
            const float factor = 0.000579f;
            const float offset = 4f;

            switch (sensor.Name)
            {
                case "Gpb3":
                    if (e.AI_value?.Length > 30) sensor.RawVoltage = e.AI_value[30] * factor + offset;
                    break;
                case "Gpb8":
                    if (e.AI_value?.Length > 35) sensor.RawVoltage = e.AI_value[35] * factor + offset;
                    break;
                case "Cpb15":
                    if (e.AI_value?.Length > 66) sensor.RawVoltage = e.AI_value[66] * factor + offset;
                    break;
                case "Cpb17":
                    if (e.AI_value?.Length > 69) sensor.RawVoltage = e.AI_value[69] * factor + offset;
                    break;
            }
        }

        private void CheckSensorAbnormal(SensorDisplay sensor)
        {
            sensor.IsAbnormal = sensor.RawVoltage < sensor.Min || sensor.RawVoltage > sensor.Max;
        }

        private void NotifyDataUpdated()
        {
            DataUpdated?.Invoke(this, EventArgs.Empty);
            UpdateConnectionStatus();
        }

        private void UpdateConnectionStatus()
        {
            bool connected = (_udpService?.IsConnected ?? false) || (_udpServiceGase?.IsConnected ?? false);

            if (IsConnected != connected)
            {
                IsConnected = connected;
            }
        }

        public event EventHandler? DataUpdated;

        #endregion

        #region 配置加载

        private void LoadSensorConfigs()
        {
            Sensors.Clear();

            var configs = _configService.LoadConfigs("发动机");
            foreach (var cfg in configs)
            {
                if (cfg.Name == "备用") continue;
                Sensors.Add(MapConfigToSensor(cfg));
            }
            AddHardcodedSensor("Gpb3", 30, "MPa", 4, 20, 0.625, -2.5);
            AddHardcodedSensor("Gpb8", 35, "MPa", 4, 20, 1.0, -4.0);
            AddHardcodedSensor("Cpb15", 66, "MPa", 4, 20, 1.5625, -6.25);
            AddHardcodedSensor("Cpb17", 69, "MPa", 4, 20, 1.5625, -6.25);
        }

        private SensorDisplay MapConfigToSensor(SensorConfig cfg)
        {
            return new SensorDisplay
            {
                Name = cfg.Name ?? $"通道{cfg.Channel}",
                Channel = cfg.Channel,
                Unit = cfg.Unit ?? "",
                Min = cfg.Min,
                Max = cfg.Max,
                Kvalue = cfg.K,
                Bvalue = cfg.B,
                RawVoltage = 0,
                Color = cfg.Color,
                IsImportant = cfg.IsImportant,

            };
        }

        private void AddHardcodedSensor(string name, int channel, string unit, double min, double max, double k, double b)
        {
            Sensors.Add(new SensorDisplay
            {
                Name = name,
                Channel = channel,
                Unit = unit,
                Min = min,
                Max = max,
                Kvalue = k,
                Bvalue = b,
                RawVoltage = 0
            });
        }

        #endregion

        #region 资源释放 (IDisposable)

        public void Dispose()
        {
            if (_isDisposed) return;

            _clockTimer?.Stop();
            _uiRefreshTimer?.Stop();
            _processStopwatch.Stop();

            try
            {
                _udpService?.Stop();
                _udpServiceGase?.Stop();
                _udpService = null;
                _udpServiceGase = null;
            }
            catch { /* 忽略停止时的错误 */ }

            _isDisposed = true;
        }

        #endregion
    }
}