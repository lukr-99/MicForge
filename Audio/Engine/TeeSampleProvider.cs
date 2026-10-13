using System;
using System.Runtime.InteropServices;
using NAudio.Wave;

namespace MicForge.Audio;

/// <summary>
/// Passes samples through unchanged, and (when <see cref="Active"/>) also copies them into
/// a tap buffer so a second output (headphone monitor) can play the same processed audio.
/// </summary>
public sealed class TeeSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly BufferedWaveProvider _tap;

    public volatile bool Active;

    public TeeSampleProvider(ISampleProvider source, BufferedWaveProvider tap)
    {
        _source = source;
        _tap = tap;
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(Span<float> buffer)
    {
        int n = _source.Read(buffer);
        if (Active && n > 0)
            _tap.AddSamples(MemoryMarshal.AsBytes(buffer[..n]));
        return n;
    }
}
