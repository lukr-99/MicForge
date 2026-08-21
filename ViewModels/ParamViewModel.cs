using System;

namespace MicForge.ViewModels;

/// <summary>One tunable parameter shown as a labeled slider with a live value readout.</summary>
public sealed class ParamViewModel : ViewModelBase
{
    private readonly Func<double> _get;
    private readonly Action<double> _set;
    private readonly string _format;
    private readonly string _unit;

    public ParamViewModel(string label, double min, double max, double step,
        Func<double> get, Action<double> set, string format = "0.#", string unit = "", string help = "")
    {
        Label = label;
        Min = min;
        Max = max;
        Step = step;
        _get = get;
        _set = set;
        _format = format;
        _unit = unit;
        Help = help;
    }

    public string Label { get; }
    public string Help { get; }
    public double Min { get; }
    public double Max { get; }
    public double Step { get; }
    public string Unit => _unit;

    public double Value
    {
        get => _get();
        set
        {
            _set(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(Display));
        }
    }

    public string Display => _get().ToString(_format) + _unit;

    /// <summary>Raise change notifications when the value was changed elsewhere (e.g. EQ graph).</summary>
    public void NotifyChanged()
    {
        OnPropertyChanged(nameof(Value));
        OnPropertyChanged(nameof(Display));
    }
}
