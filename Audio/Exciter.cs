using System;

namespace MicForge.Audio;

/// <summary>
/// Harmonic exciter. Isolates the high band, generates new upper harmonics from it with an
/// asymmetric rectifier, and blends them back in — adds "sparkle"/air and presence without
/// just boosting existing highs. Subtle amounts sound expensive; too much sounds fizzy.
/// </summary>
public sealed class Exciter : IAudioProcessor
{
    private readonly double _sr;
    private readonly Biquad _pre = new();    // isolate the high band
    private readonly Biquad _post = new();   // clean up the generated harmonics
    private double _curFreq;

    public Exciter(double sampleRate) { _sr = sampleRate; Update(); }

    public string Name => "Exciter";
    public bool Enabled { get; set; }
    public double Frequency { get; set; } = 3000;  // harmonics generated above here
    public double Amount { get; set; } = 25;         // percent

    private void Update()
    {
        _pre.Set(Biquad.FilterType.HighPass, Frequency, _sr, 0.707);
        _post.Set(Biquad.FilterType.HighPass, Frequency, _sr, 0.707);
        _curFreq = Frequency;
    }

    public void Process(float[] buffer, int offset, int count)
    {
        if (!Enabled) return;
        if (_curFreq != Frequency) Update();
        double amt = Math.Clamp(Amount / 100.0, 0, 1);

        for (int i = offset; i < offset + count; i++)
        {
            float x = buffer[i];
            float high = _pre.Process(x);
            // Asymmetric rectification generates both even and odd harmonics.
            double rect = high < 0 ? -high * 0.6 : high;
            float harm = _post.Process((float)rect);
            buffer[i] = (float)(x + amt * 3.0 * harm);
        }
    }

    public void Reset() { _pre.Reset(); _post.Reset(); }
}
