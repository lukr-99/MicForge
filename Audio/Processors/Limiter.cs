using System;

namespace MicForge.Audio;

/// <summary>
/// Look-ahead brick-wall limiter. A short delay lets the gain ramp down before a peak
/// reaches the output, so peaks are caught smoothly instead of clamped instantly.
/// </summary>
public sealed class Limiter : IAudioProcessor
{
    private readonly double _sr;
    private double _gain = 1.0;
    private double _env;
    private float[] _delay = new float[1];
    private int _len = 1, _pos;

    public Limiter(double sampleRate) => _sr = sampleRate;

    public string Name => "Limiter";
    public bool Enabled { get; set; } = true;
    public double CeilingDb { get; set; } = -1.0;
    public double ReleaseMs { get; set; } = 60;
    public double LookaheadMs { get; set; } = 2.0;

    /// <summary>Most recent gain reduction in dB, &gt;= 0 (for metering).</summary>
    public double GainReductionDb { get; private set; }

    private void EnsureDelay()
    {
        int n = Math.Max(1, (int)(_sr * LookaheadMs / 1000.0));
        if (n != _len) { _delay = new float[n]; _len = n; _pos = 0; _env = 0; _gain = 1; }
    }

    public void Process(float[] buffer, int offset, int count)
    {
        if (!Enabled) { GainReductionDb = 0; return; }
        EnsureDelay();

        double ceil = Math.Pow(10, CeilingDb / 20.0);
        double atk = Math.Exp(-2.3 / _len);      // reach ~90% across the look-ahead window
        double envRel = Math.Exp(-1.0 / _len);   // hold a peak until it exits the delay
        double rel = Math.Exp(-1.0 / (_sr * Math.Max(ReleaseMs, 0.01) / 1000.0));
        double minGain = 1.0;

        for (int i = offset; i < offset + count; i++)
        {
            double x = buffer[i];
            float delayed = _delay[_pos];
            _delay[_pos] = (float)x;
            _pos = (_pos + 1) % _len;

            double a = Math.Abs(x);
            _env = a > _env ? a : a + (_env - a) * envRel;   // peak-hold detector on the incoming signal
            double req = _env > ceil ? ceil / _env : 1.0;
            _gain = req < _gain ? req + (_gain - req) * atk : req + (_gain - req) * rel;
            if (_gain < minGain) minGain = _gain;

            buffer[i] = (float)(delayed * _gain);
        }

        GainReductionDb = minGain < 1.0 ? -20 * Math.Log10(minGain) : 0;
    }

    public void Reset()
    {
        _gain = 1.0; _env = 0; _pos = 0; GainReductionDb = 0;
        if (_delay != null) Array.Clear(_delay, 0, _delay.Length);
    }
}
