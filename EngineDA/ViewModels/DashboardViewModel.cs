using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

        // 传感器集合
        public ObservableCollection<SensorDisplay> Sensors { get; } = new();

        // 筛选视图
        private readonly CollectionView _filteredSensorsView;
        public ICollectionView FilteredSensorsView => _filteredSensorsView;

        // 服务
        private UdpDataService? _udpService;
        private UdpDataService? _udpServiceGase;
        private readonly SensorConfigService _configService;

        // 定时器与秒表
        private readonly Stopwatch _processStopwatch = new();
        private DispatcherTimer? _uiRefreshTimer; // 用于刷新秒表显示
        private DispatcherTimer? _clockTimer;     // 用于刷新系统时间

        // 状态标志
        private bool _isSignalActive = false;
        private bool _isDisposed = false;

        // 特殊传感器名称集合（用于在通用UDP处理中跳过）
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

        [ObservableProperty]
        private string processDurationText = "00:00:00.000";

        public string ConnectionStatusText => IsConnected ? "已连接" : "未连接";
        public Brush ConnectionStatusColor => IsConnected ? Brushes.LimeGreen : Brushes.Red;

        #endregion

        #region 构造与初始化

        public DashboardViewModel()
        {
            _configService = new SensorConfigService();

            // 1. 加载配置
            LoadSensorConfigs();

            // 2. 初始化视图筛选和分组
            _filteredSensorsView = (CollectionView)CollectionViewSource.GetDefaultView(Sensors);
            _filteredSensorsView.Filter = FilterSensor;
            SetupGrouping();

            // 3. 启动定时器
            StartTimers();

            // 4. 初始化通信
            InitializeUdp();
        }

        private void SetupGrouping()
        {
            // 单位映射字典
            var unitToCategoryMap = new Dictionary<string, string>
            {
                { "MPa", "压力" }, { "kPa", "压力" }, { "bar", "压力" }, { "Pa", "压力" },
                { "℃", "温度" }, { "°C", "温度" }, { "K", "温度" },
                { "RPM", "转速" }, { "r/min", "转速" },
                { "%", "百分比" },
                { "V", "电压&电流" }, { "A", "电压&电流" },
                { "Nm", "扭矩" },
                { "L/min", "流量" },
                { "m/s", "速度" },
                { "mm", "位移" },
                { "g", "振动" },
            };

            _filteredSensorsView.GroupDescriptions.Clear();
            _filteredSensorsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(SensorDisplay.Unit), new UnitToCategoryConverter(unitToCategoryMap)));
        }

        private void StartTimers()
        {
            // 系统时间 (1秒刷新一次)
            _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clockTimer.Tick += (_, _) => CurrentTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            _clockTimer.Start();

            // 秒表UI刷新 (50毫秒刷新一次，仅用于显示，不影响计时精度)
            _uiRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _uiRefreshTimer.Tick += (s, e) =>
            {
                if (_processStopwatch.IsRunning)
                {
                    ProcessDurationText = _processStopwatch.Elapsed.ToString(@"hh\:mm\:ss\.fff");
                }
            };
        }

        #endregion

        #region 命令 (Commands)

        /// <summary>
        /// 重置计时器
        /// </summary>
        [RelayCommand]
        private void ResetTimer()
        {
            _processStopwatch.Reset();
            ProcessDurationText = "00:00:00.000";
            // 如果需要重置信号触发状态，可以取消注释下面这行
            // _isSignalActive = false; 
        }

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

                // 读取配置
                string iniPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.ini");
                IniConfigHelper.FilePath = iniPath;

                // 配置通用UDP
                string localIp = IniConfigHelper.ReadIniData("Engine", "Local", "0.0.0.0");
                string multicastIp = IniConfigHelper.ReadIniData("Engine", "IP", "239.0.0.1");
                int port = int.Parse(IniConfigHelper.ReadIniData("Engine", "PORT", "12345"));

                // 配置特殊UDP (Gase)
                string gaseLocalIp = IniConfigHelper.ReadIniData("Gase", "Local", "0.0.0.0");
                string gaseMulticastIp = IniConfigHelper.ReadIniData("Gase", "IP", "239.0.0.1");
                int gasePort = int.Parse(IniConfigHelper.ReadIniData("Gase", "PORT", "12345"));
                int[] gaseHeader = { 246, 10, 26, 8, 1, 216, 0 };

                // 启动服务
                _udpService.Initialize(localIp, multicastIp, port);
                _udpService.DataReceived += OnGeneralUdpDataReceived;

                _udpServiceGase.Initialize(gaseLocalIp, gaseMulticastIp, gasePort, gaseHeader);
                _udpServiceGase.StructuredDataReceived += OnGaseUdpDataReceived;

                UpdateConnectionStatus();
            }
            catch (Exception ex)
            {
                // 可以添加日志记录
                Debug.WriteLine($"UDP Init Error: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理特殊结构化数据 (Gase)
        /// </summary>
        private void OnGaseUdpDataReceived(object? sender, UDPValues e)
        {
            // 确保在UI线程更新
            Application.Current?.Dispatcher.Invoke(() =>
            {
                foreach (var sensor in Sensors)
                {
                    // 仅处理特殊传感器
                    if (!_specialSensors.Contains(sensor.Name)) continue;

                    ApplySpecialFormula(sensor, e);
                    CheckSensorAbnormal(sensor);
                }

                NotifyDataUpdated();
            }, DispatcherPriority.Render);
        }

        /// <summary>
        /// 处理通用UDP数据
        /// </summary>
        private void OnGeneralUdpDataReceived(object? sender, short[] data)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                foreach (var sensor in Sensors)
                {
                    // 跳过由 Gase UDP 处理的特殊传感器
                    if (_specialSensors.Contains(sensor.Name)) continue;

                    // 越界检查
                    if (sensor.Channel < 0 || sensor.Channel >= data.Length) continue;

                    // 1. 更新数值 (标准转换: /1000f)
                    sensor.RawVoltage = data[sensor.Channel] / 1000f;

                    // 2. 检查异常
                    CheckSensorAbnormal(sensor);

                    // 3. 检查时统信号触发
                    if (sensor.Name == "时统信号")
                    {
                        HandleTimingSignal(sensor.RawVoltage);
                    }
                }

                NotifyDataUpdated();
            }, DispatcherPriority.Render);
        }

        /// <summary>
        /// 应用特殊传感器的计算公式
        /// </summary>
        private void ApplySpecialFormula(SensorDisplay sensor, UDPValues e)
        {
            // 常量系数提取，避免重复计算
            const float factor = 0.000579f;
            const float offset = 4f;

            switch (sensor.Name)
            {
                case "Gpb3":
                    if (e.AI_value.Length > 30) sensor.RawVoltage = e.AI_value[30] * factor + offset;
                    break;
                case "Gpb8":
                    if (e.AI_value.Length > 35) sensor.RawVoltage = e.AI_value[35] * factor + offset;
                    break;
                case "Cpb15":
                    if (e.AI_value.Length > 66) sensor.RawVoltage = e.AI_value[66] * factor + offset;
                    break;
                case "Cpb17":
                    if (e.AI_value.Length > 69) sensor.RawVoltage = e.AI_value[69] * factor + offset;
                    break;
            }
        }

        private void CheckSensorAbnormal(SensorDisplay sensor)
        {
            sensor.IsAbnormal = sensor.RawVoltage < sensor.Min || sensor.RawVoltage > sensor.Max;
        }

        /// <summary>
        /// 处理时统信号触发逻辑
        /// </summary>
        private void HandleTimingSignal(double voltage)
        {
            // 触发条件：电压 < 0.1V
            bool isTriggerActive = voltage < 0.1;

            // 状态发生改变时执行
            if (isTriggerActive != _isSignalActive)
            {
                if (isTriggerActive)
                {
                    // 信号生效：开始计时
                    _processStopwatch.Restart();
                    _uiRefreshTimer?.Start();
                }
                else
                {
                    // 信号结束：停止计时
                    _processStopwatch.Stop();
                    _uiRefreshTimer?.Stop();
                    // 补一次最终时间更新
                    ProcessDurationText = _processStopwatch.Elapsed.ToString(@"hh\:mm\:ss\.fff");
                }

                _isSignalActive = isTriggerActive;
            }
        }

        private void NotifyDataUpdated()
        {
            DataUpdated?.Invoke(this, EventArgs.Empty);
            UpdateConnectionStatus();
        }

        private void UpdateConnectionStatus()
        {
            // 只要任意一个服务连接，就视为连接成功
            bool connected = (_udpService?.IsConnected ?? false) || (_udpServiceGase?.IsConnected ?? false);

            // 避免频繁触发属性通知
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

            // 1. 加载通用配置
            var configs = _configService.LoadConfigs("发动机");
            foreach (var cfg in configs)
            {
                if (cfg.Name == "备用") continue;
                Sensors.Add(MapConfigToSensor(cfg));
            }

            // 2. 添加硬编码的特殊传感器 (Gase相关)
            // 这些传感器使用不同的计算公式 (K, B) 和 Min/Max
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
                RawVoltage = 0
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

            // 停止所有定时器
            _clockTimer?.Stop();
            _uiRefreshTimer?.Stop();
            _processStopwatch.Stop();

            // 停止 UDP 服务
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