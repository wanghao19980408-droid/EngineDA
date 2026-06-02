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

        private short[]? _latestUdpData;
        private UDPValues? _latestGaseData;

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

            _uiRefreshTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(50) };
            _uiRefreshTimer.Tick += UiRefreshTimer_Tick;
            _uiRefreshTimer.Start();
        }

        private void UiRefreshTimer_Tick(object? sender, EventArgs e)
        {
            var data = _latestUdpData;
            var gase = _latestGaseData;
            bool hasUpdate = false;

            if (data != null)
            {
                foreach (var sensor in Sensors)
                {
                    if (sensor.Channel < 0 || sensor.Channel >= data.Length) continue;

                    sensor.RawVoltage = data[sensor.Channel] / 1000f;
                    CheckSensorAbnormal(sensor);
                }
                hasUpdate = true;
            }
            if (hasUpdate)
            {
                NotifyDataUpdated();
            }
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

                string localIp = "0.0.0.0";
                string multicastIp = IniConfigHelper.ReadIniData("Engine", "IP", "224.0.1.63");
                int port = int.Parse(IniConfigHelper.ReadIniData("Engine", "PORT", "8063"));
                _udpService.Initialize(localIp, multicastIp, port);
                _udpService.DataReceived += OnGeneralUdpDataReceived;
                UpdateConnectionStatus();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"UDP Init Error: {ex.Message}");
            }
        }

        private void OnGeneralUdpDataReceived(object? sender, short[] data)
        {
            _latestUdpData = data;
        }

        private void CheckSensorAbnormal(SensorDisplay sensor)
        {
            sensor.IsAbnormal = false;
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
            foreach (var sensor in Sensors)
            {
                sensor.PropertyChanged -= Sensor_PropertyChanged;
            }

            Sensors.Clear();

            var configs = _configService.LoadConfigs("发动机");
            foreach (var cfg in configs)
            {
                if (cfg.Name == "备用") continue;
                var newSensor = MapConfigToSensor(cfg);
                newSensor.PropertyChanged += Sensor_PropertyChanged;
                Sensors.Add(newSensor);
            }
        }

        private void Sensor_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SensorDisplay.IsImportant))
            {
                Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    _filteredSensorsView?.Refresh();
                }, DispatcherPriority.Background);
            }
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
            catch {}

            _isDisposed = true;
        }

        #endregion
    }
}