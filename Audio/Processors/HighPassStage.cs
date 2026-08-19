namespace MicForge.Audio;

/// <summary>High-pass filter that removes rumble / handling noise / DC below the cutoff.</summary>
public sealed class HighPassStage : IAudioProcessor
{
    private readonly Biquad _bq = new();
    private readonly double _sr;
    private double _freq = 80;

    public HighPassStage(double sampleRate)
    {
        _sr = sampleRate;
        Update();
    }

    public string Name => "High-Pass";
    public bool Enabled { get; set; } = true;
    public double Frequency
    {
        get => _freq;
        set { _freq = value; Update(); }
    }

    private void Update() => _bq.Set(Biquad.FilterType.HighPass, _freq, _sr, 0.707);

    public void Process(float[] buffer, int offset, int count)
    {
        if (!Enabled) return;
        for (int i = offset; i < offset + count; i++)
            buffer[i] = _bq.Process(buffer[i]);
    }

    public void Reset() => _bq.Reset();
}
