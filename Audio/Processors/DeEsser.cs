using System;

namespace MicForge.Audio;

/// <summary>
/// Split-band de-esser: detects sibilant energy in a band and attenuates only the high
/// band. Phase-correct and flat at rest (out = x + (g-1)*highband).
/// </summary>
public sealed class DeEsser : AudioProcessorBase
{
    private readonly double _sr;
    private readonly Biquad _highSplit = new();
    private readonly Biquad _detBand = new();
    private readonly EnvelopeFollower _envDb = new(-100);
    private double _curFreq;

    public DeEsser(double sampleRate)
    {
        _sr = sampleRate;
        Enabled = true;
        Update();
    }

    public override string Name => "De-Esser";
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

    protected override void ProcessBlock(float[] buffer, int offset, int count)
    {
        if (_curFreq != Frequency) Update();

        double atk = DspMath.Coef(0.001, _sr); // 1 ms
        double rel = DspMath.Coef(0.050, _sr); // 50 ms

        for (int i = offset; i < offset + count; i++)
        {
            float x = buffer[i];
            float high = _highSplit.Process(x);
            float det = _detBand.Process(x);

            double env = _envDb.Process(DspMath.ToDb(det), atk, rel);
            double over = env - ThresholdDb;
            double grDb = over > 0 ? -(over - over / Ratio) : 0;

            buffer[i] = (float)(x + (DspMath.ToLinear(grDb) - 1.0) * high);
            ReductionDb = grDb;
        }

        DetectorDb = _envDb.Value;
    }

    public override void Reset() { _highSplit.Reset(); _detBand.Reset(); _envDb.Reset(-100); }
}
