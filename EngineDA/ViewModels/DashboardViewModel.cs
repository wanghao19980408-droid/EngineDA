using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using EngineDA.Helpers;
using EngineDA.Models;
using EngineDA.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
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
        private readonly SensorConfigService _configService;

        private readonly Stopwatch _processStopwatch = new();
        private DispatcherTimer? _clockTimer; 

        private bool _isDisposed = false;
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

            WeakReferenceMessenger.Default.Register<DashboardViewModel, CommConfigChangedMessage>(this, (r, m) =>
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    r.RestartUdp();
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
            _filteredSensorsView.SortDescriptions.Add(new SortDescription("OrderIndex", ListSortDirection.Ascending));
            _filteredSensorsView.SortDescriptions.Add(new SortDescription("Unit", ListSortDirection.Ascending));
            _filteredSensorsView.SortDescriptions.Add(new SortDescription("Channel", ListSortDirection.Ascending));

            if (_filteredSensorsView is ICollectionViewLiveShaping liveView && liveView.CanChangeLiveSorting)
            {
                liveView.LiveSortingProperties.Add(nameof(SensorDisplay.IsImportant));
                liveView.LiveSortingProperties.Add(nameof(SensorDisplay.OrderIndex));
                liveView.IsLiveSorting = true;

                liveView.LiveGroupingProperties.Add(nameof(SensorDisplay.DisplayGroup));
                liveView.IsLiveGrouping = true;
            }
        }

        private void StartTimers()
        {
            _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clockTimer.Tick += (_, _) => CurrentTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            _clockTimer.Start();
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

        [RelayCommand]
        private void MoveCardUp(SensorDisplay currentSensor)
        {
            if (currentSensor == null || !currentSensor.IsImportant) return;

            var importantSensors = Sensors.Where(s => s.IsImportant)
                                          .OrderBy(s => s.OrderIndex)
                                          .ThenBy(s => s.Channel)
                                          .ToList();

            int index = importantSensors.IndexOf(currentSensor);
            if (index > 0)
            {
                var previousSensor = importantSensors[index - 1];

                int temp = currentSensor.OrderIndex;
                currentSensor.OrderIndex = previousSensor.OrderIndex;
                previousSensor.OrderIndex = temp;

                if (currentSensor.OrderIndex == previousSensor.OrderIndex)
                {
                    previousSensor.OrderIndex = index;
                    currentSensor.OrderIndex = index - 1;
                }
            }
        }

        [RelayCommand]
        private void MoveCardDown(SensorDisplay currentSensor)
        {
            if (currentSensor == null || !currentSensor.IsImportant) return;

            var importantSensors = Sensors.Where(s => s.IsImportant)
                                          .OrderBy(s => s.OrderIndex)
                                          .ThenBy(s => s.Channel)
                                          .ToList();

            int index = importantSensors.IndexOf(currentSensor);
            if (index >= 0 && index < importantSensors.Count - 1)
            {
                var nextSensor = importantSensors[index + 1];

                int temp = currentSensor.OrderIndex;
                currentSensor.OrderIndex = nextSensor.OrderIndex;
                nextSensor.OrderIndex = temp;

                if (currentSensor.OrderIndex == nextSensor.OrderIndex)
                {
                    nextSensor.OrderIndex = index;
                    currentSensor.OrderIndex = index + 1;
                }
            }
        }

        #endregion

        #region UDP 通信逻辑

        public void InitializeUdp()
        {
            if (_udpService != null) return;

            try
            {
                _udpService = new UdpDataService();
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

        public void RestartUdp()
        {
            try
            {
                if (_udpService != null)
                {
                    _udpService.DataReceived -= OnGeneralUdpDataReceived;
                    _udpService.Stop();
                    _udpService = null;
                }
                IsConnected = false;
                InitializeUdp();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Restart UDP Error: {ex.Message}");
            }
        }

        private void OnGeneralUdpDataReceived(object? sender, short[] data)
        {
            App.Current.Dispatcher.InvokeAsync(() =>
            {
                foreach (var sensor in Sensors)
                {
                    if (sensor.Channel < 0 || sensor.Channel >= data.Length) continue;
                    sensor.RawVoltage = data[sensor.Channel] / 1000f;
                }

                DataUpdated?.Invoke(this, EventArgs.Empty);
                UpdateConnectionStatus();
            }, DispatcherPriority.Render);
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
            IsConnected = _udpService?.IsConnected ?? false;
            OnPropertyChanged(nameof(ConnectionStatusText));
            OnPropertyChanged(nameof(ConnectionStatusColor));
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
            int initialOrder = 0;

            var configs = _configService.LoadConfigs("发动机");
            foreach (var cfg in configs)
            {
                if (cfg.Name == "备用") continue;
                var newSensor = MapConfigToSensor(cfg);
                newSensor.OrderIndex = initialOrder++;
                newSensor.PropertyChanged += Sensor_PropertyChanged;
                Sensors.Add(newSensor);
            }
        }

        private void Sensor_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
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
            _processStopwatch.Stop();

            try
            {
                _udpService?.Stop();
                _udpService = null;
            }
            catch { }

            _isDisposed = true;
        }

        #endregion
    }
}