using CommunityToolkit.Mvvm.Messaging;
using EngineDA.Helpers;
using EngineDA.Models;
using EngineDA.Services;
using ScottPlot;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace EngineDA.Views
{
    public class ChannelData
    {
        public double[]? Values { get; set; }
        public double SampleRate { get; set; }
        public double Period { get { return 1.0 / SampleRate; } }
    }

    public class ParsedFile
    {
        public List<double> Values = new List<double>();
    }

    public class CursorItem
    {
        public string Name { get; set; } = "";
        public string ChannelInfo { get; set; } = "";
        public string Value { get; set; } = "";
    }

    public partial class HistoryControl : UserControl
    {
        private Dictionary<string, ChannelData> Datas = new Dictionary<string, ChannelData>();

        // 核心修复1：使用 ObservableCollection，确保数据增删时界面自动刷新
        private ObservableCollection<SensorConfig> _sensorConfigs = new ObservableCollection<SensorConfig>();
        private ScottPlot.Plottables.VerticalLine? _timeLine;
        private ObservableCollection<CursorItem> _cursorItems = new ObservableCollection<CursorItem>();

        private bool enableIpc1;
        private bool enableIpc2;

        public HistoryControl()
        {
            InitializeComponent();

            // 核心修复2：数据源在构造函数中只绑定一次
            CursorDataList.ItemsSource = _cursorItems;
            SensorListBox.ItemsSource = _sensorConfigs;

            AutoLoadSystemConfigs();

            // 核心修复3：监听保存配置的广播消息，强制刷新当前历史曲线界面
            WeakReferenceMessenger.Default.Register<HistoryControl, ConfigReloadMessage>(this, (r, m) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    r.AutoLoadSystemConfigs();

                    // 清空旧数据与图表，防止界面残留报错
                    r.Datas.Clear();
                    r._cursorItems.Clear();
                    r.HistoryPlot.Plot.Clear();
                    r.ConfigureChartStyle();
                    r.HistoryPlot.Refresh();

                    if (r.TimeSlider != null)
                    {
                        r.TimeSlider.Value = 0;
                    }
                });
            });
        }

        private double GetStartTime()
        {
            if (double.TryParse(TxtStartTime.Text, out double val)) return val;
            return 0.0;
        }

        private void TxtStartTime_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (Datas.Count > 0 && SensorListBox.SelectedItems.Count > 0)
            {
                SensorListBox_SelectionChanged(this, null!);
            }
        }

        public void AutoLoadSystemConfigs()
        {
            try
            {
                string iniPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.ini");

                // 加入 .Trim() 防止 INI 读取时带有不可见空格
                enableIpc1 = IniConfigHelper.ReadIniData("IPC1", "Enable", "True", iniPath).Trim().Equals("True", StringComparison.OrdinalIgnoreCase);
                enableIpc2 = IniConfigHelper.ReadIniData("IPC2", "Enable", "False", iniPath).Trim().Equals("True", StringComparison.OrdinalIgnoreCase);

                var configService = new SensorConfigService();
                var sheetNames = configService.GetSheetNames();

                // 触发 UI 列表清空
                _sensorConfigs.Clear();

                foreach (var sheetName in sheetNames)
                {
                    // 拦截未启用的工控机
                    if (sheetName == "工控机1" && !enableIpc1) continue;
                    if (sheetName == "工控机2" && !enableIpc2) continue;

                    var configs = configService.LoadConfigs(sheetName);
                    foreach (var config in configs)
                    {
                        if (!string.IsNullOrWhiteSpace(config.Name) && !config.Name.Contains("备用"))
                        {
                            config.ChannelName = $"{sheetName} - CH{config.Channel}";
                            // 添加新数据，自动通知 UI 生成列表项
                            _sensorConfigs.Add(config);
                        }
                    }
                }

                if (TxtStatus != null)
                {
                    TxtStatus.Text = $"系统配置就绪。\n共加载 {_sensorConfigs.Count} 个传感器。";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"读取配置失败: {ex.Message}", "错误");
            }
        }

        private void LoadDatFiles_Click(object sender, RoutedEventArgs e)
        {
            if (_sensorConfigs == null || _sensorConfigs.Count == 0)
            {
                MessageBox.Show("请先加载传感器配置文件 (Excel)！", "提示");
                return;
            }

            var folderDialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "请选择包含“工控机1”和“工控机2”数据文件夹的总目录"
            };

            if (folderDialog.ShowDialog() == true)
            {
                string selectedPath = folderDialog.FolderName;
                string[] allDatFiles = Directory.GetFiles(selectedPath, "*.dat", SearchOption.AllDirectories);
                _ = ProcessDataFilesAsync(allDatFiles);
            }
        }

        private async Task ProcessDataFilesAsync(string[] filePaths)
        {
            var targetFiles = filePaths.Where(f => f.Contains("BID#")).ToList();

            if (targetFiles.Count != 8)
            {
                MessageBox.Show($"文件数量异常！\n预期需要 8 个数据文件，但实际在目录下找到了 {targetFiles.Count} 个。\n请确保所选路径下仅包含“工控机1”和“工控机2”两套数据。", "文件数量错误");
                return;
            }

            var groupedByFolder = targetFiles.GroupBy(f => Path.GetDirectoryName(f))
                                             .OrderBy(g => g.Key)
                                             .ToList();

            if (groupedByFolder.Count != 2)
            {
                MessageBox.Show($"文件夹结构错误！\n预期数据应分别放在 2 个文件夹中，但当前文件分布在 {groupedByFolder.Count} 个文件夹里。", "目录结构错误");
                return;
            }

            var ipc1Files = groupedByFolder[0].ToList();
            var ipc2Files = groupedByFolder[1].ToList();

            string[] orderedFiles = new string[8];

            orderedFiles[0] = ipc1Files.FirstOrDefault(f => f.Contains("BID#0"))!;
            orderedFiles[1] = ipc1Files.FirstOrDefault(f => f.Contains("BID#1"))!;
            orderedFiles[2] = ipc1Files.FirstOrDefault(f => f.Contains("BID#2"))!;
            orderedFiles[3] = ipc1Files.FirstOrDefault(f => f.Contains("BID#3"))!;
            orderedFiles[4] = ipc2Files.FirstOrDefault(f => f.Contains("BID#0"))!;
            orderedFiles[5] = ipc2Files.FirstOrDefault(f => f.Contains("BID#1"))!;
            orderedFiles[6] = ipc2Files.FirstOrDefault(f => f.Contains("BID#2"))!;
            orderedFiles[7] = ipc2Files.FirstOrDefault(f => f.Contains("BID#3"))!;

            if (orderedFiles.Any(f => string.IsNullOrEmpty(f)))
            {
                MessageBox.Show("文件匹配失败！\n请检查两台工控机的文件夹内，是否都完整包含了 BID#0 到 BID#3。", "文件缺失");
                return;
            }

            TxtStatus.Text = "准备解析...";
            ParseProgressBar.Value = 0;
            ParseProgressBar.Visibility = Visibility.Visible;
            SensorListBox.IsEnabled = false;

            var progress = new Progress<int>(percent =>
            {
                ParseProgressBar.Value = percent;
                TxtStatus.Text = $"正在解析与合并数据: {percent}%";
            });

            try
            {
                await Task.Run(() =>
                {
                    System.Threading.Thread.CurrentThread.Priority = System.Threading.ThreadPriority.Lowest;
                    ParseAndMergeHighFrequencyData(orderedFiles, progress);
                });
                TxtStatus.Text = "解析与渲染完毕！";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"解析失败: {ex.Message}", "错误");
                TxtStatus.Text = "解析失败";
            }
            finally
            {
                ParseProgressBar.Visibility = Visibility.Collapsed;
                SensorListBox.IsEnabled = true;
            }
        }

        private void ParseAndMergeHighFrequencyData(string[] orderedFiles, IProgress<int> progress)
        {
            progress.Report(1);

            ParsedFile data0 = ExtractAllNumbersFast(orderedFiles[0], progress, 1, 8);
            ParsedFile data1 = ExtractAllNumbersFast(orderedFiles[1], progress, 8, 16);
            ParsedFile data2 = ExtractAllNumbersFast(orderedFiles[2], progress, 16, 24);
            ParsedFile data3 = ExtractAllNumbersFast(orderedFiles[3], progress, 24, 32);
            ParsedFile data4 = ExtractAllNumbersFast(orderedFiles[4], progress, 32, 40);
            ParsedFile data5 = ExtractAllNumbersFast(orderedFiles[5], progress, 40, 48);
            ParsedFile data6 = ExtractAllNumbersFast(orderedFiles[6], progress, 48, 56);
            ParsedFile data7 = ExtractAllNumbersFast(orderedFiles[7], progress, 56, 60);

            Datas.Clear();

            ProcessCardData(data0, 32, 0, 20000.0, progress, 60, 65, "工控机1");
            ProcessCardData(data1, 32, 32, 1000.0, progress, 65, 70, "工控机1");
            ProcessCardData(data2, 32, 64, 1000.0, progress, 70, 75, "工控机1");
            ProcessCardData(data3, 32, 96, 1000.0, progress, 75, 80, "工控机1");

            ProcessCardData(data4, 32, 0, 1000.0, progress, 80, 85, "工控机2");
            ProcessCardData(data5, 32, 32, 1000.0, progress, 85, 90, "工控机2");
            ProcessCardData(data6, 32, 64, 1000.0, progress, 90, 95, "工控机2");
            ProcessCardData(data7, 32, 96, 1000.0, progress, 95, 100, "工控机2");

            Application.Current.Dispatcher.Invoke(() =>
            {
                HistoryPlot.Plot.Clear();
                ConfigureChartStyle();
                HistoryPlot.Refresh();
            });
        }

        private void ProcessCardData(ParsedFile data, int numChannels, int startChannelOffset, double sampleRate, IProgress<int> progress, int startPercent, int endPercent, string device)
        {
            if (data.Values.Count == 0) { progress.Report(endPercent); return; }
            int count = data.Values.Count / numChannels;
            if (count == 0) { progress.Report(endPercent); return; }

            string iniPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config.ini");
            bool isFilterEnabled = EngineDA.Helpers.IniConfigHelper.ReadIniData("HistoryFilter", "Enabled", "False", iniPath).Equals("True", StringComparison.OrdinalIgnoreCase);

            int windowSize20k = int.Parse(EngineDA.Helpers.IniConfigHelper.ReadIniData("HistoryFilter", "WindowSize20k", "1000", iniPath));
            int windowSize1k = int.Parse(EngineDA.Helpers.IniConfigHelper.ReadIniData("HistoryFilter", "WindowSize1k", "500", iniPath));

            int completedChannels = 0;
            object lockObj = new object();

            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount > 4 ? Environment.ProcessorCount - 3 : 1)
            };

            System.Threading.Tasks.Parallel.For(0, numChannels, parallelOptions, ch =>
            {
                double[] vals = new double[count];
                for (int i = 0; i < count; i++)
                {
                    vals[i] = data.Values[i * numChannels + ch];
                }

                if (isFilterEnabled)
                {
                    int actualWindowSize = (sampleRate <= 1000) ? windowSize1k : windowSize20k;
                    if (actualWindowSize < 3) actualWindowSize = 3;
                    vals = ApplyHampelFilter(vals, actualWindowSize);
                }

                string key = $"{device} - CH{startChannelOffset + ch}";

                Datas[key] = new ChannelData
                {
                    Values = vals,
                    SampleRate = sampleRate
                };

                lock (lockObj)
                {
                    completedChannels++;
                    int currentPercent = startPercent + (completedChannels * (endPercent - startPercent) / numChannels);
                    progress.Report(currentPercent);
                }
            });
            progress.Report(endPercent);
        }

        private double[] ApplyHampelFilter(double[] src, int windowSize)
        {
            int n = src.Length;
            if (n == 0) return Array.Empty<double>();

            var dst = new double[n];
            int half = windowSize / 2;
            var win = new double[windowSize + 4];
            var deviations = new double[windowSize + 4];

            double thresholdFactor = 3.0;

            for (int i = 0; i < n; i++)
            {
                int count = 0;
                for (int k = i - half; k < i - half + windowSize; k++)
                {
                    int idx = k < 0 ? -k : (k >= n ? 2 * n - 2 - k : k);
                    win[count++] = src[idx];
                }

                var sortedWin = new double[count];
                Array.Copy(win, sortedWin, count);
                Array.Sort(sortedWin);
                double median = sortedWin[count / 2];

                for (int j = 0; j < count; j++)
                {
                    deviations[j] = Math.Abs(win[j] - median);
                }

                Array.Sort(deviations, 0, count);
                double mad = deviations[count / 2];

                double maxAllowedDeviation = thresholdFactor * 1.4826 * mad;

                if (mad > 1e-6 && Math.Abs(src[i] - median) > maxAllowedDeviation)
                {
                    dst[i] = median;
                }
                else
                {
                    dst[i] = src[i];
                }
            }
            return dst;
        }

        private ParsedFile ExtractAllNumbersFast(string filePath, IProgress<int> progress, int startPercent, int endPercent)
        {
            ParsedFile pf = new ParsedFile();
            long totalLength = new FileInfo(filePath).Length;

            using (StreamReader sr = new StreamReader(filePath))
            {
                string? line;
                long bytesRead = 0;
                int lastReported = startPercent;

                while ((line = sr.ReadLine()) != null)
                {
                    bytesRead += line.Length + 2;
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var parts = line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var part in parts)
                    {
                        if (part.StartsWith("[")) continue;
                        if (double.TryParse(part, out double val)) pf.Values.Add(val);
                    }

                    if (totalLength > 0)
                    {
                        int currentPercent = startPercent + (int)(bytesRead * (endPercent - startPercent) / totalLength);
                        if (currentPercent > lastReported && currentPercent <= endPercent)
                        {
                            progress.Report(currentPercent);
                            lastReported = currentPercent;
                        }
                    }
                }
            }
            progress.Report(endPercent);
            return pf;
        }

        private void SensorListBox_SelectionChanged(object? sender, SelectionChangedEventArgs? e)
        {
            if (Datas.Count == 0) return;
            _cursorItems.Clear();
            HistoryPlot.Plot.Clear();
            ConfigureChartStyle();

            double currentStartTime = GetStartTime();
            double maxTimeForSlider = currentStartTime;

            foreach (SensorConfig config in SensorListBox.SelectedItems)
            {
                if (Datas.TryGetValue(config.ChannelName!, out var chData) && chData.Values != null)
                {
                    int n = chData.Values.Length;
                    double[] ys = new double[n];
                    for (int i = 0; i < n; i++)
                    {
                        ys[i] = chData.Values[i] * config.K + config.B;
                    }

                    var sig = HistoryPlot.Plot.Add.Signal(ys);
                    sig.Data.Period = chData.Period;
                    sig.Data.XOffset = currentStartTime;

                    string unitStr = string.IsNullOrEmpty(config.Unit) ? "" : $" ({config.Unit})";
                    sig.LegendText = $"{config.Name}{unitStr} [{config.ChannelName}]";

                    // 取第一个物理值显示在光标数据栏，修复之前的字符串格式化错误
                    double initialPhysicalVal = ys.Length > 0 ? ys[0] : 0;

                    _cursorItems.Add(new CursorItem
                    {
                        Name = config.Name ?? "未知",
                        ChannelInfo = $"[{config.ChannelName}]",
                        Value = $"{initialPhysicalVal:F3} {config.Unit}"
                    });

                    double currentChannelMaxTime = currentStartTime + (n - 1) * chData.Period;
                    if (currentChannelMaxTime > maxTimeForSlider)
                    {
                        maxTimeForSlider = currentChannelMaxTime;
                    }
                }
            }

            _timeLine = HistoryPlot.Plot.Add.VerticalLine(currentStartTime);
            _timeLine.LineColor = ScottPlot.Colors.Red;
            _timeLine.LineWidth = 1.5f;

            TimeSlider.Minimum = currentStartTime;
            TimeSlider.Maximum = maxTimeForSlider == currentStartTime ? currentStartTime + 1 : maxTimeForSlider;
            TimeSlider.Value = currentStartTime;

            HistoryPlot.Plot.ShowLegend();
            HistoryPlot.Plot.Axes.AutoScale();
            HistoryPlot.Refresh();
        }

        private void TimeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Datas.Count == 0 || SensorListBox.SelectedItems.Count == 0 || _timeLine == null) return;

            double xSec = e.NewValue;
            double currentStartTime = GetStartTime();
            double timeFromStart = xSec - currentStartTime;

            _timeLine.X = xSec;

            if (TxtSliderTime != null) TxtSliderTime.Text = $"当前时刻: {xSec:F2} 秒";

            _cursorItems.Clear();

            if (timeFromStart >= 0)
            {
                foreach (SensorConfig config in SensorListBox.SelectedItems)
                {
                    if (Datas.TryGetValue(config.ChannelName!, out var chData) && chData.Values != null)
                    {
                        int xIndex = (int)Math.Round(timeFromStart * chData.SampleRate);

                        if (xIndex >= 0 && xIndex < chData.Values.Length)
                        {
                            double physicalVal = chData.Values[xIndex] * config.K + config.B;

                            _cursorItems.Add(new CursorItem
                            {
                                Name = config.Name ?? "未知",
                                ChannelInfo = $"[{config.ChannelName}]",
                                Value = $"{physicalVal:F3} {config.Unit}"
                            });
                        }
                    }
                }
                HistoryPlot.Refresh();
            }
        }

        private void Grid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.FocusedElement is TextBox) return;

            double step = (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)) ? 0.5 : 0.01;

            if (e.Key == Key.Left)
            {
                TimeSlider.Value = Math.Max(TimeSlider.Minimum, TimeSlider.Value - step);
                e.Handled = true;
            }
            else if (e.Key == Key.Right)
            {
                TimeSlider.Value = Math.Min(TimeSlider.Maximum, TimeSlider.Value + step);
                e.Handled = true;
            }
        }

        private void ConfigureChartStyle()
        {
            string fontName = ScottPlot.Fonts.Detect("汉");
            if (string.IsNullOrEmpty(fontName)) fontName = "Microsoft YaHei";

            HistoryPlot.Plot.Axes.Title.Label.FontName = fontName;
            HistoryPlot.Plot.Axes.Bottom.Label.FontName = fontName;
            HistoryPlot.Plot.Axes.Left.Label.FontName = fontName;
            HistoryPlot.Plot.Axes.Bottom.TickLabelStyle.FontName = fontName;
            HistoryPlot.Plot.Axes.Left.TickLabelStyle.FontName = fontName;
            HistoryPlot.Plot.Legend.FontName = fontName;

            HistoryPlot.Plot.XLabel("时序时间 (秒)");
            HistoryPlot.Plot.YLabel("物理量");
            HistoryPlot.Plot.Title("传感器历史曲线");
        }
    }
}