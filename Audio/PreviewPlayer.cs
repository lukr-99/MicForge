using System;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MicForge.Audio;

/// <summary>
/// Plays the built-in synthetic voice sample (looped) through the shared DSP chain to a
/// chosen output device, so the user can preview Crafting/EQ changes on a standard voice
/// instead of their own. Mutually exclusive with live mic capture (both drive the one chain).
/// </summary>
public sealed class PreviewPlayer : IDisposable
{
    private readonly DspChain _chain;
    private readonly float[] _sample;
    private WasapiOut _out;

    public bool Playing { get; private set; }

    public PreviewPlayer(DspChain chain, float[] sample)
    {
        _chain = chain;
        _sample = sample;
    }

    /// <summary>Start playback to the given device id (falls back to the default render device).</summary>
    public void Start(string deviceId)
    {
        Stop();

        using var en = new MMDeviceEnumerator();
        MMDevice device = null;
        if (!string.IsNullOrEmpty(deviceId))
            try { device = en.GetDevice(deviceId); } catch { device = null; }
        device ??= en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        if (device == null) return;

        var loop = new SampleLoopProvider(_sample, AudioEngine.SampleRate);
        var dsp = new DspSampleProvider(loop, _chain);

        var mix = device.AudioClient.MixFormat;
        ISampleProvider outProv = dsp;
        if (mix.SampleRate != AudioEngine.SampleRate)
            outProv = new WdlResamplingSampleProvider(outProv, mix.SampleRate);
        if (mix.Channels >= 2)
            outProv = new MonoToStereoSampleProvider(outProv);

        _out = new WasapiOut(device, AudioClientShareMode.Shared, true, 60);
        _out.Init(outProv);
        _out.Play();
        Playing = true;
    }

    public void Stop()
    {
        try { _out?.Stop(); } catch { }
        _out?.Dispose();
        _out = null;
        Playing = false;
        _chain.Reset();
    }

    public void Dispose() => Stop();

    private sealed class SampleLoopProvider : ISampleProvider
    {
        private readonly float[] _data;
        private int _pos;

        public SampleLoopProvider(float[] data, int sampleRate)
        {
            _data = data;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            for (int i = 0; i < count; i++)
            {
                buffer[offset + i] = _data[_pos];
                if (++_pos >= _data.Length) _pos = 0;
            }
            return count;
        }
    }
}
