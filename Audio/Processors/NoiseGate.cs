using System;

namespace MicForge.Audio;

/// <summary>Downward noise gate with attack / hold / release and a floor (range).</summary>
public sealed class NoiseGate : AudioProcessorBase
{
    private readonly double _sr;
    private readonly EnvelopeFollower _detect = new(0);   // level detector (instant attack)
    private readonly EnvelopeFollower _gain = new(1.0);    // applied gain 0..1
    private double _hold;                                   // remaining hold samples

    public NoiseGate(double sampleRate) { _sr = sampleRate; Enabled = true; }

    public override string Name => "Noise Gate";
    public double ThresholdDb { get; set; } = -45;
    public double AttackMs { get; set; } = 3;
    public double HoldMs { get; set; } = 150;
    public double ReleaseMs { get; set; } = 200;
    public double RangeDb { get; set; } = -70;   // attenuation applied when fully closed

    /// <summary>Open on RNNoise voice-activity instead of level.</summary>
    public bool UseVad { get; set; }
    public double VadThreshold { get; set; } = 0.6;
    public Func<double> VadProvider;   // returns 0..1, or &lt; 0 when unavailable
    public bool IsOpen => _gain.Value > 0.5;

    /// <summary>Current detector level in dB (for metering).</summary>
    public double DetectorDb { get; private set; } = -100;
    /// <summary>Current attenuation applied in dB, &lt;= 0 (for metering).</summary>
    public double ReductionDb { get; private set; }

    protected override void ProcessBlock(float[] buffer, int offset, int count)
    {
        double thr = DspMath.ToLinear(ThresholdDb);
        double floor = DspMath.ToLinear(RangeDb);
        double atk = DspMath.CoefMs(AttackMs, _sr);
        double rel = DspMath.CoefMs(ReleaseMs, _sr);
        double detRel = DspMath.Coef(0.010, _sr);   // 10 ms detector smoothing
        double holdSamples = _sr * HoldMs / 1000.0;
        double vad = (UseVad && VadProvider != null) ? VadProvider() : -1;

        for (int i = offset; i < offset + count; i++)
        {
            double det = _detect.Process(Math.Abs(buffer[i]), 0, detRel);

            bool open = vad >= 0 ? vad >= VadThreshold : det >= thr;
            double target;
            if (open) { target = 1.0; _hold = holdSamples; }
            else if (_hold > 0) { target = 1.0; _hold -= 1; }
            else target = floor;

            buffer[i] = (float)(buffer[i] * _gain.Process(target, atk, rel));
        }

        DetectorDb = DspMath.ToDb(_detect.Value);
        ReductionDb = DspMath.ToDb(_gain.Value);
    }

    public override void Reset() { _gain.Reset(1.0); _detect.Reset(0); _hold = 0; }
}
