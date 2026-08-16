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

        for (int i = offset; i < offset + count; i++)
        {
            double x = Math.Abs(buffer[i]);
            _detect = x > _detect ? x : x + (_detect - x) * detRel;

            double target;
            if (_detect >= thr) { target = 1.0; _hold = holdSamples; }
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

/// <summary>Feed-forward compressor with soft knee and makeup gain.</summary>
public sealed class Compressor : IAudioProcessor
{
    private readonly double _sr;
    private double _envDb = -100;

    public Compressor(double sampleRate) => _sr = sampleRate;

    public string Name => "Compressor";
    public bool Enabled { get; set; } = true;
    public double ThresholdDb { get; set; } = -18;
    public double Ratio { get; set; } = 3;
    public double AttackMs { get; set; } = 10;
    public double ReleaseMs { get; set; } = 120;
    public double KneeDb { get; set; } = 6;
    public double MakeupDb { get; set; } = 0;

    /// <summary>Most recent gain reduction in dB (for metering).</summary>
    public double GainReductionDb { get; private set; }

    public void Process(float[] buffer, int offset, int count)
    {
        if (!Enabled) { GainReductionDb = 0; return; }

        double atk = Math.Exp(-1.0 / (_sr * Math.Max(AttackMs, 0.01) / 1000.0));
        double rel = Math.Exp(-1.0 / (_sr * Math.Max(ReleaseMs, 0.01) / 1000.0));
        double makeup = Math.Pow(10, MakeupDb / 20.0);
        double maxGr = 0;

        for (int i = offset; i < offset + count; i++)
        {
            double x = buffer[i];
            double lvl = 20 * Math.Log10(Math.Abs(x) + 1e-9);
            double coef = lvl > _envDb ? atk : rel;
            _envDb = lvl + (_envDb - lvl) * coef;

            double over = _envDb - ThresholdDb;
            double gainDb; // <= 0
            if (2 * over < -KneeDb) gainDb = 0;
            else if (KneeDb > 0 && 2 * Math.Abs(over) <= KneeDb)
            {
                double t = over + KneeDb / 2;
                gainDb = (1.0 / Ratio - 1.0) * (t * t) / (2 * KneeDb);
            }
            else gainDb = (ThresholdDb + over / Ratio) - _envDb;

            if (-gainDb > maxGr) maxGr = -gainDb;
            double g = Math.Pow(10, gainDb / 20.0) * makeup;
            buffer[i] = (float)(x * g);
        }

        GainReductionDb = maxGr;
    }

    public void Reset() { _envDb = -100; GainReductionDb = 0; }
}

/// <summary>
/// Split-band de-esser: detects sibilant energy in a band and attenuates only the
/// high band. Phase-correct and flat at rest (out = x + (g-1)*highband).
/// </summary>
public sealed class DeEsser : IAudioProcessor
{
    private readonly double _sr;
    private readonly Biquad _highSplit = new();
    private readonly Biquad _detBand = new();
    private double _envDb = -100;
    private double _curFreq;

    public DeEsser(double sampleRate)
    {
        _sr = sampleRate;
        Update();
    }

    public string Name => "De-Esser";
    public bool Enabled { get; set; } = true;
    public double Frequency { get; set; } = 6500;
    public double ThresholdDb { get; set; } = -28;
    public double Ratio { get; set; } = 4;

    /// <summary>Current sibilance-band level in dB (for metering).</summary>
    public double DetectorDb { get; private set; } = -100;
    /// <summary>Current reduction applied in dB, &lt;= 0 (for metering).</summary>
    public double ReductionDb { get; private set; }

    private void Update()
    {
        _highSplit.Set(Biquad.FilterType.HighPass, Frequency, _sr, 0.707);
        _detBand.Set(Biquad.FilterType.BandPass, Frequency, _sr, 1.2);
        _curFreq = Frequency;
    }

    public void Process(float[] buffer, int offset, int count)
    {
        if (!Enabled) return;
        if (_curFreq != Frequency) Update();

        double atk = Math.Exp(-1.0 / (_sr * 0.001)); // 1 ms
        double rel = Math.Exp(-1.0 / (_sr * 0.050)); // 50 ms

        for (int i = offset; i < offset + count; i++)
        {
            float x = buffer[i];
            float high = _highSplit.Process(x);
            float det = _detBand.Process(x);

            double lvl = 20 * Math.Log10(Math.Abs(det) + 1e-9);
            double coef = lvl > _envDb ? atk : rel;
            _envDb = lvl + (_envDb - lvl) * coef;

            double over = _envDb - ThresholdDb;
            double grDb = over > 0 ? -(over - over / Ratio) : 0;
            double g = Math.Pow(10, grDb / 20.0);

            buffer[i] = (float)(x + (g - 1.0) * high);
            ReductionDb = grDb;
        }

        DetectorDb = _envDb;
    }

    public void Reset() { _highSplit.Reset(); _detBand.Reset(); _envDb = -100; }
}

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

    private void EnsureDelay()
    {
        int n = Math.Max(1, (int)(_sr * LookaheadMs / 1000.0));
        if (n != _len) { _delay = new float[n]; _len = n; _pos = 0; _env = 0; _gain = 1; }
    }

    public void Process(float[] buffer, int offset, int count)
    {
        if (!Enabled) return;
        EnsureDelay();

        double ceil = Math.Pow(10, CeilingDb / 20.0);
        double atk = Math.Exp(-2.3 / _len);      // reach ~90% across the look-ahead window
        double envRel = Math.Exp(-1.0 / _len);   // hold a peak until it exits the delay
        double rel = Math.Exp(-1.0 / (_sr * Math.Max(ReleaseMs, 0.01) / 1000.0));

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

            buffer[i] = (float)(delayed * _gain);
        }
    }

    public void Reset()
    {
        _gain = 1.0; _env = 0; _pos = 0;
        if (_delay != null) Array.Clear(_delay, 0, _delay.Length);
    }
}
