using NAudio.Wave;

namespace MicForge.Audio;

/// <summary>Wraps a mono float source and runs the <see cref="DspChain"/> as it is pulled.</summary>
public sealed class DspSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly DspChain _chain;

    public DspSampleProvider(ISampleProvider source, DspChain chain)
    {
        _source = source;
        _chain = chain;
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        int n = _source.Read(buffer, offset, count);
        if (n > 0) _chain.Process(buffer, offset, n);
        return n;
    }
}
