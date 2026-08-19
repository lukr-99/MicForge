using System;

namespace MicForge.Audio;

/// <summary>
/// Small shared DSP helpers so the recurring dB / time-constant / envelope math lives in one
/// place instead of being copy-pasted through every processor.
/// </summary>
public static class DspMath
{
    /// <summary>dB → linear amplitude (e.g. -6 dB → 0.5).</summary>
    public static double ToLinear(double db) => Math.Pow(10, db / 20.0);

    /// <summary>Linear amplitude → dB, floored so silence maps to a finite value.</summary>
    public static double ToDb(double linear) => 20 * Math.Log10(Math.Abs(linear) + 1e-9);

    /// <summary>
    /// One-pole smoothing coefficient for a given time constant (seconds) at a sample rate:
    /// <c>env = target + (env - target) * coef</c> reaches ~63% in <paramref name="seconds"/>.
    /// </summary>
    public static double Coef(double seconds, double sampleRate)
        => Math.Exp(-1.0 / (sampleRate * Math.Max(seconds, 1e-6)));

    /// <summary>As <see cref="Coef(double,double)"/> but the time constant is in milliseconds.</summary>
    public static double CoefMs(double ms, double sampleRate) => Coef(ms / 1000.0, sampleRate);
}
