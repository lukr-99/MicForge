using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using MicForge.Audio;
using NAudio.CoreAudioApi;

namespace MicForge.ViewModels;

/// <summary>The Crafting macro cards and the preview-voice player.</summary>
public sealed partial class MainViewModel
{
    // ---- crafting (macro voice cards) ----
    private EqStageViewModel _eqStage;
    public EqStageViewModel EqStage => _eqStage;
    public ObservableCollection<CraftCard> CraftCards { get; } = new();
    private bool _craftingBuilt;

    // Category filter for the Crafting cards.
    public string[] CraftCategories { get; } =
        new[] { "All" }.Concat(CraftCatalog.Categories).ToArray();

    private System.ComponentModel.ICollectionView _craftView;
    public System.ComponentModel.ICollectionView CraftView
    {
        get
        {
            if (_craftView == null)
            {
                _craftView = System.Windows.Data.CollectionViewSource.GetDefaultView(CraftCards);
                _craftView.Filter = o => _craftCategory == "All" || (o as CraftCard)?.Category == _craftCategory;
            }
            return _craftView;
        }
    }

    private string _craftCategory = "All";
    public string SelectedCraftCategory
    {
        get => _craftCategory;
        set { if (Set(ref _craftCategory, value)) CraftView.Refresh(); }
    }

    private void BuildCraftCards()
    {
        if (_craftingBuilt) return;
        _craftingBuilt = true;
        foreach (var cfg in CraftCatalog.Load())
            CraftCards.Add(new CraftCard(ApplyCrafting, cfg));
    }

    /// <summary>Re-read craftcards.json, preserving which cards were on and at what strength.</summary>
    [RelayCommand]
    private void ReloadCraft()
    {
        var states = new Dictionary<string, (bool on, double amt)>();
        foreach (var c in CraftCards) states[c.Id] = (c.Enabled, c.Intensity);

        CraftCards.Clear();
        _craftingBuilt = false;
        BuildCraftCards();

        foreach (var c in CraftCards)
            if (states.TryGetValue(c.Id, out var s)) c.SetSilently(s.on, s.amt);
        ApplyCrafting();
    }

