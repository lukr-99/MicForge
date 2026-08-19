using System;

namespace MicForge.Audio;

/// <summary>Simple wide-band gain stage (used for input trim and output/makeup).</summary>
public sealed class GainStage : IAudioProcessor
{
    private double _gain = 1.0;

    public GainStage(string name) => Name = name;

    public string Name { get; }
    public bool Enabled { get; set; } = true;
    public double GainDb
    {
        get => 20 * Math.Log10(_gain);
        set => _gain = Math.Pow(10, value / 20.0);
    }

    public void Process(float[] buffer, int offset, int count)
    {
        if (!Enabled || Math.Abs(_gain - 1.0) < 1e-9) return;
        for (int i = offset; i < offset + count; i++)
            buffer[i] = (float)(buffer[i] * _gain);
    }

    public void Reset() { }
}
