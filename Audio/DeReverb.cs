using System;

namespace MicForge.Audio;

/// <summary>
/// Light de-reverb / echo-tail reducer. Compares a fast envelope (direct sound) against a
/// slow one (the lingering room tail); when the signal is in a decaying tail — fast energy
/// well below the recent average — it pulls the level down, favouring the direct voice.
/// Not a full acoustic dereverb, but it tightens up a boomy or echoey room.
/// </summary>
public sealed class DeReverb : IAudioProcessor
{
    private readonly double _sr;
    private double _fast, _slow, _gain = 1.0;

    public DeReverb(double sampleRate) => _sr = sampleRate;

    public string Name => "De-Reverb";
    public bool Enabled { get; set; }
    public double Amount { get; set; } = 50;     // percent — how hard to suppress the tail
    public double DecayMs { get; set; } = 150;    // room-tail time constant

    /// <summary>Current attenuation in dB, &lt;= 0 (for metering).</summary>
    public double ReductionDb { get; private set; }

    public void Process(float[] buffer, int offset, int count)
    {
        if (!Enabled) { ReductionDb = 0; return; }

        double fastC = Math.Exp(-1.0 / (_sr * 0.003));                         // 3 ms
        double slowC = Math.Exp(-1.0 / (_sr * Math.Max(DecayMs, 10) / 1000.0)); // tail
        double relC = Math.Exp(-1.0 / (_sr * 0.030));
        double depth = Math.Clamp(Amount / 100.0, 0, 1);
        double minG = 1.0;

        for (int i = offset; i < offset + count; i++)
        {
            double x = buffer[i];
            double a = Math.Abs(x);
            _fast = a > _fast ? a : a + (_fast - a) * fastC;
            _slow = a > _slow ? a + (_slow - a) * slowC : a + (_slow - a) * slowC;

            // Ratio < 1 → we're in a decaying tail; drive the gain down there.
            double ratio = _slow > 1e-6 ? _fast / _slow : 1.0;
            double target = Math.Clamp(ratio, 0, 1);
            target = 1.0 - depth * (1.0 - target);
            _gain = target < _gain ? target + (_gain - target) * fastC
                                   : target + (_gain - target) * relC;
            if (_gain < minG) minG = _gain;

            buffer[i] = (float)(x * _gain);
        }

        ReductionDb = 20 * Math.Log10(minG + 1e-9);
    }

    public void Reset() { _fast = _slow = 0; _gain = 1.0; ReductionDb = 0; }
}
