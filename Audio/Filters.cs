using System;
using System.Collections.Generic;

namespace MicForge.Audio;

/// <summary>Simple wide-band gain (used for input trim and output/makeup).</summary>
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

/// <summary>High-pass filter to kill rumble / handling noise / DC.</summary>
public sealed class HighPassStage : IAudioProcessor
{
    private readonly Biquad _bq = new();
    private readonly double _sr;
    private double _freq = 80;

    public HighPassStage(double sampleRate)
    {
        _sr = sampleRate;
        Update();
    }

    public string Name => "High-Pass";
    public bool Enabled { get; set; } = true;
    public double Frequency
    {
        get => _freq;
        set { _freq = value; Update(); }
    }

    private void Update() => _bq.Set(Biquad.FilterType.HighPass, _freq, _sr, 0.707);

    public void Process(float[] buffer, int offset, int count)
    {
        if (!Enabled) return;
        for (int i = offset; i < offset + count; i++)
            buffer[i] = _bq.Process(buffer[i]);
    }

    public void Reset() => _bq.Reset();
}

/// <summary>Multi-band parametric EQ (low shelf + 3 peaks + high shelf by default).</summary>
public sealed class ParametricEq : IAudioProcessor
{
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
