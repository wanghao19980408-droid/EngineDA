using CommunityToolkit.Mvvm.Messaging;
using EngineDA.Helpers;
using EngineDA.Models;
using EngineDA.Services;
using ScottPlot;
using System;
using System.Collections.Concurrent;
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
        private ConcurrentDictionary<string, ChannelData> Datas = new ConcurrentDictionary<string, ChannelData>();

        private ObservableCollection<SensorConfig> _sensorConfigs = new ObservableCollection<SensorConfig>();
        private ScottPlot.Plottables.VerticalLine? _timeLine;
        private ObservableCollection<CursorItem> _cursorItems = new ObservableCollection<CursorItem>();

        public HistoryControl()
        {
            InitializeComponent();

            CursorDataList.ItemsSource = _cursorItems;
            SensorListBox.ItemsSource = _sensorConfigs;

            AutoLoadSystemConfigs();

            WeakReferenceMessenger.Default.Register<HistoryControl, ConfigReloadMessage>(this, (r, m) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    r.AutoLoadSystemConfigs();

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
            return 0.000;
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
                var configService = new SensorConfigService();
                var sheetNames = configService.GetSheetNames();

                _sensorConfigs.Clear();

                foreach (var sheetName in sheetNames)
                {
                    var configs = configService.LoadConfigs(sheetName);
                    foreach (var config in configs)
                    {
                        if (!string.IsNullOrWhiteSpace(config.Name) && !config.Name.Contains("备用"))
                        {
                            config.ChannelName = $"{sheetName} - CH{config.Channel}";
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

            if (targetFiles.Count == 0)
            {
                MessageBox.Show($"未在目录下找到任何包含 BID# 的数据文件。", "文件缺失");
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

            List<string>[] orderedFiles = new List<string>[8];

            orderedFiles[0] = ipc1Files.Where(f => f.Contains("BID#0")).OrderBy(f => f).ToList();
            orderedFiles[1] = ipc1Files.Where(f => f.Contains("BID#1")).OrderBy(f => f).ToList();
            orderedFiles[2] = ipc1Files.Where(f => f.Contains("BID#2")).OrderBy(f => f).ToList();
            orderedFiles[3] = ipc1Files.Where(f => f.Contains("BID#3")).OrderBy(f => f).ToList();
            orderedFiles[4] = ipc2Files.Where(f => f.Contains("BID#15")).OrderBy(f => f).ToList();
            orderedFiles[5] = ipc2Files.Where(f => f.Contains("BID#14")).OrderBy(f => f).ToList();
            orderedFiles[6] = ipc2Files.Where(f => f.Contains("BID#13")).OrderBy(f => f).ToList();
            orderedFiles[7] = ipc2Files.Where(f => f.Contains("BID#0")).OrderBy(f => f).ToList();

            if (orderedFiles.Any(list => list.Count == 0))
            {
                MessageBox.Show("部分板卡文件缺失！\n请检查两台工控机的文件夹内，是否都完整包含了预期的 BID# 数据文件。", "文件缺失");
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

        private void ParseAndMergeHighFrequencyData(List<string>[] orderedFiles, IProgress<int> progress)
        {
            progress.Report(1);

            ParsedFile data0 = ExtractAllNumbersFastFromFiles(orderedFiles[0], progress, 0, 6);
            ParsedFile data1 = ExtractAllNumbersFastFromFiles(orderedFiles[1], progress, 6, 12);
            ParsedFile data2 = ExtractAllNumbersFastFromFiles(orderedFiles[2], progress, 12, 18);
            ParsedFile data3 = ExtractAllNumbersFastFromFiles(orderedFiles[3], progress, 18, 25);
            ParsedFile data4 = ExtractAllNumbersFastFromFiles(orderedFiles[4], progress, 25, 31);
            ParsedFile data5 = ExtractAllNumbersFastFromFiles(orderedFiles[5], progress, 31, 37);
            ParsedFile data6 = ExtractAllNumbersFastFromFiles(orderedFiles[6], progress, 37, 43);
            ParsedFile data7 = ExtractAllNumbersFastFromFiles(orderedFiles[7], progress, 43, 50);

            Datas.Clear();

            ProcessCardData(data0, 32, 0, 20000.0, progress, 50, 56, "工控机1");
            ProcessCardData(data1, 32, 32, 1000.0, progress, 56, 62, "工控机1");
            ProcessCardData(data2, 32, 64, 1000.0, progress, 62, 68, "工控机1");
            ProcessCardData(data3, 32, 96, 1000.0, progress, 68, 75, "工控机1");

            ProcessCardData(data4, 32, 0, 20000.0, progress, 75, 81, "工控机2");
            ProcessCardData(data5, 32, 32, 1000.0, progress, 81, 87, "工控机2");
            ProcessCardData(data6, 32, 64, 1000.0, progress, 87, 93, "工控机2");
            ProcessCardData(data7, 32, 96, 1000.0, progress, 93, 100, "工控机2");

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
            var sortedWin = new double[windowSize + 4];
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

                Array.Copy(win, sortedWin, count);
                Array.Sort(sortedWin, 0, count);
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

        private ParsedFile ExtractAllNumbersFastFromFiles(List<string> filePaths, IProgress<int> progress, int startPercent, int endPercent)
        {
            ParsedFile pf = new ParsedFile();
            long totalLength = filePaths.Sum(f => new FileInfo(f).Length);
            long bytesRead = 0;
            int lastReported = startPercent;

            foreach (var filePath in filePaths)
            {
                using (StreamReader sr = new StreamReader(filePath))
                {
                    string? line;
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

            if (TxtSliderTime != null) TxtSliderTime.Text = $"当前时刻: {xSec:F3} 秒";

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

            double step = (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)) ? 0.01 : 0.001;

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