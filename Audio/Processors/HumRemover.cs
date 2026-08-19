using System;

namespace MicForge.Audio;

/// <summary>
/// Removes mains hum and its harmonics with a stack of narrow notch filters at the base
/// frequency (50 Hz in most of the world, 60 Hz in North America) and its multiples.
/// </summary>
public sealed class HumRemover : AudioProcessorBase
{
    private readonly double _sr;
    private Biquad[] _notches = Array.Empty<Biquad>();
    private double _curFreq;
    private int _curHarm;
    private double _curQ;

    public HumRemover(double sampleRate) => _sr = sampleRate;

    public override string Name => "Hum Remover";
    public double Frequency { get; set; } = 50;   // 50 (EU/most) or 60 (US) Hz
    public int Harmonics { get; set; } = 4;        // notch this many multiples of the base
    public double Q { get; set; } = 30;            // higher = narrower notch (less tone loss)

    private int ClampedHarm => Math.Clamp(Harmonics, 1, 10);

    private void Rebuild()
    {
        int n = ClampedHarm;
        var arr = new Biquad[n];
        for (int i = 0; i < n; i++)
        {
            var bq = new Biquad();
            bq.Set(Biquad.FilterType.Notch, Frequency * (i + 1), _sr, Q);
            arr[i] = bq;
        }
        _notches = arr;
        _curFreq = Frequency; _curHarm = n; _curQ = Q;
    }

    protected override void ProcessBlock(float[] buffer, int offset, int count)
    {
        if (_notches.Length == 0 || _curFreq != Frequency || _curHarm != ClampedHarm || _curQ != Q)
            Rebuild();

        var notches = _notches;
        for (int i = offset; i < offset + count; i++)
        {
            float x = buffer[i];
            for (int k = 0; k < notches.Length; k++) x = notches[k].Process(x);
            buffer[i] = x;
        }
    }

    public override void Reset() { foreach (var b in _notches) b.Reset(); }
}
