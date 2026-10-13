using System;

namespace MicForge.Audio;

/// <summary>
/// A snapshot of an audio endpoint for the UI. The name is read once (from the COM
/// property store) at enumeration time, so binding a ComboBox to these never touches
/// COM again — unlike binding directly to <c>MMDevice.FriendlyName</c>, which re-queries
/// the endpoint on every render pass and makes the device pickers laggy.
/// <para>
/// Two snapshots of the same endpoint are equal (by id). The device lists are rebuilt
/// with new snapshots on every refresh, and a bound ComboBox clears its selection when
/// the selected item is not in the new list, which would set the selected device to null.
/// </para>
/// </summary>
public sealed class DeviceInfo : IEquatable<DeviceInfo>
{
    public string Id { get; }
    public string Name { get; }

    public DeviceInfo(string id, string name)
    {
        Id = id;
        Name = name;
    }

    public bool Equals(DeviceInfo other) => other is not null && string.Equals(Id, other.Id, StringComparison.Ordinal);
    public override bool Equals(object obj) => Equals(obj as DeviceInfo);
    public override int GetHashCode() => Id is null ? 0 : StringComparer.Ordinal.GetHashCode(Id);

    public override string ToString() => Name;
}
