using System;

namespace MicForge.Audio;

/// <summary>
/// Reduces mouth clicks and lip-smacks: watches a mid/high detection band for a fast
/// spike that jumps well above its own short-term average (the signature of a click) and
/// briefly ducks the high band when it fires. Flat at rest (out = x + (g-1)*highband).
/// </summary>
public sealed class DeClicker : AudioProcessorBase
{
    private readonly double _sr;
    private readonly Biquad _detect = new();   // detector band
    private readonly Biquad _high = new();      // band to duck
    private double _fast, _slow, _reduce = 1.0;
    private double _curFreq;

    public DeClicker(double sampleRate) { _sr = sampleRate; Update(); }

    public override string Name => "De-Click";
    public double Frequency { get; set; } = 3000;  // clicks/smacks live around here
    public double Sensitivity { get; set; } = 6;    // dB the fast peak must exceed the average
    public double Strength { get; set; } = 70;      // percent — how hard to duck a detected click

    private void Update()
    {
        _detect.Set(Biquad.FilterType.BandPass, Frequency, _sr, 1.0);
        _high.Set(Biquad.FilterType.HighPass, Frequency * 0.6, _sr, 0.707);
        _curFreq = Frequency;
    }

    protected override void ProcessBlock(float[] buffer, int offset, int count)
    {
        if (_curFreq != Frequency) Update();

        double fastC = Math.Exp(-1.0 / (_sr * 0.0005)); // 0.5 ms
        double slowC = Math.Exp(-1.0 / (_sr * 0.050));  // 50 ms average
        double relC = Math.Exp(-1.0 / (_sr * 0.004));   // 4 ms recovery
        double depth = Math.Clamp(Strength / 100.0, 0, 1);

        for (int i = offset; i < offset + count; i++)
        {
            float x = buffer[i];
            double a = Math.Abs(_detect.Process(x));
            _fast = a > _fast ? a : a + (_fast - a) * fastC;
            _slow = a + (_slow - a) * slowC;

            double ratioDb = 20 * Math.Log10((_fast + 1e-9) / (_slow + 1e-9));
            double target = ratioDb > Sensitivity ? (1.0 - depth) : 1.0;
            // Duck instantly on a click, recover gently.
            _reduce = target < _reduce ? target : target + (_reduce - target) * relC;

            float high = _high.Process(x);
            buffer[i] = (float)(x + (_reduce - 1.0) * high);
        }
    }

    public override void Reset() { _detect.Reset(); _high.Reset(); _fast = _slow = 0; _reduce = 1.0; }
}
