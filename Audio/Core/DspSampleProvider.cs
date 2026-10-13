using System;
using NAudio.Wave;

namespace MicForge.Audio;

/// <summary>Wraps a mono float source and runs the <see cref="DspChain"/> as it is pulled.</summary>
public sealed class DspSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly DspChain _chain;

    // The chain works on arrays, but NAudio 3 hands us a span (often over the device buffer).
    // The source reads into this scratch array, which only grows when a bigger block comes, so
    // the real-time path does not allocate once it is running.
    private float[] _scratch = Array.Empty<float>();

    public DspSampleProvider(ISampleProvider source, DspChain chain)
    {
        _source = source;
        _chain = chain;
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(Span<float> buffer)
    {
        if (_scratch.Length < buffer.Length) _scratch = new float[buffer.Length];
        int n = _source.Read(_scratch.AsSpan(0, buffer.Length));
        if (n <= 0) return n;
        _chain.Process(_scratch, 0, n);
        _scratch.AsSpan(0, n).CopyTo(buffer);
        return n;
    }
}
