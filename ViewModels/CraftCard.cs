using System;
using System.Collections.Generic;

namespace MicForge.ViewModels;

/// <summary>
/// One "voice character" card in the Crafting tab. Each card is a macro: a set of deltas
/// (pitch, five EQ band gains, saturation drive) applied at its Intensity. Enabled cards
/// are summed onto the EQ + Voice Changer + Saturation stages for an instant, layerable
/// voice — no technical knobs required. Definitions come from craftcards.json.
/// </summary>
public sealed class CraftCard : ViewModelBase
{
    private static readonly string[] BandNames = { "Low shelf", "Low-mid", "Mid", "Presence", "Air" };
    private readonly Action _onChange;

    public CraftCard(Action onChange, CraftCardConfig cfg)
    {
        _onChange = onChange;
        Id = cfg.Id;
        Icon = cfg.Icon;
        Title = cfg.Title;
        Category = string.IsNullOrWhiteSpace(cfg.Category) ? "Tone" : cfg.Category;
        Blurb = cfg.Blurb;
        Explanation = string.IsNullOrWhiteSpace(cfg.Explanation) ? cfg.Blurb : cfg.Explanation;
        Pitch = cfg.Pitch;
        Eq = cfg.Eq;
        Drive = cfg.Drive;
        Exciter = cfg.Exciter;
    }

    public string Id { get; }
    public string Icon { get; }
    public string Title { get; }
    public string Category { get; }
    public string Blurb { get; }
    public string Explanation { get; }

    // Deltas at 100% intensity.
    public double Pitch { get; }      // semitones
    public double[] Eq { get; }       // 5 band gain deltas (low shelf, low-mid, mid, presence, air)
    public double Drive { get; }      // saturation drive dB
    public double Exciter { get; }    // harmonic exciter amount (percent)

    /// <summary>Human-readable list of exactly what this card changes at full intensity.</summary>
    public string TechnicalPeek
    {
        get
        {
            var parts = new List<string>();
            if (Math.Abs(Pitch) >= 0.01) parts.Add($"Pitch {Pitch:+0;-0} st");
            for (int i = 0; i < 5 && i < Eq.Length; i++)
                if (Math.Abs(Eq[i]) >= 0.01) parts.Add($"{BandNames[i]} {Eq[i]:+0.#;-0.#} dB");
            if (Drive >= 0.5) parts.Add($"Saturation drive +{Drive:0.#} dB");
            if (Exciter >= 0.5) parts.Add($"Exciter {Exciter:0}%");
            return parts.Count == 0 ? "No change." : string.Join("   ·   ", parts);
        }
    }

    private bool _enabled;
    public bool Enabled
    {
        get => _enabled;
        set { if (Set(ref _enabled, value)) _onChange?.Invoke(); }
    }

    private double _intensity = 100;
    public double Intensity
    {
        get => _intensity;
        set { if (Set(ref _intensity, value) && _enabled) _onChange?.Invoke(); }
    }

    /// <summary>0 when off, else Intensity as a 0..1 scale.</summary>
    public double Scale => _enabled ? Math.Clamp(_intensity / 100.0, 0, 1) : 0;

    /// <summary>Set enabled + intensity without triggering a recompute (used when restoring).</summary>
    public void SetSilently(bool enabled, double intensity)
    {
        _enabled = enabled;
        _intensity = intensity;
        OnPropertyChanged(nameof(Enabled));
        OnPropertyChanged(nameof(Intensity));
    }
}
