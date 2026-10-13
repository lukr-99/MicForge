using MicForge.Audio;
using NAudio.CoreAudioApi;

namespace MicForge.Tests;

public sealed class DefaultDeviceWatcherTests
{
    [Fact]
    public void OnDefaultDeviceChanged_RaisesOncePerNewCaptureDevice()
    {
        var watcher = new DefaultDeviceWatcher();
        var raised = new List<string>();
        watcher.DefaultCaptureChanged += raised.Add;

        watcher.OnDefaultDeviceChanged(DataFlow.Render, Role.Communications, "speakers");
        watcher.OnDefaultDeviceChanged(DataFlow.Capture, Role.Multimedia, "mic-a");
        watcher.OnDefaultDeviceChanged(DataFlow.Capture, Role.Communications, "mic-a");
        watcher.OnDefaultDeviceChanged(DataFlow.Capture, Role.Console, "mic-a");
        watcher.OnDefaultDeviceChanged(DataFlow.Capture, Role.Console, "");
        watcher.OnDefaultDeviceChanged(DataFlow.Capture, Role.Console, "mic-b");

        Assert.Equal(["mic-a", "mic-b"], raised);
    }
}
