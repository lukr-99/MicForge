using System;

namespace MicForge.Audio;

/// <summary>
/// Real-time pitch shifter (a granular delay-line shifter with two half-window-offset read
/// taps, triangular-cross-faded to hide the wrap discontinuity). Shifts the whole voice up
/// or down by a number of semitones — deep villain, chipmunk, or subtle disguise.
/// </summary>
public sealed class VoiceChanger : IAudioProcessor
{
    private const int Window = 2048;              // grain length in samples
    private readonly int _bufLen = 1 << 13;       // 8192-sample delay line
    private readonly float[] _buf;
    private int _wpos;
    private double _phase;                        // 0..1 sawtooth
    private double _ratio = 1.0;
    private double _curSemi = double.NaN;

    public VoiceChanger(double sampleRate) => _buf = new float[_bufLen];

    public string Name => "Voice Changer";
    public bool Enabled { get; set; }
    public double Semitones { get; set; } = 0;    // -12 .. +12
    public double Mix { get; set; } = 100;         // percent wet

    public void Process(float[] buffer, int offset, int count)
    {
        if (!Enabled) return;

        // No shift requested → pass through clean (avoids granular coloration at unity).
        if (Math.Abs(Semitones) < 0.05) return;

        if (_curSemi != Semitones) { _ratio = Math.Pow(2.0, Semitones / 12.0); _curSemi = Semitones; }

        double mix = Math.Clamp(Mix / 100.0, 0, 1);
        double dPhase = (1.0 - _ratio) / Window;

        for (int i = offset; i < offset + count; i++)
        {
            float x = buffer[i];
            _buf[_wpos] = x;

            double p1 = _phase;
            double p2 = _phase + 0.5; if (p2 >= 1.0) p2 -= 1.0;

            float s1 = Read(p1);
            float s2 = Read(p2);
            double w1 = 1.0 - Math.Abs(2.0 * p1 - 1.0);
            double w2 = 1.0 - Math.Abs(2.0 * p2 - 1.0);
            double denom = w1 + w2;
            double wet = denom > 1e-9 ? (s1 * w1 + s2 * w2) / denom : x;

            buffer[i] = (float)(x * (1 - mix) + wet * mix);

            _phase += dPhase;
            if (_phase >= 1.0) _phase -= 1.0;
            else if (_phase < 0.0) _phase += 1.0;

            if (++_wpos >= _bufLen) _wpos = 0;
        }
    }

    private float Read(double phase01)
    {
        double rp = _wpos - phase01 * Window;   // always behind the write pointer
        while (rp < 0) rp += _bufLen;
        int i0 = (int)rp;
        double frac = rp - i0;
        int i1 = i0 + 1; if (i1 >= _bufLen) i1 -= _bufLen;
        return (float)(_buf[i0] * (1 - frac) + _buf[i1] * frac);
    }

    public void Reset() { Array.Clear(_buf, 0, _bufLen); _wpos = 0; _phase = 0; }
}
