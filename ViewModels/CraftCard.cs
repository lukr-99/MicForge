using System;

namespace MicForge.ViewModels;

/// <summary>
/// One "voice character" card in the Crafting tab. Each card is a macro: a set of deltas
/// (pitch, five EQ band gains, saturation drive) applied at its Intensity. Enabled cards
/// are summed onto the EQ + Voice Changer + Saturation stages for an instant, layerable
/// voice — no technical knobs required.
/// </summary>
public sealed class CraftCard : ViewModelBase
{
    private readonly Action _onChange;

    public CraftCard(Action onChange, string id, string icon, string title, string blurb,
        double pitch, double[] eq, double drive)
    {
        _onChange = onChange;
        Id = id;
        Icon = icon;
        Title = title;
        Blurb = blurb;
        Pitch = pitch;
        Eq = eq;
        Drive = drive;
    }

    public string Id { get; }
    public string Icon { get; }
    public string Title { get; }
    public string Blurb { get; }

    // Deltas at 100% intensity.
    public double Pitch { get; }      // semitones
    public double[] Eq { get; }       // 5 band gain deltas (low shelf, low-mid, mid, presence, air)
    public double Drive { get; }      // saturation drive dB

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
