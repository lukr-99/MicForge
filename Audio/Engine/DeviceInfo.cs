namespace MicForge.Audio;

/// <summary>
/// A snapshot of an audio endpoint for the UI. The name is read once (from the COM
/// property store) at enumeration time, so binding a ComboBox to these never touches
/// COM again — unlike binding directly to <c>MMDevice.FriendlyName</c>, which re-queries
/// the endpoint on every render pass and makes the device pickers laggy.
/// </summary>
public sealed class DeviceInfo
{
    public string Id { get; }
    public string Name { get; }

    public DeviceInfo(string id, string name)
    {
        Id = id;
        Name = name;
    }

    public override string ToString() => Name;
}
