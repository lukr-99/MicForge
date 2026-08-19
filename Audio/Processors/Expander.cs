using System;

namespace MicForge.Audio;

/// <summary>
/// Downward expander: a gentler alternative to the gate. Instead of slamming shut, it turns
/// quiet signal down progressively (by the ratio) once it falls below the threshold, so
/// room tone and bleed fade naturally instead of chopping.
/// </summary>
public sealed class Expander : AudioProcessorBase
{
    private readonly double _sr;
    private double _envDb = -100;
    private double _gainDb;

    public Expander(double sampleRate) => _sr = sampleRate;

    public override string Name => "Expander";
    public double ThresholdDb { get; set; } = -45;
    public double Ratio { get; set; } = 2.5;       // >1: downward expansion
    public double ReleaseMs { get; set; } = 150;
    public double RangeDb { get; set; } = -24;      // maximum attenuation

    /// <summary>Current attenuation in dB, &lt;= 0 (for metering).</summary>
    public double ReductionDb { get; private set; }

    protected override void WhenDisabled() => ReductionDb = 0;

    protected override void ProcessBlock(float[] buffer, int offset, int count)
    {

        double atk = Math.Exp(-1.0 / (_sr * 0.005));  // 5 ms detector attack
        double rel = Math.Exp(-1.0 / (_sr * Math.Max(ReleaseMs, 0.01) / 1000.0));
        double minG = 0;

        for (int i = offset; i < offset + count; i++)
        {
            double x = buffer[i];
            double lvl = 20 * Math.Log10(Math.Abs(x) + 1e-9);
            double coef = lvl > _envDb ? atk : rel;
            _envDb = lvl + (_envDb - lvl) * coef;

            double grTarget = _envDb < ThresholdDb ? (_envDb - ThresholdDb) * (Ratio - 1.0) : 0;
            if (grTarget < RangeDb) grTarget = RangeDb;
            // Move gain toward target with the same smoothing.
            _gainDb = grTarget + (_gainDb - grTarget) * (grTarget < _gainDb ? atk : rel);
            if (_gainDb < minG) minG = _gainDb;

            buffer[i] = (float)(x * Math.Pow(10, _gainDb / 20.0));
        }

        ReductionDb = minG;
    }

    public override void Reset() { _envDb = -100; _gainDb = 0; ReductionDb = 0; }
}
