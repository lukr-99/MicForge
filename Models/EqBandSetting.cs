namespace MicForge;

/// <summary>Serializable state of one equalizer band (part of a <see cref="Settings"/> snapshot).</summary>
public sealed class EqBandSetting
{
    public bool Enabled { get; set; } = true;
    public int Type { get; set; }
    public double Freq { get; set; }
    public double GainDb { get; set; }
    public double Q { get; set; }
}
