using System;

namespace MicForge.Audio;

/// <summary>
/// Look-ahead brick-wall limiter. A short delay lets the gain ramp down before a peak
/// reaches the output, so peaks are caught smoothly instead of clamped instantly.
/// </summary>
public sealed class Limiter : AudioProcessorBase
{
    private readonly double _sr;
    private readonly EnvelopeFollower _env = new(0);   // peak-hold detector
    private double _gain = 1.0;
    private float[] _delay = new float[1];
    private int _len = 1, _pos;

    public Limiter(double sampleRate) { _sr = sampleRate; Enabled = true; }

    public override string Name => "Limiter";
    public double CeilingDb { get; set; } = -1.0;
    public double ReleaseMs { get; set; } = 60;
    public double LookaheadMs { get; set; } = 2.0;

    /// <summary>Most recent gain reduction in dB, &gt;= 0 (for metering).</summary>
    public double GainReductionDb { get; private set; }

    protected override void WhenDisabled() => GainReductionDb = 0;

    private void EnsureDelay()
    {
        int n = Math.Max(1, (int)(_sr * LookaheadMs / 1000.0));
        if (n != _len) { _delay = new float[n]; _len = n; _pos = 0; _env.Reset(0); _gain = 1; }
    }

    protected override void ProcessBlock(float[] buffer, int offset, int count)
    {
        EnsureDelay();

        double ceil = DspMath.ToLinear(CeilingDb);
        double atk = Math.Exp(-2.3 / _len);      // reach ~90% across the look-ahead window
        double envRel = Math.Exp(-1.0 / _len);   // hold a peak until it exits the delay
        double rel = DspMath.CoefMs(ReleaseMs, _sr);
        double minGain = 1.0;

        for (int i = offset; i < offset + count; i++)
        {
            double x = buffer[i];
            float delayed = _delay[_pos];
            _delay[_pos] = (float)x;
            _pos = (_pos + 1) % _len;

            double env = _env.Process(Math.Abs(x), 0, envRel);   // instant-attack peak follower
            double req = env > ceil ? ceil / env : 1.0;
            _gain = req < _gain ? req + (_gain - req) * atk : req + (_gain - req) * rel;
            if (_gain < minGain) minGain = _gain;

            buffer[i] = (float)(delayed * _gain);
        }

        GainReductionDb = minGain < 1.0 ? -20 * Math.Log10(minGain) : 0;
    }

    public override void Reset()
    {
        _gain = 1.0; _env.Reset(0); _pos = 0; GainReductionDb = 0;
        if (_delay != null) Array.Clear(_delay, 0, _delay.Length);
    }
}
