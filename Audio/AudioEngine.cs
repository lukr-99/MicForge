using System;
using System.Collections.Generic;
using System.Linq;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MicForge.Audio;

/// <summary>
/// Captures from a WASAPI input device, converts to mono 48 kHz float, runs the DSP
/// chain, and renders to a WASAPI output device (normally the VB-CABLE virtual input).
/// </summary>
public sealed class AudioEngine : IDisposable
{
    public const int SampleRate = 48000;

    private WasapiCapture _capture;
    private WasapiOut _output;
    private BufferedWaveProvider _buffer;

    public DspChain Chain { get; } = new(SampleRate);
    public bool Running { get; private set; }

    public static List<MMDevice> InputDevices() => Enumerate(DataFlow.Capture);
    public static List<MMDevice> OutputDevices() => Enumerate(DataFlow.Render);

    private static List<MMDevice> Enumerate(DataFlow flow)
    {
        using var en = new MMDeviceEnumerator();
        return en.EnumerateAudioEndPoints(flow, DeviceState.Active).ToList();
    }

    public static string DefaultInputId()
    {
        using var en = new MMDeviceEnumerator();
        try { return en.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications).ID; }
        catch { return null; }
    }

    public void Start(MMDevice input, MMDevice output)
    {
        Stop();

        _capture = new WasapiCapture(input, true, 30);
        var capFmt = _capture.WaveFormat;
        if (capFmt.Channels > 2)
            throw new NotSupportedException(
                $"Input device has {capFmt.Channels} channels; please pick a mono or stereo mic.");

        _buffer = new BufferedWaveProvider(capFmt)
        {
            DiscardOnBufferOverflow = true,
            BufferDuration = TimeSpan.FromMilliseconds(500)
        };
        _capture.DataAvailable += (_, a) => _buffer.AddSamples(a.Buffer, 0, a.BytesRecorded);

        // Capture format -> mono 48 kHz float.
        ISampleProvider sp = _buffer.ToSampleProvider();
        if (sp.WaveFormat.Channels == 2)
            sp = new StereoToMonoSampleProvider(sp) { LeftVolume = 0.5f, RightVolume = 0.5f };
        if (sp.WaveFormat.SampleRate != SampleRate)
            sp = new WdlResamplingSampleProvider(sp, SampleRate);

        var dsp = new DspSampleProvider(sp, Chain);

        // Mono 48 kHz -> output device mix format.
        var mix = output.AudioClient.MixFormat;
        ISampleProvider outProv = dsp;
        if (mix.SampleRate != SampleRate)
            outProv = new WdlResamplingSampleProvider(outProv, mix.SampleRate);
        if (mix.Channels >= 2)
            outProv = new MonoToStereoSampleProvider(outProv);

        _output = new WasapiOut(output, AudioClientShareMode.Shared, true, 50);
        _output.Init(outProv);

        _capture.StartRecording();
        _output.Play();
        Running = true;
    }

    public void Stop()
    {
        try { _output?.Stop(); } catch { }
        try { _capture?.StopRecording(); } catch { }
        _output?.Dispose(); _output = null;
        _capture?.Dispose(); _capture = null;
        _buffer = null;
        Chain.Reset();
        Running = false;
    }

    public void Dispose() => Stop();
}
