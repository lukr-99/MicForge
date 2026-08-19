using System.Collections.Generic;

namespace MicForge.Audio;

/// <summary>Multi-band parametric EQ (low shelf + 3 peaks + high shelf by default).</summary>
public sealed class ParametricEq : IAudioProcessor
{
    /// <summary>One EQ band: a filter type/frequency/gain/Q plus the biquad that realises it.</summary>
    public sealed class Band
    {
        public Biquad.FilterType Type;
        public double Freq;
        public double GainDb;
        public double Q;
        public bool Enabled = true;
        internal readonly Biquad Filter = new();

        internal void Update(double sr) => Filter.Set(Type, Freq, sr, Q, GainDb);
    }

    private readonly double _sr;
    public readonly List<Band> Bands = new();

    public ParametricEq(double sampleRate)
    {
        _sr = sampleRate;
        Bands.Add(new Band { Type = Biquad.FilterType.LowShelf, Freq = 120, GainDb = 0, Q = 0.707 });
        Bands.Add(new Band { Type = Biquad.FilterType.Peaking, Freq = 300, GainDb = 0, Q = 1.0 });
        Bands.Add(new Band { Type = Biquad.FilterType.Peaking, Freq = 1800, GainDb = 0, Q = 1.0 });
        Bands.Add(new Band { Type = Biquad.FilterType.Peaking, Freq = 5000, GainDb = 0, Q = 1.0 });
        Bands.Add(new Band { Type = Biquad.FilterType.HighShelf, Freq = 10000, GainDb = 0, Q = 0.707 });
        UpdateAll();
    }

    public string Name => "Equalizer";
    public bool Enabled { get; set; } = true;

    public void UpdateAll()
    {
        foreach (var b in Bands) b.Update(_sr);
    }

    public void Process(float[] buffer, int offset, int count)
    {
        if (!Enabled) return;
        for (int i = offset; i < offset + count; i++)
        {
            float x = buffer[i];
            foreach (var b in Bands)
                if (b.Enabled) x = b.Filter.Process(x);
            buffer[i] = x;
        }
    }

    public void Reset()
    {
        foreach (var b in Bands) b.Filter.Reset();
    }
}
