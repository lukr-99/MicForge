using System;
using MicForge.Audio;

namespace MicForge.ViewModels;

/// <summary>Equalizer stage that also exposes the band data for the graph view.</summary>
public sealed class EqStageViewModel : StageViewModel
{
    public EqStageViewModel(ParametricEq eq, DspChain chain, double sampleRate, string accent,
        Func<bool> getEnabled, Action<bool> setEnabled)
        : base("Equalizer", accent, getEnabled, setEnabled)
    {
        Eq = eq;
        Chain = chain;
        SampleRate = sampleRate;
    }

    public override bool IsEq => true;

    public ParametricEq Eq { get; }
    public DspChain Chain { get; }
    public double SampleRate { get; }
    public double GainRange => 18;

    // Per-band draggable frequency limits on the graph (matches the slider ranges).
    public double[] FreqMin { get; } = { 40, 100, 500, 2000, 2000 };
    public double[] FreqMax { get; } = { 500, 1500, 6000, 16000, 16000 };

    /// <summary>Refresh the slider readouts after the graph changed a band.</summary>
    public void NotifyParamsChanged()
    {
        foreach (var p in Params) p.NotifyChanged();
    }
}
