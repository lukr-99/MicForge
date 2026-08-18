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
    public Saturation Saturation { get; }
    public Limiter Limiter { get; }
    public GainStage OutputGain { get; }
    public LoudnessProcessor Loudness { get; }

    public volatile float InputPeak;
    public volatile float OutputPeak;
    public volatile bool Bypass;
    public volatile bool Mute;

    public const int SpectrumSamples = 2048;
    private readonly float[] _spec = new float[SpectrumSamples];
    private readonly float[] _specOut = new float[SpectrumSamples];
    private int _specPos;
    private int _specOutPos;
    private readonly object _specLock = new();

    public DspChain(double sampleRate)
    {
        InputGain = new GainStage("Input Gain");
        HighPass = new HighPassStage(sampleRate);
        Suppressor = new NoiseSuppressor();
        Gate = new NoiseGate(sampleRate);
        Eq = new ParametricEq(sampleRate);
        Compressor = new Compressor(sampleRate);
        DeEsser = new DeEsser(sampleRate);
        Saturation = new Saturation();
        Limiter = new Limiter(sampleRate);
        OutputGain = new GainStage("Output Gain");
        Loudness = new LoudnessProcessor(sampleRate);

        _chain = new IAudioProcessor[]
        {
            InputGain, HighPass, Suppressor, Gate, Eq, Compressor, DeEsser, Saturation, Limiter, OutputGain, Loudness
        };
    }

    public void Process(float[] buffer, int offset, int count)
    {
        // Snapshot the raw input for the spectrum analyzer.
        lock (_specLock)
        {
            for (int i = offset; i < offset + count; i++)
            {
                _spec[_specPos] = buffer[i];
                if (++_specPos == SpectrumSamples) _specPos = 0;
            }
        }

        float ip = 0;
        for (int i = offset; i < offset + count; i++)
        {
            float a = Math.Abs(buffer[i]);
            if (a > ip) ip = a;
        }
        InputPeak = ip;

        if (Bypass)
        {
            if (Mute) { Array.Clear(buffer, offset, count); OutputPeak = 0; }
            else OutputPeak = ip;
            CaptureOut(buffer, offset, count);
            return;
        }

        foreach (var p in _chain) p.Process(buffer, offset, count);

        if (Mute)
        {
            Array.Clear(buffer, offset, count);
            OutputPeak = 0;
        }
        else
        {
            float op = 0;
            for (int i = offset; i < offset + count; i++)
            {
                float a = Math.Abs(buffer[i]);
                if (a > op) op = a;
            }
            OutputPeak = op;
        }
        CaptureOut(buffer, offset, count);
    }

    private void CaptureOut(float[] buffer, int offset, int count)
    {
        lock (_specLock)
        {
            for (int i = offset; i < offset + count; i++)
            {
                _specOut[_specOutPos] = buffer[i];
                if (++_specOutPos == SpectrumSamples) _specOutPos = 0;
            }
        }
    }

    public void Reset()
    {
        foreach (var p in _chain) p.Reset();
        InputPeak = 0;
        OutputPeak = 0;
    }

    /// <summary>Copies the most recent input samples (oldest-first) for spectrum analysis.</summary>
    public void CopySpectrum(float[] dest) => Copy(_spec, _specPos, dest);

    /// <summary>Copies the most recent processed-output samples (oldest-first).</summary>
    public void CopyOutputSpectrum(float[] dest) => Copy(_specOut, _specOutPos, dest);

    private void Copy(float[] ring, int pos, float[] dest)
    {
        int n = dest.Length;
        lock (_specLock)
        {
            int idx = pos - n;
            while (idx < 0) idx += SpectrumSamples;
            for (int i = 0; i < n; i++)
            {
                dest[i] = ring[idx];
                if (++idx == SpectrumSamples) idx = 0;
            }
        }
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
