using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;

namespace EngineDA.Models
{
    public partial class SensorDisplay : ObservableObject
    {
        public static double DeadZone = 0.0;

        [ObservableProperty] private string name = "";
        [ObservableProperty] private int channel;
        [ObservableProperty] private int orderIndex = 0;

        private string unit = "";
        public string Unit
        {
            get => unit;
            set
            {
                SetProperty(ref unit, value);
                OnPropertyChanged(nameof(DisplayGroup));
                OnPropertyChanged(nameof(IsTemperatureSensor));
                OnPropertyChanged(nameof(CurrentUnit));
            }
        }

        public bool IsTemperatureSensor
        {
            get
            {
                string u = Unit?.Trim() ?? "";
                return u == "℃" || u == "°C" || u == "K";
            }
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CurrentUnit))]
        [NotifyPropertyChangedFor(nameof(DisplayValue))]
        [NotifyPropertyChangedFor(nameof(Value))]
        private bool showAsKelvin;

        public string CurrentUnit
        {
            get
            {
                if (!IsTemperatureSensor) return Unit;
                return ShowAsKelvin ? "K" : "℃";
            }
        }

        [RelayCommand]
        public void ToggleUnit()
        {
            if (IsTemperatureSensor)
            {
                ShowAsKelvin = !ShowAsKelvin;
            }
        }

        private double CalculatePhysicalValue(double raw)
        {
            double val = Kvalue * raw + Bvalue;
            if (IsTemperatureSensor)
            {
                string u = Unit?.Trim() ?? "";
                bool isOriginallyKelvin = u == "K";

                if (isOriginallyKelvin && !ShowAsKelvin)
                    val -= 273.15;
                else if (!isOriginallyKelvin && ShowAsKelvin)
                    val += 273.15;
            }
            return val;
        }

        public double CardWidth => 255;
        public double CardHeight => 165;

        [ObservableProperty] private double min;
        [ObservableProperty] private double max;
        [ObservableProperty] private double kvalue;
        [ObservableProperty] private double bvalue;
        [ObservableProperty] private string? color;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(DisplayGroup))]
        private bool isImportant;

        public string DisplayGroup
        {
            get
            {
                if (IsImportant) return "⭐ 重要关注";

                string u = Unit?.Trim() ?? "";
                if (u == "MPa" || u == "kPa" || u == "bar" || u == "Pa") return "🌡️ 压力";
                if (u == "℃" || u == "°C" || u == "K") return "🔥 温度";
                if (u == "RPM" || u == "r/min") return "⚙️ 转速";
                if (u == "%") return "📊 百分比";
                if (u == "V" || u == "A") return "⚡ 电压/电流";
                if (u == "Nm") return "💪 扭矩";
                if (u == "L/min") return "🌊 流量";
                if (u == "m/s") return "🚀 速度";
                if (u == "mm") return "📏 位移";
                if (u == "g") return "📳 振动";
                if (u == "KN") return "🗡 推力";
                if (u == "L/S") return "🐺 流量";
                return "📁 其他数据";
            }
        }

        private const int FilterWindowSize = 30;
        private const int TrimCount = 6;        

        private readonly Queue<double> _buffer = new();
        private double _cachedValue;

        private double rawVoltage;
        public double RawVoltage
        {
            get => rawVoltage;
            set
            {
                rawVoltage = value;

                _buffer.Enqueue(value);
                if (_buffer.Count > FilterWindowSize)
                {
                    _buffer.Dequeue();
                }

                RefreshCachedValue();
            }
        }

        public double Value => _cachedValue;

        private void RefreshCachedValue()
        {
            double filteredRaw = ComputeFilteredRawValue();

            double newPhysicalValue = CalculatePhysicalValue(filteredRaw);

            if (Math.Abs(_cachedValue - newPhysicalValue) > DeadZone || _buffer.Count <= 1)
            {
                _cachedValue = newPhysicalValue;

                OnPropertyChanged(nameof(RawVoltage));
                OnPropertyChanged(nameof(Value));
                OnPropertyChanged(nameof(DisplayValue));
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private double ComputeFilteredRawValue()
        {
            if (_buffer.Count == 0) return rawVoltage;

            var sorted = _buffer.OrderBy(v => v).ToList();
            int trim = Math.Min(TrimCount, sorted.Count / 3);
            var mid = sorted.Skip(trim).Take(sorted.Count - trim * 2).ToList();

            return mid.Count > 0 ? mid.Average() : sorted.Average();
        }

        public string DisplayValue
        {
            get
            {
                double displayPhysicalValue = Value;

                string u = Unit?.Trim() ?? "";
                if (u == "RPM" || u == "r/min")
                {
                    return Math.Truncate(displayPhysicalValue).ToString("#,##0");
                }

                double roundedValue = Math.Round(displayPhysicalValue, 2, MidpointRounding.AwayFromZero);
                return roundedValue.ToString("0.00");
            }
        }

        public event EventHandler? ValueChanged;

        private bool isAbnormal;
        public bool IsAbnormal
        {
            get => isAbnormal;
            set => SetProperty(ref isAbnormal, value);
        }
    }
}