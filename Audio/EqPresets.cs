namespace MicForge.Audio;

/// <summary>
/// Quick equalizer-curve presets, like most audio apps. Each one sets only the 5 EQ bands
/// (resetting them to the standard low-shelf / 3 peaks / high-shelf layout first) — it does
/// not touch the rest of the chain.
/// </summary>
public static class EqPresets
{
    public static readonly string[] Names =
    {
        "Flat", "Bass Boost", "Bass Cut", "Warm", "Bright", "Treble Boost",
        "Vocal", "Presence", "De-Mud", "Loudness", "Air", "Podcast"
    };

    public static void Apply(string name, ParametricEq eq)
    {
        // Standard, flat 5-band layout (also undoes any Crafting band-type changes).
        SetBand(eq, 0, Biquad.FilterType.LowShelf, 120, 0, 0.707);
        SetBand(eq, 1, Biquad.FilterType.Peaking, 300, 0, 1.0);
        SetBand(eq, 2, Biquad.FilterType.Peaking, 1800, 0, 1.0);
        SetBand(eq, 3, Biquad.FilterType.Peaking, 5000, 0, 1.0);
        SetBand(eq, 4, Biquad.FilterType.HighShelf, 10000, 0, 0.707);

        switch (name)
        {
            case "Bass Boost":   Gains(eq, 6, 2, 0, 0, 0); break;
            case "Bass Cut":     Gains(eq, -6, -1, 0, 0, 0); break;
            case "Warm":         Gains(eq, 3, 1, 0, -2, -1); break;
            case "Bright":       Gains(eq, 0, 0, 0, 3, 4); break;
            case "Treble Boost": Gains(eq, 0, 0, 0, 2, 6); break;
            case "Vocal":        Gains(eq, -1, -1, 2, 3, 0); break;
            case "Presence":     Gains(eq, 0, 0, 3, 4, 0); break;
            case "De-Mud":       eq.Bands[1].Freq = 350; Gains(eq, 0, -4, 0, 0, 0); break;
            case "Loudness":     Gains(eq, 5, 0, -2, 0, 5); break;
            case "Air":          Gains(eq, 0, 0, 0, 0, 5); break;
            case "Podcast":      Gains(eq, 3, 1, 0, 1, 1); break;
            // "Flat" leaves the bands at 0.
        }
        eq.UpdateAll();
    }

    private static void SetBand(ParametricEq eq, int i, Biquad.FilterType type, double freq, double gain, double q)
    {
        if (i >= eq.Bands.Count) return;
        var b = eq.Bands[i];
        b.Type = type; b.Freq = freq; b.GainDb = gain; b.Q = q; b.Enabled = true;
    }

    private static void Gains(ParametricEq eq, double b0, double b1, double b2, double b3, double b4)
    {
        eq.Bands[0].GainDb = b0; eq.Bands[1].GainDb = b1; eq.Bands[2].GainDb = b2;
        eq.Bands[3].GainDb = b3; eq.Bands[4].GainDb = b4;
    }
}
