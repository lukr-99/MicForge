using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MicForge.Audio;

/// <summary>
/// Captures from a WASAPI input device, converts to mono 48 kHz float, runs the DSP
/// chain, and renders to a WASAPI output device (normally the VB-CABLE virtual input).
/// Optionally mirrors the processed audio to a second device (headphone monitor), and
/// automatically reconnects if the input/output device is unplugged or changes.
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
    private string _monId, _monName;
    private bool _monEnabled;

    // Reconnect state.
    private string _inId, _inName, _outId, _outName;
    private bool _userStopped = true;
    private volatile bool _recovering;
    private readonly object _lock = new();
    private System.Timers.Timer _recoveryTimer;

    public DspChain Chain { get; } = new(SampleRate);
    public volatile bool Running;
    public volatile bool Reconnecting;

    // Reliability telemetry.
    public volatile int Glitches;                 // dropped-buffer / stall events this session
    private long _lastDataTicks;
    private System.Timers.Timer _watchdog;

    // Public lists for the UI: lightweight POCOs whose names are read once here, so
    // binding a ComboBox to them never re-queries COM (the source of the picker lag).
    public static List<DeviceInfo> InputDevices() => ToInfo(Enumerate(DataFlow.Capture));
    public static List<DeviceInfo> OutputDevices() => ToInfo(Enumerate(DataFlow.Render));

    private static List<DeviceInfo> ToInfo(List<MMDevice> devs)
    {
        var list = new List<DeviceInfo>(devs.Count);
        foreach (var d in devs)
        {
            list.Add(new DeviceInfo(d.ID, d.FriendlyName));
            d.Dispose();
        }
        return list;
    }

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

    public void Start(DeviceInfo input, DeviceInfo output)
    {
        lock (_lock)
        {
            _userStopped = false;
            _recovering = false;
            var inDev = Resolve(Enumerate(DataFlow.Capture), input?.Id, input?.Name);
            var outDev = Resolve(Enumerate(DataFlow.Render), output?.Id, output?.Name);
            if (inDev == null || outDev == null)
                throw new InvalidOperationException("The selected audio device is no longer available.");
            _inId = inDev.ID; _inName = inDev.FriendlyName;
            _outId = outDev.ID; _outName = outDev.FriendlyName;
            StopStreams();
            StartCore(inDev, outDev);
        }
    }

    private void StartCore(MMDevice input, MMDevice output)
    {
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
        _capture.DataAvailable += (_, a) =>
        {
            if (_buffer.BufferedBytes + a.BytesRecorded > _buffer.BufferLength) Glitches++;
            _buffer.AddSamples(a.Buffer, 0, a.BytesRecorded);
            _lastDataTicks = Environment.TickCount64;
        };
        _capture.RecordingStopped += OnStopped;

        // Capture format -> mono 48 kHz float.
        ISampleProvider sp = _buffer.ToSampleProvider();
        if (sp.WaveFormat.Channels == 2)
            sp = new StereoToMonoSampleProvider(sp) { LeftVolume = 0.5f, RightVolume = 0.5f };
        if (sp.WaveFormat.SampleRate != SampleRate)
            sp = new WdlResamplingSampleProvider(sp, SampleRate);

        var dsp = new DspSampleProvider(sp, Chain);

        _monBuffer = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 1))
        {
            DiscardOnBufferOverflow = true,
            BufferDuration = TimeSpan.FromSeconds(1)
        };
        _tee = new TeeSampleProvider(dsp, _monBuffer);

        var mix = output.AudioClient.MixFormat;
        ISampleProvider outProv = _tee;
        if (mix.SampleRate != SampleRate)
            outProv = new WdlResamplingSampleProvider(outProv, mix.SampleRate);
        if (mix.Channels >= 2)
            outProv = new MonoToStereoSampleProvider(outProv);

        _output = new WasapiOut(output, AudioClientShareMode.Shared, true, 50);
        _output.PlaybackStopped += OnStopped;
        _output.Init(outProv);

        _lastDataTicks = Environment.TickCount64;
        _capture.StartRecording();
        _output.Play();

        Running = true;
        Reconnecting = false;
        _recovering = false;

        EnsureWatchdog();
        _watchdog.Start();
        ApplyMonitor();
    }

    private void EnsureWatchdog()
    {
        if (_watchdog != null) return;
        _watchdog = new System.Timers.Timer(1000) { AutoReset = true };
        _watchdog.Elapsed += (_, _) =>
        {
            if (!Running || _recovering || _userStopped) return;
            if (Environment.TickCount64 - _lastDataTicks > 2000)
            {
                Log.Warn("Audio stall detected (no capture data for 2s) — restarting the stream.");
                Glitches++;
                BeginRecovery();
            }
        };
    }

    // ---- reconnect ----

    private void OnStopped(object sender, StoppedEventArgs e)
    {
        // Only react to unexpected stops (device lost); clean stops carry no exception.
        if (_userStopped || e?.Exception == null) return;
        BeginRecovery();
    }

    private void BeginRecovery()
    {
        if (_recovering) return;
        _recovering = true;
        Running = false;
        Reconnecting = true;

        // Never dispose the audio objects inside their own stopped-event; hop off it.
        Task.Run(() =>
        {
            lock (_lock)
            {
                if (_userStopped) { _recovering = false; return; }
                StopStreams();
                EnsureRecoveryTimer();
                _recoveryTimer.Start();
            }
        });
    }

    private void EnsureRecoveryTimer()
    {
        if (_recoveryTimer != null) return;
        _recoveryTimer = new System.Timers.Timer(1500) { AutoReset = true };
        _recoveryTimer.Elapsed += RecoveryTick;
    }

    private void RecoveryTick(object sender, ElapsedEventArgs e)
    {
        if (!Monitor.TryEnter(_lock)) return;
        try
        {
            if (_userStopped || Running) { _recoveryTimer.Stop(); return; }

            var inDev = Resolve(Enumerate(DataFlow.Capture), _inId, _inName);
            var outDev = Resolve(Enumerate(DataFlow.Render), _outId, _outName);
            if (inDev == null || outDev == null) return;   // devices not back yet; keep waiting

            StopStreams();
            StartCore(inDev, outDev);
            _recoveryTimer.Stop();
        }
        catch
        {
            // Device came back mid-init or format not ready; keep retrying.
        }
        finally { Monitor.Exit(_lock); }
    }

    private static MMDevice Resolve(List<MMDevice> list, string id, string name)
        => list.FirstOrDefault(d => d.ID == id) ?? list.FirstOrDefault(d => d.FriendlyName == name);

    // ---- monitor ----

    public void ConfigureMonitor(DeviceInfo device, bool enabled)
    {
        _monId = device?.Id;
        _monName = device?.Name;
        _monEnabled = enabled;
        lock (_lock) { if (Running) ApplyMonitor(); }
    }

    private void ApplyMonitor()
    {
        StopMonitorOutput();
        if (_tee == null) return;

        if (_monEnabled && !string.IsNullOrEmpty(_monId))
        {
            _monBuffer.ClearBuffer();
            _tee.Active = true;
            try { StartMonitorOutput(); }
            catch { _tee.Active = false; StopMonitorOutput(); }
        }
        else
        {
            _tee.Active = false;
        }
    }

    private void StartMonitorOutput()
    {
        var device = Resolve(Enumerate(DataFlow.Render), _monId, _monName);
        if (device == null) return;
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

    // ---- lifecycle ----

    private void StopStreams()
    {
        StopMonitorOutput();
        try { if (_output != null) _output.PlaybackStopped -= OnStopped; } catch { }
        try { if (_capture != null) _capture.RecordingStopped -= OnStopped; } catch { }
        try { _output?.Stop(); } catch { }
        try { _capture?.StopRecording(); } catch { }
        _output?.Dispose(); _output = null;
        _capture?.Dispose(); _capture = null;
        _buffer = null;
        _tee = null;
        _monBuffer = null;
    }

    public void Stop()
    {
        lock (_lock)
        {
            _userStopped = true;
            _recovering = false;
            Reconnecting = false;
            _recoveryTimer?.Stop();
            _watchdog?.Stop();
            StopStreams();
            Chain.Reset();
            Running = false;
        }
    }

    public void Dispose() => Stop();
}
