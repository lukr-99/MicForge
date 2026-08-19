namespace MicForge.ViewModels;

/// <summary>Serializable definition of one Crafting card (edited via <c>craftcards.json</c>).</summary>
public sealed class CraftCardConfig
{
    public string Id { get; set; }
    public string Icon { get; set; }
    public string Title { get; set; }
    public string Category { get; set; }             // Tone / Polish / Fun / FX
    public string Blurb { get; set; }
    public string Explanation { get; set; }
    public double Pitch { get; set; }                 // semitones
    public double[] Eq { get; set; } = new double[5]; // gains: low shelf, low-mid, mid, presence, air
    public double Drive { get; set; }                 // saturation drive (dB)
    public double Exciter { get; set; }               // harmonic exciter amount (percent)
    public double LowCut { get; set; }                // high-pass cutoff (Hz); 0 = none (thins the voice)
    public double HighCut { get; set; }               // low-pass cutoff (Hz); 0 = none (muffles the voice)
}
