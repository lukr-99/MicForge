using MicForge.Audio;

namespace MicForge.Tests;

public sealed class DspChainTests
{
    private const double Sr = 48000;

    private static float[] Sine(int n, double amp = 0.3, double hz = 440)
    {
        var b = new float[n];
        for (int i = 0; i < n; i++) b[i] = (float)(amp * Math.Sin(2 * Math.PI * hz * i / Sr));
        return b;
    }

    [Fact]
    public void Process_NonFiniteInput_DoesNotPoisonFilterStages()
    {
        // Only the high-pass biquad runs. Without an input guard, one NaN would stay in its
        // filter state forever and every later sample would be NaN.
        var chain = new DspChain(Sr);
        foreach (var p in chain.DefaultOrder) p.Enabled = false;
        chain.HighPass.Enabled = true;

        var buf = Sine(960);
        buf[10] = float.NaN;
        buf[20] = float.PositiveInfinity;
        buf[30] = float.NegativeInfinity;
        chain.Process(buf, 0, buf.Length);

        var next = Sine(4800);
        chain.Process(next, 0, next.Length);

        Assert.All(buf, s => Assert.True(float.IsFinite(s)));
        Assert.All(next, s => Assert.True(float.IsFinite(s)));
        Assert.True(next.Max(Math.Abs) > 0.1f, "audio went silent after a non-finite sample");
    }
}
