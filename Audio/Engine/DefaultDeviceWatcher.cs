using System;
using NAudio.CoreAudioApi;

namespace MicForge.Audio;

/// <summary>
/// Watches Windows for a change of the default capture (microphone) device and raises an
/// event so the app can follow it. Uses NAudio's endpoint notification client; the
/// callbacks arrive on a background thread, so subscribers must marshal to the UI thread.
/// </summary>
public sealed class DefaultDeviceWatcher : IDisposable
{
    private MMDeviceEnumerator _enum;
    private MMDeviceNotificationClient _client;
    private string _lastId;

    /// <summary>Raised with the new default capture device id.</summary>
    public event Action<string> DefaultCaptureChanged;

    public void Start()
    {
        if (_enum != null) return;
        _enum = new MMDeviceEnumerator();
        // Raise on the Core Audio thread, as before, not on the thread that called Start.
        _client = _enum.CreateNotificationClient(useSynchronizationContext: false);
        _client.DefaultDeviceChanged += (_, e) => OnDefaultDeviceChanged(e.Flow, e.Role, e.DeviceId);
    }

    public void Dispose()
    {
        try { _client?.Dispose(); } catch { }
        try { _enum?.Dispose(); } catch { }
        _client = null;
        _enum = null;
    }

    internal void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        if (flow != DataFlow.Capture) return;
        if (role != Role.Communications && role != Role.Console) return;
        if (string.IsNullOrEmpty(defaultDeviceId) || defaultDeviceId == _lastId) return;
        _lastId = defaultDeviceId;
        DefaultCaptureChanged?.Invoke(defaultDeviceId);
    }
}
