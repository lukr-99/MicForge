namespace MicForge;

/// <summary>Processing status in words for the tray menu and accessible tooltip.</summary>
public sealed record TrayStatus(bool Muted, bool Processing, bool Reconnecting)
{
    public string ToolTip => Muted ? "MicForge: muted"
        : Reconnecting ? "MicForge: reconnecting"
        : Processing ? "MicForge: live" : "MicForge: stopped";
}
