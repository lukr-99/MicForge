using System;

namespace MicForge.Audio;

/// <summary>Simple wide-band gain stage (used for input trim and output/makeup).</summary>
public sealed class GainStage : AudioProcessorBase
{
    private double _gain = 1.0;

    public GainStage(string name) { Name = name; Enabled = true; }

    public override string Name { get; }

    public double GainDb
    {
        get => 20 * Math.Log10(_gain);
        set => _gain = DspMath.ToLinear(value);
    }

    protected override void ProcessBlock(float[] buffer, int offset, int count)
    {
        if (Math.Abs(_gain - 1.0) < 1e-9) return;
        for (int i = offset; i < offset + count; i++)
            buffer[i] = (float)(buffer[i] * _gain);
    }
}
