using System;

namespace MicForge.Audio;

/// <summary>
/// Single-channel echo remover. An NLMS adaptive filter learns the delayed, attenuated copy
/// of your own voice that a room (or a speaker bleeding into the mic) sends back, and
/// subtracts it. Tune Delay to roughly the echo time; Strength blends dry vs. cleaned.
/// (It has no far-end reference, so it targets slap-back / room echo of your own voice,
/// not full duplex acoustic echo cancellation.)
/// </summary>
public sealed class EchoRemover : IAudioProcessor
{
    private const int Taps = 512;                 // ~10 ms modelling window around the delay
    private readonly double _sr;
    private float[] _hist;
    private int _hlen, _pos, _delaySamp;
    private readonly float[] _w = new float[Taps];
    private double _curDelay;

    public EchoRemover(double sampleRate) { _sr = sampleRate; Ensure(); }

    public string Name => "Echo Remover";
    public bool Enabled { get; set; }
    public double DelayMs { get; set; } = 120;    // approximate echo time
    public double Strength { get; set; } = 60;     // percent

    private void Ensure()
    {
        int d = Math.Max(1, (int)(_sr * DelayMs / 1000.0));
        int hlen = d + Taps + 2;
        if (_hist == null || _hlen != hlen)
        {
            _hist = new float[hlen];
            _hlen = hlen;
            _pos = 0;
            Array.Clear(_w, 0, Taps);
        }
        _delaySamp = d;
        _curDelay = DelayMs;
    }

    public void Process(float[] buffer, int offset, int count)
    {
        if (!Enabled) return;
        if (_curDelay != DelayMs) Ensure();

        double strength = Math.Clamp(Strength / 100.0, 0, 1);
        if (strength <= 0) return;
        double mu = 0.4 * strength;      // adaptation rate
        double leak = 1.0 - 1e-4;        // gentle leakage keeps the filter from drifting

        for (int i = offset; i < offset + count; i++)
        {
            float x = buffer[i];
            _hist[_pos] = x;

            int baseIdx = _pos - _delaySamp;
            double y = 0, norm = 1e-6;
            for (int k = 0; k < Taps; k++)
            {
                int idx = baseIdx - k; while (idx < 0) idx += _hlen;
                float r = _hist[idx];
                y += _w[k] * r;
                norm += r * r;
            }

            double e = x - y;            // voice with the estimated echo removed
            double g = mu / norm;
            for (int k = 0; k < Taps; k++)
            {
                int idx = baseIdx - k; while (idx < 0) idx += _hlen;
                _w[k] = (float)(_w[k] * leak + g * e * _hist[idx]);
            }

            buffer[i] = (float)(x * (1 - strength) + e * strength);
            if (++_pos >= _hlen) _pos = 0;
        }
    }

    public void Reset()
    {
        if (_hist != null) Array.Clear(_hist, 0, _hlen);
        Array.Clear(_w, 0, Taps);
        _pos = 0;
    }
}
