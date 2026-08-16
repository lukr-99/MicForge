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
/// Optionally also mirrors the processed audio to a second device (headphone monitor).
/// </summary>
public sealed class AudioEngine : IDisposable
{
    public const int SampleRate = 48000;

    private WasapiCapture _capture;
    private WasapiOut _output;
    private BufferedWaveProvider _buffer;

    private TeeSampleProvider _tee;
    private BufferedWaveProvider _monBuffer;
    private WasapiOut _monitor;
    private MMDevice _monDevice;
    private bool _monEnabled;

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

    public static string DefaultOutputId()
    {
        using var en = new MMDeviceEnumerator();
        try { return en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).ID; }
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

        // Tap the processed signal so a monitor output can mirror it.
        _monBuffer = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 1))
        {
            DiscardOnBufferOverflow = true,
            BufferDuration = TimeSpan.FromSeconds(1)
        };
        _tee = new TeeSampleProvider(dsp, _monBuffer);

        // Mono 48 kHz -> output device mix format.
        var mix = output.AudioClient.MixFormat;
        ISampleProvider outProv = _tee;
        if (mix.SampleRate != SampleRate)
            outProv = new WdlResamplingSampleProvider(outProv, mix.SampleRate);
        if (mix.Channels >= 2)
            outProv = new MonoToStereoSampleProvider(outProv);

        _output = new WasapiOut(output, AudioClientShareMode.Shared, true, 50);
        _output.Init(outProv);

        _capture.StartRecording();
        _output.Play();
        Running = true;

        ApplyMonitor();
    }

    /// <summary>Set which device (if any) mirrors the processed audio. Applies live.</summary>
    public void ConfigureMonitor(MMDevice device, bool enabled)
    {
        _monDevice = device;
        _monEnabled = enabled;
        if (Running) ApplyMonitor();
    }

    private void ApplyMonitor()
    {
        StopMonitorOutput();
        if (_tee == null) return;

        if (_monEnabled && _monDevice != null)
        {
            _monBuffer.ClearBuffer();
            _tee.Active = true;
            try { StartMonitorOutput(_monDevice); }
            catch { _tee.Active = false; StopMonitorOutput(); }
        }
        else
        {
            _tee.Active = false;
        }
    }

    private void StartMonitorOutput(MMDevice device)
    {
        var mix = device.AudioClient.MixFormat;
        ISampleProvider mp = _monBuffer.ToSampleProvider();
        if (mix.SampleRate != SampleRate)
            mp = new WdlResamplingSampleProvider(mp, mix.SampleRate);
        if (mix.Channels >= 2)
            mp = new MonoToStereoSampleProvider(mp);

        _monitor = new WasapiOut(device, AudioClientShareMode.Shared, true, 60);
        _monitor.Init(mp);
        _monitor.Play();
    }

    private void StopMonitorOutput()
    {
        try { _monitor?.Stop(); } catch { }
        _monitor?.Dispose();
        _monitor = null;
    }

    public void Stop()
    {
        StopMonitorOutput();
        try { _output?.Stop(); } catch { }
        try { _capture?.StopRecording(); } catch { }
        _output?.Dispose(); _output = null;
        _capture?.Dispose(); _capture = null;
        _buffer = null;
        _tee = null;
        _monBuffer = null;
        Chain.Reset();
        Running = false;
    }

    public void Dispose() => Stop();
}
