using System;

namespace MicForge.Audio;

/// <summary>
/// Plosive ("P"/"B" pop) suppressor. Isolates the low band, watches for the sudden burst
/// of low-frequency energy a plosive makes, and ducks only that band while it lasts —
/// phase-correct and flat at rest (out = x + (g-1)*lowband).
/// </summary>
public sealed class DePlosive : IAudioProcessor
{
    private readonly double _sr;
    private readonly Biquad _low = new();
    private double _env = -100;
    private double _curFreq;

    public DePlosive(double sampleRate) { _sr = sampleRate; Update(); }

    public string Name => "De-Plosive";
    public bool Enabled { get; set; }
    public double Frequency { get; set; } = 150;   // isolate the pop energy below this
    public double ThresholdDb { get; set; } = -30; // low-band level a pop must exceed
    public double Strength { get; set; } = 70;     // percent — how hard to duck the pop

    /// <summary>Current low-band reduction in dB, &lt;= 0 (for metering).</summary>
    public double ReductionDb { get; private set; }

    private void Update()
    {
        _low.Set(Biquad.FilterType.LowPass, Frequency, _sr, 0.707);
        _curFreq = Frequency;
    }

    public void Process(float[] buffer, int offset, int count)
    {
        if (!Enabled) return;
        if (_curFreq != Frequency) Update();

        double atk = Math.Exp(-1.0 / (_sr * 0.002)); // 2 ms
        double rel = Math.Exp(-1.0 / (_sr * 0.080)); // 80 ms
        double depth = Math.Clamp(Strength / 100.0, 0, 1);
        double maxRed = 0;

        for (int i = offset; i < offset + count; i++)
        {
            float x = buffer[i];
            float low = _low.Process(x);

            double lvl = 20 * Math.Log10(Math.Abs(low) + 1e-9);
            double coef = lvl > _env ? atk : rel;
            _env = lvl + (_env - lvl) * coef;

            double over = _env - ThresholdDb;
            double g = 1.0;
            if (over > 0)
            {
                double red = Math.Min(over, 24) * depth;   // dB to pull the low band down
                g = Math.Pow(10, -red / 20.0);
                if (red > maxRed) maxRed = red;
            }
            buffer[i] = (float)(x + (g - 1.0) * low);
        }

        ReductionDb = -maxRed;
    }

    public void Reset() { _low.Reset(); _env = -100; ReductionDb = 0; }
}
