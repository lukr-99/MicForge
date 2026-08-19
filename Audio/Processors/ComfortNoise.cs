using System;

namespace MicForge.Audio;

/// <summary>
/// Comfort noise: adds a faint, soft-filtered noise bed that fills the dead silence a gate
/// or expander leaves behind, so listeners on a call don't think you dropped off. It fades
/// in only when you're not speaking, so it never muddies your voice.
/// </summary>
public sealed class ComfortNoise : AudioProcessorBase
{
    private readonly double _sr;
    private readonly Biquad _tone = new();   // soften the hiss
    private uint _rng = 0x1234567u;
    private double _env = -100;
    private double _fill;                     // 0..1 fade of the noise bed
    private double _curTone;

    public ComfortNoise(double sampleRate) { _sr = sampleRate; Update(); }

    public override string Name => "Comfort Noise";
    public double LevelDb { get; set; } = -60;   // noise bed level
    public double ToneHz { get; set; } = 2000;   // low-pass on the noise

    private void Update()
    {
        _tone.Set(Biquad.FilterType.LowPass, ToneHz, _sr, 0.707);
        _curTone = ToneHz;
    }

    private float White()
    {
        _rng ^= _rng << 13; _rng ^= _rng >> 17; _rng ^= _rng << 5;
        return (_rng / 2147483648f) - 1f;   // -1..1
    }

    protected override void ProcessBlock(float[] buffer, int offset, int count)
    {
        if (_curTone != ToneHz) Update();

        double amp = Math.Pow(10, LevelDb / 20.0);
        double fillRise = Math.Exp(-1.0 / (_sr * 0.100));  // fade in over ~100 ms of silence
        double fillFall = Math.Exp(-1.0 / (_sr * 0.020));  // duck out fast when you speak
        double detRel = Math.Exp(-1.0 / (_sr * 0.050));

        for (int i = offset; i < offset + count; i++)
        {
            double x = buffer[i];
            double a = Math.Abs(x);
            _env = a > _env ? a : a + (_env - a) * detRel;

            // Present when quiet, gone when speaking.
            double target = _env < 0.02 ? 1.0 : 0.0;
            _fill = target > _fill ? target + (_fill - target) * fillRise
                                   : target + (_fill - target) * fillFall;

            float n = _tone.Process(White());
            buffer[i] = (float)(x + n * amp * _fill);
        }
    }

    public override void Reset() { _tone.Reset(); _env = -100; _fill = 0; }
}
