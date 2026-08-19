using System;

namespace MicForge.Audio;

/// <summary>
/// Split-band de-esser: detects sibilant energy in a band and attenuates only the high
/// band. Phase-correct and flat at rest (out = x + (g-1)*highband).
/// </summary>
public sealed class DeEsser : IAudioProcessor
{
    private readonly double _sr;
    private readonly Biquad _highSplit = new();
    private readonly Biquad _detBand = new();
    private double _envDb = -100;
    private double _curFreq;

    public DeEsser(double sampleRate)
    {
        _sr = sampleRate;
        Update();
    }

    public string Name => "De-Esser";
    public bool Enabled { get; set; } = true;
    public double Frequency { get; set; } = 6500;
    public double ThresholdDb { get; set; } = -28;
    public double Ratio { get; set; } = 4;

    /// <summary>Current sibilance-band level in dB (for metering).</summary>
    public double DetectorDb { get; private set; } = -100;
    /// <summary>Current reduction applied in dB, &lt;= 0 (for metering).</summary>
    public double ReductionDb { get; private set; }

    private void Update()
    {
        _highSplit.Set(Biquad.FilterType.HighPass, Frequency, _sr, 0.707);
        _detBand.Set(Biquad.FilterType.BandPass, Frequency, _sr, 1.2);
        _curFreq = Frequency;
    }

    public void Process(float[] buffer, int offset, int count)
    {
        if (!Enabled) return;
        if (_curFreq != Frequency) Update();

        double atk = Math.Exp(-1.0 / (_sr * 0.001)); // 1 ms
        double rel = Math.Exp(-1.0 / (_sr * 0.050)); // 50 ms

        for (int i = offset; i < offset + count; i++)
        {
            float x = buffer[i];
            float high = _highSplit.Process(x);
            float det = _detBand.Process(x);

            double lvl = 20 * Math.Log10(Math.Abs(det) + 1e-9);
            double coef = lvl > _envDb ? atk : rel;
            _envDb = lvl + (_envDb - lvl) * coef;

            double over = _envDb - ThresholdDb;
            double grDb = over > 0 ? -(over - over / Ratio) : 0;
            double g = Math.Pow(10, grDb / 20.0);

            buffer[i] = (float)(x + (g - 1.0) * high);
            ReductionDb = grDb;
        }

        DetectorDb = _envDb;
    }

    public void Reset() { _highSplit.Reset(); _detBand.Reset(); _envDb = -100; }
}
