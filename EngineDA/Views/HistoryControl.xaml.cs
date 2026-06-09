using EngineDA.Models;
using EngineDA.Services;
using Microsoft.Win32;
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
        private ChannelData[] _channelData;
        private List<SensorConfig> _sensorConfigs = new List<SensorConfig>();
        private ScottPlot.Plottables.VerticalLine _timeLine;
        private ObservableCollection<CursorItem> _cursorItems = new ObservableCollection<CursorItem>();

        public HistoryControl()
        {
            InitializeComponent();
            CursorDataList.ItemsSource = _cursorItems;
            AutoLoadSystemConfigs();
        }

        private double GetStartTime()
        {
            if (double.TryParse(TxtStartTime.Text, out double val)) return val;
            return 0.0;
        }

        private void TxtStartTime_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_channelData != null && SensorListBox.SelectedItems.Count > 0)
            {
                SensorListBox_SelectionChanged(null, null);
            }
        }

        private void AutoLoadSystemConfigs()
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
                            _sensorConfigs.Add(config);
                        }
                    }
                }

                _sensorConfigs = _sensorConfigs.OrderBy(c => c.Channel).ToList();
                SensorListBox.ItemsSource = _sensorConfigs;
                TxtStatus.Text = $"系统配置就绪。\n共加载 {_sensorConfigs.Count} 个传感器。";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"读取配置失败: {ex.Message}", "错误");
            }
        }

        private async void LoadDatFiles_Click(object sender, RoutedEventArgs e)
        {
            if (_sensorConfigs == null || _sensorConfigs.Count == 0) return;

            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Multiselect = true,
                Filter = "Dat files (*.dat)|*.dat",
                Title = "请全选该批次的 4 个原始数据文件"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                var filePaths = openFileDialog.FileNames;
                string fileBid0 = filePaths.FirstOrDefault(f => f.Contains("BID#0"));
                string fileBid1 = filePaths.FirstOrDefault(f => f.Contains("BID#1"));
                string fileBid2 = filePaths.FirstOrDefault(f => f.Contains("BID#2"));
                string fileBid3 = filePaths.FirstOrDefault(f => f.Contains("BID#3"));

                if (fileBid0 == null || fileBid1 == null || fileBid2 == null || fileBid3 == null)
                {
                    MessageBox.Show("未找齐包含 BID#0 至 BID#3 的文件！", "错误");
                    return;
                }

                string[] orderedFiles = { fileBid0, fileBid1, fileBid2, fileBid3 };

                TxtStatus.Text = "准备解析...";
                ParseProgressBar.Value = 0;
                ParseProgressBar.Visibility = Visibility.Visible;
                SensorListBox.IsEnabled = false;

                var progress = new Progress<int>(percent =>
                {
                    ParseProgressBar.Value = percent;
                    if (percent <= 60)
                    {
                        TxtStatus.Text = $"正在读取与解析原始文件...";
                    }
                    else if (percent < 100)
                    {
                        TxtStatus.Text = $"正在对数据进行处理...";
                    }
                    else
                    {
                        TxtStatus.Text = "数据处理完成，正在渲染图表...";
                    }
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
                    MessageBox.Show($"解析数据失败: {ex.Message}", "错误");
                    TxtStatus.Text = "解析失败！";
                }
                finally
                {
                    await Task.Delay(500); 
                    ParseProgressBar.Visibility = Visibility.Collapsed;
                    SensorListBox.IsEnabled = true;
                }
            }
        }

        private void ParseAndMergeHighFrequencyData(string[] orderedFiles, IProgress<int> progress)
        {
            progress.Report(1);
            ParsedFile data0 = ExtractAllNumbersFast(orderedFiles[0], progress, 1, 15);
            ParsedFile data1 = ExtractAllNumbersFast(orderedFiles[1], progress, 15, 30);
            ParsedFile data2 = ExtractAllNumbersFast(orderedFiles[2], progress, 30, 45);
            ParsedFile data3 = ExtractAllNumbersFast(orderedFiles[3], progress, 45, 60);

            _channelData = new ChannelData[224];

            ProcessCardData(data0, 32, 0, 20000.0, progress, 60, 70); // 20kHz
            ProcessCardData(data1, 64, 32, 1000.0, progress, 70, 80); // 1kHz
            ProcessCardData(data2, 64, 96, 1000.0, progress, 80, 90); // 1kHz
            ProcessCardData(data3, 64, 160, 1000.0, progress, 90, 100); // 1kHz

            Application.Current.Dispatcher.Invoke(() =>
            {
                HistoryPlot.Plot.Clear();
                ConfigureChartStyle();
                HistoryPlot.Refresh();
            });
        }

        private void ProcessCardData(ParsedFile data, int numChannels, int startChannelOffset, double sampleRate, IProgress<int> progress, int startPercent, int endPercent)
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

                _channelData[startChannelOffset + ch] = new ChannelData
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
                string line;
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

        private void SensorListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_channelData == null) return;

            HistoryPlot.Plot.Clear();
            ConfigureChartStyle();

            double currentStartTime = GetStartTime();
            double maxTimeForSlider = currentStartTime;

            foreach (SensorConfig config in SensorListBox.SelectedItems)
            {
                if (config.Channel >= 0 && config.Channel < 224 && _channelData[config.Channel] != null)
                {
                    var chData = _channelData[config.Channel];
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
                    sig.Label = $"{config.Name}{unitStr} [CH:{config.Channel}]";

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
            if (_channelData == null || SensorListBox.SelectedItems.Count == 0 || _timeLine == null) return;

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
                    if (config.Channel >= 0 && config.Channel < 224 && _channelData[config.Channel] != null)
                    {
                        var chData = _channelData[config.Channel];
                        int xIndex = (int)Math.Round(timeFromStart * chData.SampleRate);

                        if (xIndex >= 0 && xIndex < chData.Values.Length)
                        {
                            double physicalVal = chData.Values[xIndex] * config.K + config.B;

                            _cursorItems.Add(new CursorItem
                            {
                                Name = config.Name,
                                ChannelInfo = $"[CH:{config.Channel}]",
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