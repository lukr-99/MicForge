using MicForge.Audio;
using NAudio.Wave;

namespace MicForge.Tests;

public sealed class SampleProviderTests
{
    private const int Sr = 48000;

    private static float[] Ramp(int n)
    {
        var b = new float[n];
        for (int i = 0; i < n; i++) b[i] = (i % 200) / 400f - 0.25f;
        return b;
    }

    private static DspChain HighPassOnly()
    {
        var chain = new DspChain(Sr);
        foreach (var p in chain.DefaultOrder) p.Enabled = false;
        chain.HighPass.Enabled = true;
        return chain;
    }

    [Fact]
    public void DspSampleProvider_Read_MatchesChainAndWritesOnlyTheSpan()
    {
        var input = Ramp(1000);
        var expected = (float[])input.Clone();
        HighPassOnly().Process(expected, 0, expected.Length);

        var provider = new DspSampleProvider(new ArrayProvider(input), HighPassOnly());
        var output = new float[1010];
        Array.Fill(output, 9f);
        int pos = 5;
        foreach (int block in new[] { 100, 400, 300, 200 })   // the scratch array grows, then is reused
        {
            Assert.Equal(block, provider.Read(output.AsSpan(pos, block)));
            pos += block;
        }

        Assert.Equal(9f, output[4]);
        Assert.Equal(9f, output[1005]);
        Assert.Equal(expected, output[5..1005]);
    }

    [Fact]
    public void TeeSampleProvider_Read_CopiesToTapOnlyWhenActive()
    {
        var input = Ramp(300);
        var tap = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(Sr, 1), TimeSpan.FromSeconds(1));
        var tee = new TeeSampleProvider(new ArrayProvider(input), tap);
        var output = new float[100];

        tee.Read(output);
        Assert.Equal(0, tap.BufferedBytes);

        tee.Active = true;
        Assert.Equal(100, tee.Read(output));
        Assert.Equal(input[100..200], output);
        Assert.Equal(400, tap.BufferedBytes);

        var tapped = new float[100];
        tap.ToSampleProvider().Read(tapped);
        Assert.Equal(output, tapped);
    }

    [Fact]
    public void SampleLoopProvider_Read_WrapsAroundTheSample()
    {
        var loop = new PreviewPlayer.SampleLoopProvider([1f, 2f, 3f], Sr);
        var output = new float[7];

        Assert.Equal(7, loop.Read(output));
        Assert.Equal([1f, 2f, 3f, 1f, 2f, 3f, 1f], output);
    }

    [Fact]
    public void DeviceFormatProvider_Read_PutsMonoOnFrontLeftAndRightOnly()
    {
        var mix = WaveFormat.CreateIeeeFloatWaveFormat(Sr, 6);
        var provider = new PreviewPlayer.DeviceFormatProvider(new ArrayProvider([0.5f, -0.25f]), mix);
        var bytes = new byte[2 * 6 * 4];

        Assert.Equal(bytes.Length, provider.Read(bytes));

        var frames = new float[12];
        Buffer.BlockCopy(bytes, 0, frames, 0, bytes.Length);
        Assert.Equal([0.5f, 0.5f, 0, 0, 0, 0, -0.25f, -0.25f, 0, 0, 0, 0], frames);
    }

    /// <summary>Plays an array once, then reports the end of the stream.</summary>
    private sealed class ArrayProvider(float[] data) : ISampleProvider
    {
        private int _pos;

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(Sr, 1);

        public int Read(Span<float> buffer)
        {
            int n = Math.Min(buffer.Length, data.Length - _pos);
            data.AsSpan(_pos, n).CopyTo(buffer);
            _pos += n;
            return n;
        }
    }
}
