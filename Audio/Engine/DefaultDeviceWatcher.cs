using System;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace MicForge.Audio;

/// <summary>
/// Watches Windows for a change of the default capture (microphone) device and raises an
/// event so the app can follow it. Registered with the Core Audio endpoint enumerator; the
/// callbacks arrive on a background thread, so subscribers must marshal to the UI thread.
/// </summary>
public sealed class DefaultDeviceWatcher : IMMNotificationClient, IDisposable
{
    private MMDeviceEnumerator _enum;
    private string _lastId;

    /// <summary>Raised with the new default capture device id.</summary>
    public event Action<string> DefaultCaptureChanged;

    public void Start()
    {
        if (_enum != null) return;
        _enum = new MMDeviceEnumerator();
        _enum.RegisterEndpointNotificationCallback(this);
    }

    public void Dispose()
    {
        try { _enum?.UnregisterEndpointNotificationCallback(this); } catch { }
        try { _enum?.Dispose(); } catch { }
        _enum = null;
    }

    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        if (flow != DataFlow.Capture) return;
        if (role != Role.Communications && role != Role.Console) return;
        if (string.IsNullOrEmpty(defaultDeviceId) || defaultDeviceId == _lastId) return;
        _lastId = defaultDeviceId;
        DefaultCaptureChanged?.Invoke(defaultDeviceId);
    }

    public void OnDeviceStateChanged(string deviceId, DeviceState newState) { }
    public void OnDeviceAdded(string pwstrDeviceId) { }
    public void OnDeviceRemoved(string deviceId) { }
    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
}
