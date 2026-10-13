using MicForge.Audio;

namespace MicForge.Tests;

public sealed class LimiterTests
{
    private const double Sr = 48000;
    private const int Lookahead = 96; // 2 ms at 48 kHz

    private static Limiter Make(double ceilingDb = -1.0, double releaseMs = 60)
        => new(Sr) { CeilingDb = ceilingDb, ReleaseMs = releaseMs, LookaheadMs = 2.0 };

    private static float[] Sine(int n, double amp, double hz = 440)
    {
        var b = new float[n];
        for (int i = 0; i < n; i++) b[i] = (float)(amp * Math.Sin(2 * Math.PI * hz * i / Sr));
        return b;
    }

    private static float[] Constant(int n, float v)
    {
        var b = new float[n];
        Array.Fill(b, v);
        return b;
    }

    [Fact]
    public void Process_SignalBelowCeiling_IsDelayedButUnchanged()
    {
        var input = Sine(4800, 0.3);
        var buf = (float[])input.Clone();
        var lim = Make();

        lim.Process(buf, 0, buf.Length);

        for (int i = Lookahead; i < buf.Length; i++)
            Assert.Equal(input[i - Lookahead], buf[i], 6);
        Assert.Equal(0, lim.GainReductionDb);
    }

    [Fact]
    public void Process_SignalBelowCeiling_FirstLookaheadSamplesAreSilence()
    {
        var buf = Sine(1000, 0.3);
        Make().Process(buf, 0, buf.Length);

        for (int i = 0; i < Lookahead; i++) Assert.Equal(0f, buf[i]);
    }

    [Fact]
    public void Process_SustainedOverCeiling_SettlesAtOrBelowCeiling()
    {
        double ceil = Math.Pow(10, -1.0 / 20);
        var buf = Constant(48000, 1.0f);
        Make().Process(buf, 0, buf.Length);

        for (int i = 24000; i < buf.Length; i++)
            Assert.True(Math.Abs(buf[i]) <= ceil + 1e-4, $"sample {i} = {buf[i]}");
        Assert.Equal(ceil, buf[^1], 3);
    }

    [Fact]
    public void Process_LoudSineAfterAttack_StaysUnderCeiling()
    {
        double ceil = Math.Pow(10, -3.0 / 20);
        var buf = Sine(48000, 1.0);
        Make(ceilingDb: -3).Process(buf, 0, buf.Length);

        float peak = 0;
        for (int i = 24000; i < buf.Length; i++) peak = Math.Max(peak, Math.Abs(buf[i]));
        // Release ripple between sine peaks lets later peaks poke ~1% over the ceiling.
        Assert.True(peak <= ceil * 1.02, $"peak {peak} > ceiling {ceil}");
    }

    [Fact]
    public void Process_InitialOvershoot_IsBoundedByLookaheadRamp()
    {
        // Gain reaches ~90% of the needed reduction when the peak leaves the delay,
        // so the first peak may exceed the ceiling slightly but never by much.
        double ceil = Math.Pow(10, -1.0 / 20);
        var buf = Constant(2000, 1.0f);
        Make().Process(buf, 0, buf.Length);

        float peak = buf.Max(Math.Abs);
        Assert.True(peak <= ceil * 1.02, $"first peak {peak} overshoots ceiling {ceil}");
    }

    [Fact]
    public void Process_OverCeiling_ReportsPositiveGainReduction()
    {
        var buf = Constant(4800, 1.0f);
        var lim = Make();
        lim.Process(buf, 0, buf.Length);

        Assert.InRange(lim.GainReductionDb, 0.5, 1.01); // about 1 dB for -1 dB ceiling
    }

    [Fact]
    public void Process_Attack_GainFallsMonotonicallyOverLookahead()
    {
        var lim = Make();
        var buf = Constant(Lookahead * 2, 1.0f);
        lim.Process(buf, 0, buf.Length);

        // Delayed samples of 1.0 appear after the lookahead; gain applied to them must not rise.
        for (int i = Lookahead + 1; i < buf.Length; i++)
            Assert.True(buf[i] <= buf[i - 1] + 1e-7f, $"gain rose at {i}");
    }

    [Fact]
    public void Process_AfterLoudBurst_ReleaseRestoresUnityGain()
    {
        var lim = Make(releaseMs: 20);
        var burst = Constant(2400, 1.0f);
        lim.Process(burst, 0, burst.Length);

        var quiet = Constant(48000, 0.2f);
        lim.Process(quiet, 0, quiet.Length);

        Assert.Equal(0.2f, quiet[^1], 3);

        var more = Constant(480, 0.2f);
        lim.Process(more, 0, more.Length);
        Assert.True(lim.GainReductionDb < 0.01);
    }

