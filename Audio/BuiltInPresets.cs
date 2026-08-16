namespace MicForge.Audio;

/// <summary>Ready-made starting points, tuned for voice. Applied onto the live chain.</summary>
public static class BuiltInPresets
{
    public static readonly string[] Names =
    {
        "Default (flat)", "Broadcast Voice", "Podcast Warm", "Streaming", "Gaming Clarity"
    };

    private static void Band(ParametricEq eq, int i, double freq, double gain, double q)
    {
        var b = eq.Bands[i];
        b.Freq = freq; b.GainDb = gain; b.Q = q;
    }

    public static void Apply(string name, DspChain c)
    {
        var hp = c.HighPass; var g = c.Gate; var eq = c.Eq; var comp = c.Compressor;
        var de = c.DeEsser; var lim = c.Limiter;

        // Common baseline.
        c.InputGain.GainDb = 0;
        hp.Enabled = true;
        g.Enabled = true; g.AttackMs = 3;
        eq.Enabled = true;
        comp.Enabled = true; comp.KneeDb = 6;
        de.Enabled = true;
        lim.Enabled = true; lim.CeilingDb = -1; lim.ReleaseMs = 60;
        c.Suppressor.Enabled = false;

        switch (name)
        {
            case "Broadcast Voice":
                hp.Frequency = 90;
                g.ThresholdDb = -42; g.HoldMs = 120; g.ReleaseMs = 150; g.RangeDb = -70;
                Band(eq, 0, 120, 1.5, 0.707); Band(eq, 1, 350, -2, 1.2); Band(eq, 2, 1500, 0, 1.0);
                Band(eq, 3, 4000, 3, 1.0); Band(eq, 4, 10000, 3, 0.707);
                comp.ThresholdDb = -20; comp.Ratio = 3; comp.AttackMs = 8; comp.ReleaseMs = 120; comp.MakeupDb = 4;
                de.Frequency = 6500; de.ThresholdDb = -30; de.Ratio = 4;
                c.OutputGain.GainDb = 1;
                break;

            case "Podcast Warm":
                hp.Frequency = 75;
                g.ThresholdDb = -50; g.HoldMs = 180; g.ReleaseMs = 250; g.RangeDb = -60;
                Band(eq, 0, 100, 2, 0.707); Band(eq, 1, 250, -1.5, 1.0); Band(eq, 2, 1200, 0, 1.0);
                Band(eq, 3, 3500, 1.5, 1.0); Band(eq, 4, 9000, 1, 0.707);
                comp.ThresholdDb = -22; comp.Ratio = 2.5; comp.AttackMs = 15; comp.ReleaseMs = 180; comp.KneeDb = 8; comp.MakeupDb = 3;
                de.Frequency = 6000; de.ThresholdDb = -28; de.Ratio = 3;
                c.OutputGain.GainDb = 0;
                break;

            case "Streaming":
                c.Suppressor.Enabled = c.Suppressor.Available;
                hp.Frequency = 90;
                g.ThresholdDb = -40; g.HoldMs = 100; g.ReleaseMs = 120; g.RangeDb = -80;
                Band(eq, 0, 120, 0, 0.707); Band(eq, 1, 300, -2, 1.2); Band(eq, 2, 2000, 2, 1.0);
                Band(eq, 3, 5000, 3, 1.0); Band(eq, 4, 11000, 3, 0.707);
                comp.ThresholdDb = -18; comp.Ratio = 3.5; comp.AttackMs = 6; comp.ReleaseMs = 100; comp.MakeupDb = 5;
                de.Frequency = 7000; de.ThresholdDb = -32; de.Ratio = 5;
                c.OutputGain.GainDb = 1;
                break;

            case "Gaming Clarity":
                c.Suppressor.Enabled = c.Suppressor.Available;
                hp.Frequency = 100;
                g.ThresholdDb = -38; g.AttackMs = 2; g.HoldMs = 80; g.ReleaseMs = 100; g.RangeDb = -85;
                Band(eq, 0, 120, -1, 0.707); Band(eq, 1, 400, -2, 1.2); Band(eq, 2, 2500, 2.5, 1.0);
                Band(eq, 3, 6000, 3.5, 1.0); Band(eq, 4, 12000, 2, 0.707);
                comp.ThresholdDb = -16; comp.Ratio = 4; comp.AttackMs = 5; comp.ReleaseMs = 90; comp.KneeDb = 4; comp.MakeupDb = 5;
                de.Frequency = 7000; de.ThresholdDb = -30; de.Ratio = 5;
                c.OutputGain.GainDb = 1.5;
                break;

            default: // Default (flat)
                hp.Frequency = 80;
                g.ThresholdDb = -45; g.HoldMs = 150; g.ReleaseMs = 200; g.RangeDb = -70;
                Band(eq, 0, 120, 0, 0.707); Band(eq, 1, 300, 0, 1.0); Band(eq, 2, 1800, 0, 1.0);
                Band(eq, 3, 5000, 0, 1.0); Band(eq, 4, 10000, 0, 0.707);
                comp.ThresholdDb = -18; comp.Ratio = 3; comp.AttackMs = 10; comp.ReleaseMs = 120; comp.MakeupDb = 0;
                de.Frequency = 6500; de.ThresholdDb = -28; de.Ratio = 4;
                c.OutputGain.GainDb = 0;
                break;
        }

        eq.UpdateAll();
    }
}
