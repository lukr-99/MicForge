using System;
using NAudio.Wave;

namespace MicForge.Audio;

/// <summary>The ordered processing chain plus in/out peak metering.</summary>
public sealed class DspChain
{
    private readonly IAudioProcessor[] _chain;

    public GainStage InputGain { get; }
    public HighPassStage HighPass { get; }
    public NoiseSuppressor Suppressor { get; }
    public NoiseGate Gate { get; }
    public ParametricEq Eq { get; }
    public Compressor Compressor { get; }
    public DeEsser DeEsser { get; }
    public Limiter Limiter { get; }
    public GainStage OutputGain { get; }

    public volatile float InputPeak;
    public volatile float OutputPeak;

    public DspChain(double sampleRate)
    {
        InputGain = new GainStage("Input Gain");
        HighPass = new HighPassStage(sampleRate);
        Suppressor = new NoiseSuppressor();
        Gate = new NoiseGate(sampleRate);
        Eq = new ParametricEq(sampleRate);
        Compressor = new Compressor(sampleRate);
        DeEsser = new DeEsser(sampleRate);
        Limiter = new Limiter(sampleRate);
        OutputGain = new GainStage("Output Gain");

        _chain = new IAudioProcessor[]
        {
            InputGain, HighPass, Suppressor, Gate, Eq, Compressor, DeEsser, Limiter, OutputGain
        };
    }

    public void Process(float[] buffer, int offset, int count)
    {
        float ip = 0;
        for (int i = offset; i < offset + count; i++)
        {
            float a = Math.Abs(buffer[i]);
            if (a > ip) ip = a;
        }
        InputPeak = ip;

        foreach (var p in _chain) p.Process(buffer, offset, count);

        float op = 0;
        for (int i = offset; i < offset + count; i++)
        {
            float a = Math.Abs(buffer[i]);
            if (a > op) op = a;
        }
        OutputPeak = op;
    }

    public void Reset()
    {
        foreach (var p in _chain) p.Reset();
        InputPeak = 0;
        OutputPeak = 0;
    }
}

/// <summary>Wraps a mono float source and runs the DSP chain as it is pulled.</summary>
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
