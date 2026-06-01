using CommunityToolkit.Mvvm.ComponentModel;

namespace EngineDA.Models;

public partial class SensorConfig : ObservableObject
{
    private string? _name;
    public string? Name { get => _name; set => SetProperty(ref _name, value); }

    private int _channel;
    public int Channel { get => _channel; set => SetProperty(ref _channel, value); }

    private double _k;
    public double K { get => _k; set => SetProperty(ref _k, value); }

    private double _b;
    public double B { get => _b; set => SetProperty(ref _b, value); }

    private string? _unit;
    public string? Unit { get => _unit; set => SetProperty(ref _unit, value); }

    private double _min;
    public double Min { get => _min; set => SetProperty(ref _min, value); }

    private double _max;
    public double Max { get => _max; set => SetProperty(ref _max, value); }
}