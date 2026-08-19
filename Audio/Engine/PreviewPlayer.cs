using System;
using MicForge;
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
    private float[] _sample = Array.Empty<float>();
    private WasapiOut _out;

    public bool Playing { get; private set; }

    public PreviewPlayer(DspChain chain)
    {
        _chain = chain;
    }

    /// <summary>Start playback of the given sample to the device id (falls back to default render).</summary>
    public void Start(string deviceId, float[] sample)
    {
        Stop();
        _sample = sample ?? Array.Empty<float>();
        if (_sample.Length == 0) return;

        using var en = new MMDeviceEnumerator();
        MMDevice device = null;
        if (!string.IsNullOrEmpty(deviceId))
            try { device = en.GetDevice(deviceId); } catch { device = null; }
        device ??= en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        if (device == null) return;

        var loop = new SampleLoopProvider(_sample, AudioEngine.SampleRate);
        var dsp = new DspSampleProvider(loop, _chain);

        var mix = device.AudioClient.MixFormat;
        ISampleProvider mono = dsp;
        if (mix.SampleRate != AudioEngine.SampleRate)
            mono = new WdlResamplingSampleProvider(mono, mix.SampleRate);

        _out = new WasapiOut(device, AudioClientShareMode.Shared, true, 60);
        // Shared-mode mixes are 32-bit float; render straight into the device's exact
        // (often multi-channel, WaveFormatExtensible) format so nothing gets up-mixed off
        // the actual speakers — a plain stereo stream can end up inaudible on a 7.1 device.
        if (mix.BitsPerSample == 32)
            _out.Init(new DeviceFormatProvider(mono, mix));
        else if (mix.Channels > 1)
            _out.Init(new MonoToStereoSampleProvider(mono));
        else
            _out.Init(mono);
        _out.Play();
        Playing = true;
        Log.Info($"Preview playing: {device.FriendlyName} ({mix.SampleRate} Hz, {mix.Channels} ch, {mix.BitsPerSample}-bit), {_sample.Length} samples");
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

    /// <summary>
    /// Renders a mono float source into the device's exact 32-bit-float format (any channel
    /// count / WaveFormatExtensible), placing the signal on front L/R and silence elsewhere.
    /// Matching the device format exactly avoids WASAPI up-mixing the audio off the speakers.
    /// </summary>
    private sealed class DeviceFormatProvider : IWaveProvider
    {
        private readonly ISampleProvider _mono;
        private readonly int _ch;
        private float[] _buf = Array.Empty<float>();

        public DeviceFormatProvider(ISampleProvider mono, WaveFormat mixFormat)
        {
            _mono = mono;
            _ch = mixFormat.Channels;
            WaveFormat = mixFormat;
        }

        public WaveFormat WaveFormat { get; }

        public int Read(byte[] buffer, int offset, int count)
        {
            int frames = count / (_ch * 4);
            if (_buf.Length < frames) _buf = new float[frames];
            int got = _mono.Read(_buf, 0, frames);

            int pos = offset;
            for (int i = 0; i < got; i++)
            {
                float s = _buf[i];
                for (int c = 0; c < _ch; c++)
                {
                    float v = c < 2 ? s : 0f;
                    var bytes = BitConverter.GetBytes(v);
                    buffer[pos++] = bytes[0]; buffer[pos++] = bytes[1];
                    buffer[pos++] = bytes[2]; buffer[pos++] = bytes[3];
                }
            }
            return got * _ch * 4;
        }
    }

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
