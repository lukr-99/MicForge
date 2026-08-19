using System;

namespace MicForge.Audio;

/// <summary>Downward noise gate with attack / hold / release and a floor (range).</summary>
public sealed class NoiseGate : IAudioProcessor
{
    private readonly double _sr;
    private double _env = 1.0;      // current applied gain 0..1
    private double _hold;           // remaining hold samples
    private double _detect;         // smoothed level detector

    public NoiseGate(double sampleRate) => _sr = sampleRate;

    public string Name => "Noise Gate";
    public bool Enabled { get; set; } = true;
    public double ThresholdDb { get; set; } = -45;
    public double AttackMs { get; set; } = 3;
    public double HoldMs { get; set; } = 150;
    public double ReleaseMs { get; set; } = 200;
    public double RangeDb { get; set; } = -70;   // attenuation applied when fully closed

    /// <summary>Open on RNNoise voice-activity instead of level.</summary>
    public bool UseVad { get; set; }
    public double VadThreshold { get; set; } = 0.6;
    public Func<double> VadProvider;   // returns 0..1, or &lt; 0 when unavailable
    public bool IsOpen => _env > 0.5;

    /// <summary>Current detector level in dB (for metering).</summary>
    public double DetectorDb { get; private set; } = -100;
    /// <summary>Current attenuation applied in dB, &lt;= 0 (for metering).</summary>
    public double ReductionDb { get; private set; }

    public void Process(float[] buffer, int offset, int count)
    {
        if (!Enabled) return;

        double thr = Math.Pow(10, ThresholdDb / 20.0);
        double floor = Math.Pow(10, RangeDb / 20.0);
        double atk = Math.Exp(-1.0 / (_sr * Math.Max(AttackMs, 0.01) / 1000.0));
        double rel = Math.Exp(-1.0 / (_sr * Math.Max(ReleaseMs, 0.01) / 1000.0));
        double detRel = Math.Exp(-1.0 / (_sr * 0.010)); // 10 ms detector smoothing
        double holdSamples = _sr * HoldMs / 1000.0;
        double vad = (UseVad && VadProvider != null) ? VadProvider() : -1;

        for (int i = offset; i < offset + count; i++)
        {
            double x = Math.Abs(buffer[i]);
            _detect = x > _detect ? x : x + (_detect - x) * detRel;

            bool open = vad >= 0 ? vad >= VadThreshold : _detect >= thr;
            double target;
            if (open) { target = 1.0; _hold = holdSamples; }
            else if (_hold > 0) { target = 1.0; _hold -= 1; }
            else target = floor;

            double coef = target > _env ? atk : rel;
            _env = target + (_env - target) * coef;
            buffer[i] = (float)(buffer[i] * _env);
        }

        DetectorDb = 20 * Math.Log10(_detect + 1e-9);
        ReductionDb = 20 * Math.Log10(_env + 1e-9);
    }

    public void Reset() { _env = 1.0; _hold = 0; _detect = 0; }
}