    [RelayCommand]
    private void OpenCraftFile()
    {
        try
        {
            CraftCatalog.EnsureFile();
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(CraftCatalog.FilePath) { UseShellExecute = true });
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "MicForge"); }
    }

    /// <summary>
    /// Sum the enabled cards onto the High-Pass + EQ + Voice Changer + Saturation + Exciter
    /// stages, live. EQ bands sit at fixed frequencies; LowCut drives the high-pass and
    /// HighCut turns the top EQ band into a low-pass, giving real band-limiting (telephone,
    /// megaphone, underwater…).
    /// </summary>
    private void ApplyCrafting()
    {
        var c = _engine.Chain;
        double pitch = 0, drive = 0, exciter = 0;
        var eq = new double[5];
        double lowCut = 80;      // Hz, the highest requested cut wins
        double highCut = 0;      // Hz, the lowest requested cut wins; 0 = off
        bool any = false;

        foreach (var card in CraftCards)
        {
            double s = card.Scale;
            if (s <= 0) continue;
            any = true;
            pitch += card.Pitch * s;
            drive += card.Drive * s;
            exciter += card.Exciter * s;
            for (int i = 0; i < 5; i++) eq[i] += card.Eq[i] * s;

            double lc = 80 + (Math.Max(card.LowCut, 80) - 80) * s;
            if (lc > lowCut) lowCut = lc;

            if (card.HighCut > 20)
            {
                double hc = 20000 - (20000 - card.HighCut) * s;
                if (highCut <= 0 || hc < highCut) highCut = hc;
            }
        }

        // Fixed-frequency crafting EQ; the top band becomes a low-pass when a card muffles.
        SetBand(c.Eq, 0, Biquad.FilterType.LowShelf, 120, 0.707, eq[0]);
        SetBand(c.Eq, 1, Biquad.FilterType.Peaking, 500, 1.0, eq[1]);
        SetBand(c.Eq, 2, Biquad.FilterType.Peaking, 1700, 1.4, eq[2]);
        SetBand(c.Eq, 3, Biquad.FilterType.Peaking, 4000, 1.0, eq[3]);
        if (highCut > 20 && highCut < 19000)
            SetBand(c.Eq, 4, Biquad.FilterType.LowPass, highCut, 0.707, 0);
        else
            SetBand(c.Eq, 4, Biquad.FilterType.HighShelf, 10000, 0.707, eq[4]);
        c.Eq.UpdateAll();
        if (any) c.Eq.Enabled = true;

        // Low cut via the high-pass stage.
        c.HighPass.Frequency = Math.Clamp(lowCut, 20, 700);
        if (any) c.HighPass.Enabled = true;

        double semi = Math.Clamp(pitch, -12, 12);
        c.VoiceChanger.Semitones = semi;
        c.VoiceChanger.Enabled = Math.Abs(semi) >= 0.05;

        c.Saturation.Enabled = drive > 0.5;
        if (c.Saturation.Enabled) { c.Saturation.DriveDb = Math.Clamp(drive, 0, 24); c.Saturation.Mix = 60; }

        c.Exciter.Enabled = exciter > 0.5;
        if (c.Exciter.Enabled) c.Exciter.Amount = Math.Clamp(exciter, 0, 100);

        RefreshParamDisplays();
        SaveSettings();
    }

    private static void SetBand(ParametricEq eq, int i, Biquad.FilterType type, double freq, double q, double gain)
    {
        if (i >= eq.Bands.Count) return;
        var b = eq.Bands[i];
        b.Type = type; b.Freq = freq; b.Q = q; b.GainDb = Math.Clamp(gain, -18, 18); b.Enabled = true;
    }

    [RelayCommand]
    private void ResetCrafting()
    {
        foreach (var card in CraftCards) card.SetSilently(false, card.Intensity);
        ApplyCrafting();
    }

    private void RestoreCrafting(Settings saved)
    {
        if (SetCraftStates(saved)) ApplyCrafting();
    }

    /// <summary>Set card states from settings without touching the chain. Returns true if any are on.</summary>
    private bool SetCraftStates(Settings saved)
    {
        BuildCraftCards();
        bool any = false;
        foreach (var card in CraftCards)
        {
            var st = saved?.CraftCards?.FirstOrDefault(x => x.Id == card.Id);
            card.SetSilently(st?.Enabled ?? false, st?.Intensity ?? card.Intensity);
            if (st?.Enabled == true) any = true;
        }
        return any;
    }

    /// <summary>Re-read every param slider from the model (after crafting changes values under it).</summary>
    private void RefreshParamDisplays()
    {
        foreach (var s in Stages)
            foreach (var p in s.Params) p.NotifyChanged();
    }

    // ---- crafting preview (play a standard voice sample through the chain) ----
    private PreviewPlayer _preview;
    public ObservableCollection<PreviewSample> PreviewSamples { get; } = new();

    private PreviewSample _selectedPreviewSample;
    public PreviewSample SelectedPreviewSample
    {
        get => _selectedPreviewSample;
        set { if (Set(ref _selectedPreviewSample, value) && _previewActive) RestartPreview(); }
    }

    private void LoadPreviewSamples()
    {
        PreviewSamples.Clear();
        foreach (var s in VoiceSample.List()) PreviewSamples.Add(s);
        _selectedPreviewSample = PreviewSamples.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedPreviewSample));
    }

    private bool _previewActive;
    public bool PreviewSampleActive
    {
        get => _previewActive;
        set
        {
            if (!Set(ref _previewActive, value)) return;
            if (value) StartSamplePreview(); else _preview?.Stop();
        }
    }

    private void StartSamplePreview()
    {
        if (_engine.Running || _engine.Reconnecting) _engine.Stop();
        _preview ??= new PreviewPlayer(_engine.Chain);
        try
        {
            var samples = VoiceSample.LoadFor(_selectedPreviewSample, AudioEngine.SampleRate);
            _preview.Start(SelectedMonitorDevice?.Id, samples);
        }
        catch (Exception ex)
        {
            Log.Error("Sample preview failed to start", ex);
            _previewActive = false;
            OnPropertyChanged(nameof(PreviewSampleActive));
            MessageBox.Show("Could not start the preview on your output device.", "MicForge");
        }
    }

    private void RestartPreview()
    {
        _preview?.Stop();
        StartSamplePreview();
    }

    [RelayCommand]
    private void AddPreviewSample()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        { Filter = "WAV audio (*.wav)|*.wav", Title = "Add a preview voice" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var dest = Path.Combine(VoiceSample.UserFolder, Path.GetFileName(dlg.FileName));
            File.Copy(dlg.FileName, dest, overwrite: true);
            LoadPreviewSamples();
            var added = PreviewSamples.FirstOrDefault(s =>
                !s.IsSynth && string.Equals(s.Path, dest, StringComparison.OrdinalIgnoreCase));
            if (added != null) SelectedPreviewSample = added;
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "MicForge"); }
    }
}
