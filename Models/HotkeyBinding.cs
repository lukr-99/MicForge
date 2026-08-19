namespace MicForge;

/// <summary>Serializable global-hotkey binding: an action id plus its modifiers + virtual key.</summary>
public sealed class HotkeyBinding
{
    public string Action { get; set; }
    public uint Modifiers { get; set; }
    public uint Vk { get; set; }
}
