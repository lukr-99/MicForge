using System;

namespace MicForge.Audio;

/// <summary>Feed-forward compressor with soft knee and makeup gain.</summary>
public sealed class Compressor : IAudioProcessor
{
    private readonly double _sr;
    private double _envDb = -100;

    public Compressor(double sampleRate) => _sr = sampleRate;

    public string Name => "Compressor";
    public bool Enabled { get; set; } = true;
    public double ThresholdDb { get; set; } = -18;
    public double Ratio { get; set; } = 3;
    public double AttackMs { get; set; } = 10;
    public double ReleaseMs { get; set; } = 120;
    public double KneeDb { get; set; } = 6;
    public double MakeupDb { get; set; } = 0;

    /// <summary>Most recent gain reduction in dB (for metering).</summary>
    public double GainReductionDb { get; private set; }

    public void Process(float[] buffer, int offset, int count)
    {
        if (!Enabled) { GainReductionDb = 0; return; }

        double atk = Math.Exp(-1.0 / (_sr * Math.Max(AttackMs, 0.01) / 1000.0));
        double rel = Math.Exp(-1.0 / (_sr * Math.Max(ReleaseMs, 0.01) / 1000.0));
        double makeup = Math.Pow(10, MakeupDb / 20.0);
        double maxGr = 0;

        for (int i = offset; i < offset + count; i++)
        {
            double x = buffer[i];
            double lvl = 20 * Math.Log10(Math.Abs(x) + 1e-9);
            double coef = lvl > _envDb ? atk : rel;
            _envDb = lvl + (_envDb - lvl) * coef;

            double over = _envDb - ThresholdDb;
            double gainDb; // <= 0
            if (2 * over < -KneeDb) gainDb = 0;
            else if (KneeDb > 0 && 2 * Math.Abs(over) <= KneeDb)
            {
                double t = over + KneeDb / 2;
                gainDb = (1.0 / Ratio - 1.0) * (t * t) / (2 * KneeDb);
            }
            else gainDb = (ThresholdDb + over / Ratio) - _envDb;

            if (-gainDb > maxGr) maxGr = -gainDb;
            double g = Math.Pow(10, gainDb / 20.0) * makeup;
            buffer[i] = (float)(x * g);
        }

        GainReductionDb = maxGr;
    }

    public void Reset() { _envDb = -100; GainReductionDb = 0; }
}
