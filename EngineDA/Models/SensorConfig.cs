using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Media;

namespace EngineDA.Models;

public partial class SensorConfig : ObservableObject
{
    private string? name;
    public string? Name { get =>  name; set => SetProperty(ref  name, value); }

    private int  channel;
    public int Channel { get =>  channel; set => SetProperty(ref  channel, value); }

    private double  k;
    public double K { get =>  k; set => SetProperty(ref  k, value); }

    private double  b;
    public double B { get =>  b; set => SetProperty(ref  b, value); }

    private string?  unit;
    public string? Unit { get =>  unit; set => SetProperty(ref  unit, value); }

    private string? color;
    public string? Color { get => color; set => SetProperty(ref color, value); }

    private bool isImportant;
    public bool IsImportant { get => isImportant; set => SetProperty(ref isImportant, value); }

    private string? channelName;
    public string? ChannelName { get => channelName; set => SetProperty(ref channelName, value); }
}