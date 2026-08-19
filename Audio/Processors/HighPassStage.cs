namespace MicForge.Audio;

/// <summary>High-pass filter that removes rumble / handling noise / DC below the cutoff.</summary>
public sealed class HighPassStage : AudioProcessorBase
{
    private readonly Biquad _bq = new();
    private readonly double _sr;
    private double _freq = 80;

    public HighPassStage(double sampleRate)
    {
        _sr = sampleRate;
        Enabled = true;
        Update();
    }

    public override string Name => "High-Pass";
    public double Frequency
    {
        get => _freq;
        set { _freq = value; Update(); }
    }

    private void Update() => _bq.Set(Biquad.FilterType.HighPass, _freq, _sr, 0.707);

    protected override void ProcessBlock(float[] buffer, int offset, int count)
    {
        for (int i = offset; i < offset + count; i++)
            buffer[i] = _bq.Process(buffer[i]);
    }

    public override void Reset() => _bq.Reset();
}
