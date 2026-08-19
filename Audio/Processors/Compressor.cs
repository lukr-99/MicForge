using System;

namespace MicForge.Audio;

/// <summary>Feed-forward compressor with soft knee and makeup gain.</summary>
public sealed class Compressor : AudioProcessorBase
{
    private readonly double _sr;
    private readonly EnvelopeFollower _envDb = new(-100);   // level detector in dB

    public Compressor(double sampleRate) { _sr = sampleRate; Enabled = true; }

    public override string Name => "Compressor";
    public double ThresholdDb { get; set; } = -18;
    public double Ratio { get; set; } = 3;
    public double AttackMs { get; set; } = 10;
    public double ReleaseMs { get; set; } = 120;
    public double KneeDb { get; set; } = 6;
    public double MakeupDb { get; set; } = 0;

    /// <summary>Most recent gain reduction in dB (for metering).</summary>
    public double GainReductionDb { get; private set; }

    protected override void WhenDisabled() => GainReductionDb = 0;

    protected override void ProcessBlock(float[] buffer, int offset, int count)
    {
        double atk = DspMath.CoefMs(AttackMs, _sr);
        double rel = DspMath.CoefMs(ReleaseMs, _sr);
        double makeup = DspMath.ToLinear(MakeupDb);
        double maxGr = 0;

        for (int i = offset; i < offset + count; i++)
        {
            double x = buffer[i];
            double env = _envDb.Process(DspMath.ToDb(x), atk, rel);

            double over = env - ThresholdDb;
            double gainDb; // <= 0
            if (2 * over < -KneeDb) gainDb = 0;
            else if (KneeDb > 0 && 2 * Math.Abs(over) <= KneeDb)
            {
                double t = over + KneeDb / 2;
                gainDb = (1.0 / Ratio - 1.0) * (t * t) / (2 * KneeDb);
            }
            else gainDb = (ThresholdDb + over / Ratio) - env;

            if (-gainDb > maxGr) maxGr = -gainDb;
            buffer[i] = (float)(x * DspMath.ToLinear(gainDb) * makeup);
        }

        GainReductionDb = maxGr;
    }

    public override void Reset() { _envDb.Reset(-100); GainReductionDb = 0; }
}
