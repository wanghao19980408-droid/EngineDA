using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Windows.Media;

namespace EngineDA.Models
{
    public partial class SensorDisplay : ObservableObject
    {
        [ObservableProperty] private string name = "";
        [ObservableProperty] private int channel;
        [ObservableProperty] private string unit = "";
        [ObservableProperty] private double min;
        [ObservableProperty] private double max;
        [ObservableProperty] private double kvalue;
        [ObservableProperty] private double bvalue;
        [ObservableProperty] private SolidColorBrush? color;

        private double _rawVoltage;
        public double RawVoltage
        {
            get => _rawVoltage;
            set
            {
                if (Math.Abs(_rawVoltage - value) <= 0.0001)
                    return;

                SetProperty(ref _rawVoltage, value);
                OnPropertyChanged(nameof(Value));
                OnValueChanged();
            }
        }

        public double Value => Kvalue * RawVoltage + Bvalue;

        public event EventHandler? ValueChanged;

        protected virtual void OnValueChanged()
        {
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
          
        private bool _isAbnormal;
        public bool IsAbnormal
        {
            get => _isAbnormal;
            set => SetProperty(ref _isAbnormal, value);
        }
    }
}