    [Fact]
    public void Process_Release_IsSlowerWithLongerReleaseTime()
    {
        float AfterBurst(double releaseMs)
        {
            var lim = Make(releaseMs: releaseMs);
            var burst = Constant(2400, 1.0f);
            lim.Process(burst, 0, burst.Length);
            var quiet = Constant(1200, 0.2f);
            lim.Process(quiet, 0, quiet.Length);
            return quiet[^1];
        }

        Assert.True(AfterBurst(10) > AfterBurst(500));
    }

    [Fact]
    public void Process_Silence_StaysSilentWithNoGainReduction()
    {
        var buf = new float[2048];
        var lim = Make();
        lim.Process(buf, 0, buf.Length);

        Assert.All(buf, s => Assert.Equal(0f, s));
        Assert.Equal(0, lim.GainReductionDb);
    }

    [Fact]
    public void Process_EmptyBuffer_DoesNothing()
    {
        var lim = Make();
        lim.Process(Array.Empty<float>(), 0, 0);
        Assert.Equal(0, lim.GainReductionDb);
    }

    [Fact]
    public void Process_RespectsOffsetAndCount()
    {
        var buf = Constant(10, 0.5f);
        Make().Process(buf, 4, 2);

        Assert.Equal(0.5f, buf[3]);
        Assert.Equal(0.5f, buf[6]);
    }

    [Fact]
    public void Process_Disabled_LeavesBufferUntouchedAndClearsMeter()
    {
        var lim = Make();
        var loud = Constant(2400, 1.0f);
        lim.Process(loud, 0, loud.Length);
        Assert.True(lim.GainReductionDb > 0);

        lim.Enabled = false;
        var buf = Constant(100, 1.0f);
        lim.Process(buf, 0, buf.Length);

        Assert.All(buf, s => Assert.Equal(1.0f, s));
        Assert.Equal(0, lim.GainReductionDb);
    }

    [Fact]
    public void Process_AcrossBlocks_MatchesSingleBlock()
    {
        var input = Sine(4800, 1.2);
        var whole = (float[])input.Clone();
        Make().Process(whole, 0, whole.Length);

        var split = (float[])input.Clone();
        var lim = Make();
        for (int o = 0; o < split.Length; o += 480) lim.Process(split, o, 480);

        for (int i = 0; i < whole.Length; i++) Assert.Equal(whole[i], split[i], 6);
    }

    [Fact]
    public void Reset_ClearsDelayLineGainAndMeter()
    {
        var lim = Make();
        var loud = Constant(2400, 1.0f);
        lim.Process(loud, 0, loud.Length);

        lim.Reset();
        var buf = Constant(400, 0.3f);
        lim.Process(buf, 0, buf.Length);

        Assert.Equal(0, lim.GainReductionDb);
        for (int i = 0; i < Lookahead; i++) Assert.Equal(0f, buf[i]);
        Assert.Equal(0.3f, buf[^1], 6);
    }

    [Fact]
    public void Process_ChangingLookahead_ResizesDelayWithoutThrowing()
    {
        var lim = Make();
        var a = Constant(500, 0.3f);
        lim.Process(a, 0, a.Length);

        lim.LookaheadMs = 5.0;
        var b = Constant(1000, 0.3f);
        lim.Process(b, 0, b.Length);

        Assert.Equal(0.3f, b[^1], 6);
    }

    [Fact]
    public void Process_NaNSample_DoesNotPoisonLaterAudio()
    {
        var lim = Make();
        var buf = Constant(1000, 0.3f);
        buf[10] = float.NaN;
        lim.Process(buf, 0, buf.Length);

        var next = Constant(2000, 0.3f);
        lim.Process(next, 0, next.Length);

        Assert.All(next, s => Assert.False(float.IsNaN(s)));
        Assert.Equal(0.3f, next[^1], 5);
    }

    [Fact]
    public void Process_NaNSample_DoesNotDisableLimitingOfLaterPeaks()
    {
        double ceil = Math.Pow(10, -1.0 / 20);
        var lim = Make();
        var buf = Constant(1000, 0.3f);
        buf[10] = float.NaN;
        lim.Process(buf, 0, buf.Length);

        var loud = Constant(48000, 1.0f);
        lim.Process(loud, 0, loud.Length);

        Assert.True(Math.Abs(loud[^1]) <= ceil + 1e-3, $"limiter blind after NaN: {loud[^1]}");
    }
}
