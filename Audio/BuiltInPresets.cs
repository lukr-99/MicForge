namespace MicForge.Audio;

/// <summary>Ready-made starting points, tuned for voice. Applied onto the live chain.</summary>
public static class BuiltInPresets
{
    public static readonly string[] Names =
    {
        "Default (flat)", "Broadcast Voice", "Podcast Warm", "Streaming",
        "Gaming Clarity", "Crisp HD", "Clean Comms"
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
        var mb = c.Multiband; var exc = c.Exciter; var exp = c.Expander;
        var der = c.DeReverb; var cn = c.ComfortNoise;

        // Common baseline (deterministic — every preset starts from the same clean slate).
        c.InputGain.GainDb = 0;
        hp.Enabled = true;
        g.Enabled = true; g.AttackMs = 3;
        eq.Enabled = true;
        comp.Enabled = true; comp.KneeDb = 6;
        de.Enabled = true;
        lim.Enabled = true; lim.CeilingDb = -1; lim.ReleaseMs = 60;
        c.Suppressor.Enabled = false;

        // The newer character/utility stages default off; presets switch on what they need.
        exp.Enabled = false; exp.ThresholdDb = -45; exp.Ratio = 2.5; exp.ReleaseMs = 150; exp.RangeDb = -24;
        der.Enabled = false; der.Amount = 50; der.DecayMs = 150;
        mb.Enabled = false; mb.CrossLow = 250; mb.CrossHigh = 3000; mb.Ratio = 3; mb.MakeupDb = 0;
        mb.ThreshLowDb = -24; mb.ThreshMidDb = -20; mb.ThreshHighDb = -22;
        exc.Enabled = false; exc.Frequency = 3500; exc.Amount = 25;
        cn.Enabled = false; cn.LevelDb = -60; cn.ToneHz = 2000;
        c.Saturation.Enabled = false;
        c.VoiceChanger.Enabled = false; c.VoiceChanger.Semitones = 0;

        // Restore the standard EQ layout (Crafting can flip band types, e.g. band 4 -> low-pass).
        eq.Bands[0].Type = Biquad.FilterType.LowShelf;
        eq.Bands[1].Type = Biquad.FilterType.Peaking;
        eq.Bands[2].Type = Biquad.FilterType.Peaking;
        eq.Bands[3].Type = Biquad.FilterType.Peaking;
        eq.Bands[4].Type = Biquad.FilterType.HighShelf;
        foreach (var b in eq.Bands) b.Enabled = true;

        switch (name)
        {
            case "Broadcast Voice":
                hp.Frequency = 90;
                g.ThresholdDb = -42; g.HoldMs = 120; g.ReleaseMs = 150; g.RangeDb = -70;
                Band(eq, 0, 120, 1.5, 0.707); Band(eq, 1, 350, -2, 1.2); Band(eq, 2, 1500, 0.5, 1.0);
                Band(eq, 3, 4000, 2.5, 1.0); Band(eq, 4, 10000, 2, 0.707);
                comp.ThresholdDb = -20; comp.Ratio = 3; comp.AttackMs = 8; comp.ReleaseMs = 120; comp.MakeupDb = 4;
                de.Frequency = 6500; de.ThresholdDb = -30; de.Ratio = 4;
                // Even, controlled tone with a touch of air.
                mb.Enabled = true; mb.CrossLow = 220; mb.CrossHigh = 3500; mb.Ratio = 2.5; mb.MakeupDb = 2;
                mb.ThreshLowDb = -24; mb.ThreshMidDb = -22; mb.ThreshHighDb = -24;
                exc.Enabled = true; exc.Frequency = 4000; exc.Amount = 15;
                c.OutputGain.GainDb = 1;
                break;

            case "Podcast Warm":
                hp.Frequency = 75;
                g.ThresholdDb = -50; g.HoldMs = 180; g.ReleaseMs = 250; g.RangeDb = -60;
                Band(eq, 0, 100, 2, 0.707); Band(eq, 1, 250, -1.5, 1.0); Band(eq, 2, 1200, 0, 1.0);
                Band(eq, 3, 3500, 1.5, 1.0); Band(eq, 4, 9000, 1, 0.707);
                comp.ThresholdDb = -22; comp.Ratio = 2.5; comp.AttackMs = 15; comp.ReleaseMs = 180; comp.KneeDb = 8; comp.MakeupDb = 3;
                de.Frequency = 6000; de.ThresholdDb = -28; de.Ratio = 3;
                // Tighten the room a little and add a whisper of sheen.
                der.Enabled = true; der.Amount = 30; der.DecayMs = 160;
                exc.Enabled = true; exc.Frequency = 5000; exc.Amount = 12;
                c.OutputGain.GainDb = 0;
                break;

            case "Streaming":
                c.Suppressor.Enabled = c.Suppressor.Available;
                hp.Frequency = 90;
                g.ThresholdDb = -40; g.HoldMs = 100; g.ReleaseMs = 120; g.RangeDb = -80;
                Band(eq, 0, 120, 0, 0.707); Band(eq, 1, 300, -2, 1.2); Band(eq, 2, 2000, 2, 1.0);
                Band(eq, 3, 5000, 2.5, 1.0); Band(eq, 4, 11000, 2, 0.707);
                comp.ThresholdDb = -18; comp.Ratio = 3.5; comp.AttackMs = 6; comp.ReleaseMs = 100; comp.MakeupDb = 5;
                de.Frequency = 7000; de.ThresholdDb = -32; de.Ratio = 5;
                // Consistent band control, sparkle, and a comfort bed between phrases.
                mb.Enabled = true; mb.CrossLow = 250; mb.CrossHigh = 3200; mb.Ratio = 3; mb.MakeupDb = 2;
                mb.ThreshLowDb = -22; mb.ThreshMidDb = -20; mb.ThreshHighDb = -22;
                exc.Enabled = true; exc.Frequency = 4500; exc.Amount = 18;
                cn.Enabled = true; cn.LevelDb = -62;
                c.OutputGain.GainDb = 1;
                break;

            case "Gaming Clarity":
                // RNNoise made voices sound hollow/"in a tube" — rely on the fast gate instead.
                c.Suppressor.Enabled = false;
                hp.Frequency = 105;
                g.ThresholdDb = -38; g.AttackMs = 2; g.HoldMs = 80; g.ReleaseMs = 100; g.RangeDb = -85;
                Band(eq, 0, 150, -3, 0.707); Band(eq, 1, 450, -1.5, 1.0); Band(eq, 2, 1800, 1, 0.8);
                Band(eq, 3, 4500, 2.5, 0.9); Band(eq, 4, 10000, 1.5, 0.707);
                comp.ThresholdDb = -16; comp.Ratio = 3.5; comp.AttackMs = 5; comp.ReleaseMs = 90; comp.KneeDb = 4; comp.MakeupDb = 4;
                de.Frequency = 7000; de.ThresholdDb = -30; de.Ratio = 4;
                // Tighten a live gaming room and keep comms present without a dead-silent gate.
                exp.Enabled = true; exp.ThresholdDb = -42; exp.Ratio = 3; exp.ReleaseMs = 120; exp.RangeDb = -20;
                der.Enabled = true; der.Amount = 40; der.DecayMs = 140;
                exc.Enabled = true; exc.Frequency = 4000; exc.Amount = 22;
                cn.Enabled = true; cn.LevelDb = -60;
                c.OutputGain.GainDb = 1.5;
                break;

            case "Crisp HD":
                hp.Frequency = 90;
                g.ThresholdDb = -44; g.HoldMs = 120; g.ReleaseMs = 150; g.RangeDb = -75;
                Band(eq, 0, 110, 0, 0.707); Band(eq, 1, 300, -1, 1.0); Band(eq, 2, 2000, 1, 1.0);
                Band(eq, 3, 5000, 2.5, 1.0); Band(eq, 4, 11000, 2.5, 0.707);
                comp.ThresholdDb = -18; comp.Ratio = 3; comp.AttackMs = 8; comp.ReleaseMs = 110; comp.MakeupDb = 3;
                de.Frequency = 7000; de.ThresholdDb = -30; de.Ratio = 4;
                // Clean multiband control with exciter for a modern, hi-fi voice.
                mb.Enabled = true; mb.CrossLow = 240; mb.CrossHigh = 3500; mb.Ratio = 2.5; mb.MakeupDb = 1;
                exc.Enabled = true; exc.Frequency = 4000; exc.Amount = 28;
                c.OutputGain.GainDb = 1;
                break;

            case "Clean Comms":
                c.Suppressor.Enabled = c.Suppressor.Available;
                hp.Frequency = 110;
                g.ThresholdDb = -44; g.HoldMs = 100; g.ReleaseMs = 130; g.RangeDb = -75;
                Band(eq, 0, 140, -2, 0.707); Band(eq, 1, 400, 0, 1.0); Band(eq, 2, 1800, 2, 1.0);
                Band(eq, 3, 4500, 2, 1.0); Band(eq, 4, 10000, 0, 0.707);
                comp.ThresholdDb = -16; comp.Ratio = 4; comp.AttackMs = 6; comp.ReleaseMs = 100; comp.MakeupDb = 4;
                de.Frequency = 6500; de.ThresholdDb = -30; de.Ratio = 4;
                // Voice-first: gentle expansion, room tightening, and a comfort bed for calls.
                exp.Enabled = true; exp.ThresholdDb = -44; exp.Ratio = 2.5; exp.ReleaseMs = 140; exp.RangeDb = -22;
                der.Enabled = true; der.Amount = 50; der.DecayMs = 150;
                cn.Enabled = true; cn.LevelDb = -58; cn.ToneHz = 1800;
                c.OutputGain.GainDb = 1;
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
