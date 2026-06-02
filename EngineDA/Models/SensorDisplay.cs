using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Windows.Media;

namespace EngineDA.Models
{
    public partial class SensorDisplay : ObservableObject
    {
        [ObservableProperty] private string name = "";
        [ObservableProperty] private int channel;

        private string unit = "";
        public string Unit
        {
            get => unit;
            set
            {
                SetProperty(ref unit, value);
                OnPropertyChanged(nameof(DisplayGroup));
                OnPropertyChanged(nameof(CardWidth));
                OnPropertyChanged(nameof(CardHeight));
            }
        }

        public double CardWidth
        {
            get
            {
                string u = Unit?.Trim() ?? "";

                if (u == "RPM" || u == "r/min") return 290;

                if (u == "℃" || u == "°C" || u == "K" || u == "L/min") return 290;

                return 230;
            }
        }

        public double CardHeight
        {
            get
            {
                string u = Unit?.Trim() ?? "";
                if (u == "RPM" || u == "r/min") return 150;
                if (u == "℃" || u == "°C" || u == "K" || u == "L/min") return 150;
                return 135;
            }
        }

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

                return "📁 其他数据";
            }
        }

        private double rawVoltage;
        public double RawVoltage
        {
            get => rawVoltage;
            set
            {
                if (Math.Abs(rawVoltage - value) <= 0.0001) return;
                SetProperty(ref rawVoltage, value);
                OnPropertyChanged(nameof(Value));
                OnValueChanged();
            }
        }

        public double Value => Kvalue * RawVoltage + Bvalue;

        public string DisplayValue
        {
            get
            {
                string u = Unit?.Trim() ?? "";
                if (u == "RPM" || u == "r/min")
                {
                    return Math.Truncate(Value).ToString("#,##0");
                }
                double truncatedValue = Math.Truncate(Value * 100.0) / 100.0;
                return truncatedValue.ToString("0.00");
            }
        }

        public event EventHandler? ValueChanged;

        protected virtual void OnValueChanged()
        {
            ValueChanged?.Invoke(this, EventArgs.Empty);
            OnPropertyChanged(nameof(DisplayValue));
        }

        private bool isAbnormal;
        public bool IsAbnormal
        {
            get => isAbnormal;
            set => SetProperty(ref isAbnormal, value);
        }
    }
}