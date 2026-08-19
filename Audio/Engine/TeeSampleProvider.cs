using System;
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
    private byte[] _bytes = Array.Empty<byte>();

    public volatile bool Active;

    public TeeSampleProvider(ISampleProvider source, BufferedWaveProvider tap)
    {
        _source = source;
        _tap = tap;
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        int n = _source.Read(buffer, offset, count);
        if (Active && n > 0)
        {
            int bytes = n * 4;
            if (_bytes.Length < bytes) _bytes = new byte[bytes];
            Buffer.BlockCopy(buffer, offset * 4, _bytes, 0, bytes);
            _tap.AddSamples(_bytes, 0, bytes);
        }
        return n;
    }
}
