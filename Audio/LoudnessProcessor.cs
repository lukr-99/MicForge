using System;

namespace MicForge.Audio;

/// <summary>
/// Measures perceived loudness (LUFS, ITU-R BS.1770 K-weighting) and, when auto-level is
/// on, slowly adjusts gain to hold a target loudness. Runs last in the chain. K-weighting
/// is measurement-only; it never colours the audio.
/// </summary>
public sealed class LoudnessProcessor : IAudioProcessor
{
    private readonly double _sr;
    private readonly Biquad _stage1 = new();   // BS.1770 pre-filter (high shelf)
    private readonly Biquad _stage2 = new();   // BS.1770 RLB high-pass

    private double _msMomentary, _msShort;     // running mean-square of K-weighted signal
    private double _gainDb;                     // current auto-level gain

    public LoudnessProcessor(double sampleRate)
    {
        _sr = sampleRate;
        // Fixed BS.1770 coefficients for 48 kHz (the engine's internal rate).
        _stage1.SetRaw(1.53512485958697, -2.69169618940638, 1.19839281085285, -1.69065929318241, 0.73248077421585);
        _stage2.SetRaw(1.0, -2.0, 1.0, -1.99004745483398, 0.99007225036621);
    }

    public string Name => "Loudness";
    public bool Enabled { get; set; } = true;   // always measures; AutoLevel controls gain

    public bool AutoLevel { get; set; }
    public double TargetLufs { get; set; } = -16;
    public double MaxGainDb { get; set; } = 12;

    public double MomentaryLufs { get; private set; } = -70;
    public double ShortTermLufs { get; private set; } = -70;

    public void Process(float[] buffer, int offset, int count)
    {
        if (!Enabled) return;

        double aM = Math.Exp(-1.0 / (_sr * 0.4));   // 400 ms momentary
        double aS = Math.Exp(-1.0 / (_sr * 3.0));   // 3 s short-term
        double gCoef = Math.Exp(-1.0 / (_sr * 1.5)); // gain glide ~1.5 s

        for (int i = offset; i < offset + count; i++)
        {
            double x = buffer[i];

            double k = _stage2.Process(_stage1.Process((float)x));
            double sq = k * k;
            _msMomentary = sq + (_msMomentary - sq) * aM;
            _msShort = sq + (_msShort - sq) * aS;

            double stLufs = -0.691 + 10 * Math.Log10(_msShort + 1e-12);

            double targetDb;
            if (AutoLevel && stLufs > -50)                       // only chase while speech is present
                targetDb = Math.Clamp(TargetLufs - stLufs, -MaxGainDb, MaxGainDb);
            else
                targetDb = AutoLevel ? _gainDb : 0;              // hold on silence; unity when off

            _gainDb = targetDb + (_gainDb - targetDb) * gCoef;
            if (AutoLevel) buffer[i] = (float)(x * Math.Pow(10, _gainDb / 20.0));
        }

        MomentaryLufs = -0.691 + 10 * Math.Log10(_msMomentary + 1e-12);
        ShortTermLufs = -0.691 + 10 * Math.Log10(_msShort + 1e-12);
    }

    public void Reset()
    {
        _msMomentary = _msShort = 0;
        _gainDb = 0;
        _stage1.Reset();
        _stage2.Reset();
        MomentaryLufs = ShortTermLufs = -70;
    }
}
