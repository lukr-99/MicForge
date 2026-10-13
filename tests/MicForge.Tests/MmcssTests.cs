using MicForge.Audio;
using NAudio.Wave;

namespace MicForge.Tests;

public sealed class MmcssTests
{
    [Fact]
    public void MmcssSampleProvider_Read_PassesSamplesThroughUnchanged()
    {
        float[] read = new float[4];
        Exception? error = null;

        // A dedicated thread, so the test never boosts a shared thread-pool thread.
        var thread = new Thread(() =>
        {
            try
            {
                var provider = new MmcssSampleProvider(new ConstantProvider(0.25f));
                provider.Read(read, 0, 2);
                provider.Read(read, 2, 2);
            }
            catch (Exception ex) { error = ex; }
        });
        thread.Start();
        thread.Join();

        Assert.Null(error);
        Assert.All(read, s => Assert.Equal(0.25f, s));
    }

    private sealed class ConstantProvider(float value) : ISampleProvider
    {
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 1);

        public int Read(float[] buffer, int offset, int count)
        {
            Array.Fill(buffer, value, offset, count);
            return count;
        }
    }
}
