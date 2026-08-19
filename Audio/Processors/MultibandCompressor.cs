using System;

namespace MicForge.Audio;

/// <summary>
/// Three-band compressor. Splits the signal into low/mid/high via a phase-complementary
/// crossover (perfect reconstruction: low+mid+high sums back to the input), compresses each
/// band independently, and sums. Controls mud, boxiness and harshness separately.
/// </summary>
public sealed class MultibandCompressor : AudioProcessorBase
{
    private readonly double _sr;
    private readonly Biquad _lp1 = new();   // below CrossLow
    private readonly Biquad _lp2 = new();   // below CrossHigh
    private double _curLo, _curHi;
    private double _eLow = -100, _eMid = -100, _eHigh = -100;

    public MultibandCompressor(double sampleRate) { _sr = sampleRate; Update(); }

    public override string Name => "Multiband";
    public double CrossLow { get; set; } = 250;
    public double CrossHigh { get; set; } = 3000;
    public double ThreshLowDb { get; set; } = -24;
    public double ThreshMidDb { get; set; } = -20;
    public double ThreshHighDb { get; set; } = -22;
    public double Ratio { get; set; } = 3;
    public double MakeupDb { get; set; } = 0;

    /// <summary>Largest band gain reduction in dB, &gt;= 0 (for metering).</summary>
    public double GainReductionDb { get; private set; }

    private void Update()
    {
        _lp1.Set(Biquad.FilterType.LowPass, CrossLow, _sr, 0.707);
        _lp2.Set(Biquad.FilterType.LowPass, CrossHigh, _sr, 0.707);
        _curLo = CrossLow; _curHi = CrossHigh;
    }

    protected override void WhenDisabled() => GainReductionDb = 0;

    protected override void ProcessBlock(float[] buffer, int offset, int count)
    {
        if (_curLo != CrossLow || _curHi != CrossHigh) Update();

        double atk = Math.Exp(-1.0 / (_sr * 0.008));
        double rel = Math.Exp(-1.0 / (_sr * 0.120));
        double makeup = Math.Pow(10, MakeupDb / 20.0);
        double maxGr = 0;

        for (int i = offset; i < offset + count; i++)
        {
            float x = buffer[i];
            float lp1 = _lp1.Process(x);
            float lp2 = _lp2.Process(x);
            float low = lp1, mid = lp2 - lp1, high = x - lp2;

            low = (float)(low * BandGain(low, ThreshLowDb, ref _eLow, atk, rel, ref maxGr));
            mid = (float)(mid * BandGain(mid, ThreshMidDb, ref _eMid, atk, rel, ref maxGr));
            high = (float)(high * BandGain(high, ThreshHighDb, ref _eHigh, atk, rel, ref maxGr));

            buffer[i] = (float)((low + mid + high) * makeup);
        }

        GainReductionDb = maxGr;
    }

    private double BandGain(double sample, double thr, ref double env, double atk, double rel, ref double maxGr)
    {
        double lvl = 20 * Math.Log10(Math.Abs(sample) + 1e-9);
        double coef = lvl > env ? atk : rel;
        env = lvl + (env - lvl) * coef;
        double over = env - thr;
        if (over <= 0) return 1.0;
        double gainDb = over / Ratio - over;   // <= 0
        if (-gainDb > maxGr) maxGr = -gainDb;
        return Math.Pow(10, gainDb / 20.0);
    }

    public override void Reset()
    {
        _lp1.Reset(); _lp2.Reset();
        _eLow = _eMid = _eHigh = -100;
        GainReductionDb = 0;
    }
}
