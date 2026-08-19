using System;

namespace MicForge.Audio;

/// <summary>
/// Input auto-gain: slowly rides the gain to hold a steady speech level as your distance
/// or volume change. Gated on silence so it doesn't pump up noise. Runs early in the chain;
/// complements the output loudness auto-leveler.
/// </summary>
public sealed class InputAgc : AudioProcessorBase
{
    private readonly double _sr;
    private double _level;    // envelope follower (linear)
    private double _gainDb;

    public InputAgc(double sampleRate) => _sr = sampleRate;

    public override string Name => "Auto Gain";
    public double TargetDb { get; set; } = -18;
    public double MaxGainDb { get; set; } = 12;

    protected override void ProcessBlock(float[] buffer, int offset, int count)
    {

        double rel = Math.Exp(-1.0 / (_sr * 0.100));   // 100 ms envelope release
        double glide = Math.Exp(-1.0 / (_sr * 1.5));    // 1.5 s gain glide
        double gate = Math.Pow(10, -45 / 20.0);         // below this = silence, hold gain

        for (int i = offset; i < offset + count; i++)
        {
            double x = buffer[i];
            double a = Math.Abs(x);
            _level = a > _level ? a : a + (_level - a) * rel;

            double targetDb;
            if (_level > gate)
                targetDb = Math.Clamp(TargetDb - 20 * Math.Log10(_level + 1e-9), -MaxGainDb, MaxGainDb);
            else
                targetDb = _gainDb;

            _gainDb = targetDb + (_gainDb - targetDb) * glide;
            buffer[i] = (float)(x * Math.Pow(10, _gainDb / 20.0));
        }
    }

    public override void Reset() { _level = 0; _gainDb = 0; }
}
