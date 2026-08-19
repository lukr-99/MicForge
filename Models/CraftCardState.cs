namespace MicForge;

/// <summary>Serializable on/off + intensity state of a Crafting card (by id).</summary>
public sealed class CraftCardState
{
    public string Id { get; set; }
    public bool Enabled { get; set; }
    public double Intensity { get; set; } = 100;
}
