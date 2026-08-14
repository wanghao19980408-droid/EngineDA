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
using System;
using System.Linq;

namespace EngineDA.ViewModels
{
    public partial class DashboardViewModel : ObservableObject, IDisposable
    {
        #region 核心数据与状态

        public ObservableCollection<SensorDisplay> Sensors { get; } = new();

        private readonly CollectionView _filteredSensorsView;
        public ICollectionView FilteredSensorsView => _filteredSensorsView;

        private UdpDataService? _udpService1;
        private UdpDataService? _udpService2;
        private bool enableIpc1 = false;
        private bool enableIpc2 = false;
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

            LoadIpcEnableConfig();
            LoadSensorConfigs();

            _filteredSensorsView = (CollectionView)CollectionViewSource.GetDefaultView(Sensors);
            _filteredSensorsView.Filter = FilterSensor;
            SetupGrouping();

            StartTimers();

            WeakReferenceMessenger.Default.Register<DashboardViewModel, ConfigReloadMessage>(this, (r, m) =>
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    r.LoadIpcEnableConfig();
                    r.LoadSensorConfigs();
                    r._filteredSensorsView?.Refresh();
                });
            });

            // 监听保存网络配置后的重新连接指令
            WeakReferenceMessenger.Default.Register<DashboardViewModel, CommConfigChangedMessage>(this, (r, m) =>
            {
                Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    r.RestartUdp();
                });
            });
        }

        private void LoadIpcEnableConfig()
        {
            string iniPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.ini");
            IniConfigHelper.FilePath = iniPath;
            enableIpc1 = IniConfigHelper.ReadIniData("IPC1", "Enable", "True", iniPath).Equals("True", StringComparison.OrdinalIgnoreCase);
            enableIpc2 = IniConfigHelper.ReadIniData("IPC2", "Enable", "False", iniPath).Equals("True", StringComparison.OrdinalIgnoreCase);
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
            if (_udpService1 != null || _udpService2 != null) return;

            try
            {
                string iniPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.ini");
                string localIp = "0.0.0.0";

                // 初始化 工控机1
                if (enableIpc1)
                {
                    _udpService1 = new UdpDataService();
                    string ip1 = IniConfigHelper.ReadIniData("IPC1", "IP", "192.168.1.100", iniPath);
                    int port1 = int.Parse(IniConfigHelper.ReadIniData("IPC1", "PORT", "8063", iniPath));
                    _udpService1.Initialize(localIp, ip1, port1);
                    _udpService1.DataReceived += OnGeneralUdpDataReceived;
                }

                // 初始化 工控机2
                if (enableIpc2)
                {
                    _udpService2 = new UdpDataService();
                    string ip2 = IniConfigHelper.ReadIniData("IPC2", "IP", "192.168.1.101", iniPath);
                    int port2 = int.Parse(IniConfigHelper.ReadIniData("IPC2", "PORT", "8064", iniPath));
                    _udpService2.Initialize(localIp, ip2, port2);
                    _udpService2.DataReceived += OnGeneralUdpDataReceived;
                }

                UpdateConnectionStatus();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"UDP Init Error: {ex.Message}");
                // 防止端口被占用导致的静默失败，使用主线程弹出提示
                Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    var errorDialog = new EngineDA.Views.ConfirmDialog($"UDP 网络绑定失败，请检查端口是否被占用:\n{ex.Message}");
                    errorDialog.ShowDialog();
                });
            }
        }

        public void RestartUdp()
        {
            try
            {
                // 1. 先安全停用旧的UDP连接
                if (_udpService1 != null)
                {
                    _udpService1.DataReceived -= OnGeneralUdpDataReceived;
                    _udpService1.Stop();
                    _udpService1 = null;
                }

                if (_udpService2 != null)
                {
                    _udpService2.DataReceived -= OnGeneralUdpDataReceived;
                    _udpService2.Stop();
                    _udpService2 = null;
                }

                IsConnected = false;

                // 2. 重新加载配置和传感器映射
                LoadIpcEnableConfig();
                LoadSensorConfigs();

                // 3. 安全刷新UI视图
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    _filteredSensorsView?.Refresh();
                });

                // 4. 重新启动UDP服务
                InitializeUdp();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Restart UDP Error: {ex.Message}");
                Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    var errorDialog = new EngineDA.Views.ConfirmDialog($"重启 UDP 服务出现异常:\n{ex.Message}");
                    errorDialog.ShowDialog();
                });
            }
        }

        private void OnGeneralUdpDataReceived(object? sender, short[] data)
        {
            App.Current.Dispatcher.InvokeAsync(() =>
            {
                bool isFromIpc1 = sender == _udpService1;
                bool isFromIpc2 = sender == _udpService2;

                foreach (var sensor in Sensors)
                {
                    if (isFromIpc1 && sensor.MachineName != "工控机1") continue;
                    if (isFromIpc2 && sensor.MachineName != "工控机2") continue;

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
            bool is1Connected = _udpService1?.IsConnected ?? false;
            bool is2Connected = _udpService2?.IsConnected ?? false;

            IsConnected = is1Connected || is2Connected;
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

            if (enableIpc1)
            {
                var configs1 = _configService.LoadConfigs("工控机1");
                foreach (var cfg in configs1)
                {
                    if (cfg.Name == "备用") continue;
                    var newSensor = MapConfigToSensor(cfg, "工控机1");
                    newSensor.OrderIndex = initialOrder++;
                    newSensor.PropertyChanged += Sensor_PropertyChanged;
                    Sensors.Add(newSensor);
                }
            }

            if (enableIpc2)
            {
                var configs2 = _configService.LoadConfigs("工控机2");
                foreach (var cfg in configs2)
                {
                    if (cfg.Name == "备用") continue;
                    var newSensor = MapConfigToSensor(cfg, "工控机2");
                    newSensor.OrderIndex = initialOrder++;
                    newSensor.PropertyChanged += Sensor_PropertyChanged;
                    Sensors.Add(newSensor);
                }
            }
        }

        private void Sensor_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
        }

        private SensorDisplay MapConfigToSensor(SensorConfig cfg, string machineName)
        {
            return new SensorDisplay
            {
                Name = cfg.Name ?? $"通道{cfg.Channel}",
                Channel = cfg.Channel,
                MachineName = machineName,
                Unit = cfg.Unit ?? "",
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
                _udpService1?.Stop();
                _udpService1 = null;

                _udpService2?.Stop();
                _udpService2 = null;
            }
            catch { }

            _isDisposed = true;
        }

        #endregion
    }
}