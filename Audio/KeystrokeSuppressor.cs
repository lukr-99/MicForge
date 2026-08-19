using System;

namespace MicForge.Audio;

/// <summary>
/// Keyboard-click suppressor. Mechanical key presses are very fast, high-frequency-rich
/// transients. This watches a high-band detector for a sharp spike well above its own
/// short-term average (the signature of a click/clack) and briefly ducks the signal so the
/// click is knocked down while your voice — which rises much more gradually — is left alone.
/// </summary>
public sealed class KeystrokeSuppressor : IAudioProcessor
{
    private readonly double _sr;
    private readonly Biquad _hp = new();
    private double _fast, _slow, _duck = 1.0, _hold;
    private double _curFreq;

    public KeystrokeSuppressor(double sampleRate) { _sr = sampleRate; Update(); }

    public string Name => "Keystroke Suppressor";
    public bool Enabled { get; set; }
    public double DetectFreq { get; set; } = 2800;   // detector high-pass (Hz) — match your keyboard's click
    public double Sensitivity { get; set; } = 8;      // dB the spike must exceed the running average
    public double Strength { get; set; } = 70;        // percent — how hard a detected click is ducked
    public double ReleaseMs { get; set; } = 45;

    /// <summary>Current attenuation in dB, &lt;= 0 (for metering).</summary>
    public double ReductionDb { get; private set; }

    private void Update()
    {
        _hp.Set(Biquad.FilterType.HighPass, DetectFreq, _sr, 0.707);
        _curFreq = DetectFreq;
    }

    public void Process(float[] buffer, int offset, int count)
    {
        if (!Enabled) { ReductionDb = 0; return; }
        if (_curFreq != DetectFreq) Update();

        double fastC = Math.Exp(-1.0 / (_sr * 0.0004));   // 0.4 ms
        double slowC = Math.Exp(-1.0 / (_sr * 0.080));     // 80 ms average
        double relC = Math.Exp(-1.0 / (_sr * Math.Max(ReleaseMs, 1) / 1000.0));
        double depth = Math.Clamp(Strength / 100.0, 0, 1);
        double holdSamp = _sr * 0.012;                     // duck for ~12 ms per click
        const double floor = 1e-4;                          // ignore clicks below this (hiss)
        double minG = 1.0;

        for (int i = offset; i < offset + count; i++)
        {
            float x = buffer[i];
            double a = Math.Abs(_hp.Process(x));
            _fast = a > _fast ? a : a + (_fast - a) * fastC;
            _slow = a + (_slow - a) * slowC;

            double ratioDb = 20 * Math.Log10((_fast + 1e-9) / (_slow + 1e-9));
            if (ratioDb > Sensitivity && _fast > floor) _hold = holdSamp;

            double target = _hold > 0 ? 1.0 - depth : 1.0;
            if (_hold > 0) _hold -= 1;
            // Duck fast, recover gently.
            _duck = target < _duck ? target : target + (_duck - target) * relC;

            buffer[i] = (float)(x * _duck);
            if (_duck < minG) minG = _duck;
        }

        ReductionDb = 20 * Math.Log10(minG + 1e-9);
    }

    public void Reset() { _hp.Reset(); _fast = _slow = 0; _duck = 1.0; _hold = 0; }
}
