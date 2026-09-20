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
        public ObservableCollection<SensorDisplay> Sensors { get; } = new();

        private readonly CollectionView filteredSensorsView;
        public ICollectionView FilteredSensorsView => filteredSensorsView;

        private UdpDataService? udpService1;
        private UdpDataService? udpService2;
        private bool enableIpc1 = false;
        private bool enableIpc2 = false;
        private readonly SensorConfigService configService;

        private readonly Stopwatch processStopwatch = new();
        private DispatcherTimer? clockTimer;

        private bool isDisposed = false;
        private bool isTimeSyncActive = false;

        public event Action<bool>? TimeSyncStateChanged;
        public event EventHandler? DataUpdated;

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

        public DashboardViewModel()
        {
            configService = new SensorConfigService();

            LoadIpcEnableConfig();
            LoadSensorConfigs();

            filteredSensorsView = (CollectionView)CollectionViewSource.GetDefaultView(Sensors);
            filteredSensorsView.Filter = FilterSensor;
            SetupGrouping();

            StartTimers();

            WeakReferenceMessenger.Default.Register<DashboardViewModel, ConfigReloadMessage>(this, (r, m) =>
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    r.LoadIpcEnableConfig();
                    r.LoadSensorConfigs();
                    r.filteredSensorsView?.Refresh();
                });
            });

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
            if (filteredSensorsView == null) return;

            filteredSensorsView.GroupDescriptions.Clear();
            filteredSensorsView.GroupDescriptions.Add(new PropertyGroupDescription("DisplayGroup"));

            filteredSensorsView.SortDescriptions.Clear();
            filteredSensorsView.SortDescriptions.Add(new SortDescription("IsImportant", ListSortDirection.Descending));
            filteredSensorsView.SortDescriptions.Add(new SortDescription("OrderIndex", ListSortDirection.Ascending));
            filteredSensorsView.SortDescriptions.Add(new SortDescription("Unit", ListSortDirection.Ascending));
            filteredSensorsView.SortDescriptions.Add(new SortDescription("Channel", ListSortDirection.Ascending));

            if (filteredSensorsView is ICollectionViewLiveShaping liveView && liveView.CanChangeLiveSorting)
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
            clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            clockTimer.Tick += (_, _) => CurrentTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            clockTimer.Start();
        }

        partial void OnSearchTextChanged(string value)
        {
            filteredSensorsView.Refresh();
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

        public void InitializeUdp()
        {
            if (udpService1 != null || udpService2 != null) return;

            try
            {
                string iniPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.ini");
                string localIp = "0.0.0.0";

                if (enableIpc1)
                {
                    udpService1 = new UdpDataService();
                    string ip1 = IniConfigHelper.ReadIniData("IPC1", "IP", "192.168.1.100", iniPath);
                    int port1 = int.Parse(IniConfigHelper.ReadIniData("IPC1", "PORT", "8063", iniPath));
                    udpService1.Initialize(localIp, ip1, port1);
                    udpService1.DataReceived += OnGeneralUdpDataReceived;
                }

                if (enableIpc2)
                {
                    udpService2 = new UdpDataService();
                    string ip2 = IniConfigHelper.ReadIniData("IPC2", "IP", "192.168.1.101", iniPath);
                    int port2 = int.Parse(IniConfigHelper.ReadIniData("IPC2", "PORT", "8064", iniPath));
                    udpService2.Initialize(localIp, ip2, port2);
                    udpService2.DataReceived += OnGeneralUdpDataReceived;
                }

                UpdateConnectionStatus();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"UDP Init Error: {ex.Message}");
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
                if (udpService1 != null)
                {
                    udpService1.DataReceived -= OnGeneralUdpDataReceived;
                    udpService1.Stop();
                    udpService1 = null;
                }

                if (udpService2 != null)
                {
                    udpService2.DataReceived -= OnGeneralUdpDataReceived;
                    udpService2.Stop();
                    udpService2 = null;
                }

                IsConnected = false;

                LoadIpcEnableConfig();
                LoadSensorConfigs();

                Application.Current?.Dispatcher.Invoke(() =>
                {
                    filteredSensorsView?.Refresh();
                });

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
            var app = Application.Current;
            if (app == null || app.Dispatcher.HasShutdownStarted) return;

            app.Dispatcher.InvokeAsync(() =>
            {
                bool isFromIpc1 = sender == udpService1;
                bool isFromIpc2 = sender == udpService2;

                foreach (var sensor in Sensors)
                {
                    if (isFromIpc1 && sensor.MachineName != "工控机1") continue;
                    if (isFromIpc2 && sensor.MachineName != "工控机2") continue;

                    if (sensor.Channel < 0 || sensor.Channel >= data.Length) continue;
                    sensor.RawVoltage = data[sensor.Channel] / 1000f;

                    if (sensor.MachineName == "工控机1" && sensor.Name == "时统信号")
                    {
                        bool isActive = sensor.RawVoltage > 1.0f;
                        if (isActive && sensor.Name == "总推力") 
                        {
                            sensor.Bvalue -= sensor.Value;
                        }
                        if (isActive != isTimeSyncActive)
                        {
                            isTimeSyncActive = isActive;
                            TimeSyncStateChanged?.Invoke(isTimeSyncActive);
                        }
                    }
                    var totalThrustSensor = Sensors.FirstOrDefault(s => s.Name == "总推力");

                    if (totalThrustSensor != null)
                    {
                        double sumRawVoltage = Sensors
                            .Where(s => s.Unit.Equals("KN", StringComparison.OrdinalIgnoreCase) && s.Name != "总推力")
                            .Sum(s => s.RawVoltage);

                        totalThrustSensor.RawVoltage = sumRawVoltage;
                    }   
                }

                DataUpdated?.Invoke(this, EventArgs.Empty);
                UpdateConnectionStatus();
            }, DispatcherPriority.Render);
        }

        private void CheckSensorAbnormal(SensorDisplay sensor)
        {
            sensor.IsAbnormal = false;
        }

        private void UpdateConnectionStatus()
        {
            bool is1Connected = udpService1?.IsConnected ?? false;
            bool is2Connected = udpService2?.IsConnected ?? false;

            IsConnected = is1Connected || is2Connected;
        }

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
                var configs1 = configService.LoadConfigs("工控机1");
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
                var configs2 = configService.LoadConfigs("工控机2");
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

        public void Dispose()
        {
            if (isDisposed) return;

            clockTimer?.Stop();
            processStopwatch.Stop();

            try
            {
                udpService1?.Stop();
                udpService1 = null;

                udpService2?.Stop();
                udpService2 = null;
            }
            catch { }

            isDisposed = true;
        }
    }
}