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

/// <summary>The preset dropdown: load/apply the built-ins and folder presets, and save/load preset files.</summary>
public sealed partial class MainViewModel
{
    public ObservableCollection<PresetItem> Presets { get; } = new();
    private bool _loadingPresets;
    private string _lastPreset;

    private PresetItem _selectedPreset;
    public PresetItem SelectedPreset
    {
        get => _selectedPreset;
        set
        {
            if (!Set(ref _selectedPreset, value) || value == null || _loadingPresets) return;
            ApplyPreset(value);
        }
    }

    /// <summary>Rebuild the preset list (built-ins + the presets folder) and select by name
    /// without re-applying — the chain state is already restored from settings.</summary>
    private void LoadPresets(string selectName)
    {
        _loadingPresets = true;
        Presets.Clear();
        foreach (var p in PresetLibrary.List()) Presets.Add(p);
        _selectedPreset = Presets.FirstOrDefault(p => p.Name == selectName);
        OnPropertyChanged(nameof(SelectedPreset));
        _loadingPresets = false;
    }

    private void ApplyPreset(PresetItem item)
    {
        if (item.IsBuiltIn)
        {
            BuiltInPresets.Apply(item.Name, _engine.Chain);
            // A built-in preset owns the sound now — clear active crafting cards so the tab
            // reflects that (without re-applying, which would wipe the preset's EQ).
            foreach (var card in CraftCards) card.SetSilently(false, card.Intensity);
            BuildStages();
            RenumberAndApplyChain(save: false);
        }
        else
        {
            var s = Settings.Load(item.Path);
            if (s == null) { MessageBox.Show("Could not read that preset.", "MicForge"); return; }
            s.ApplyTo(_engine.Chain);
            BuildStages();
            ApplyStageOrder(s.StageOrder);
            RestoreCrafting(s);
        }
        _lastPreset = item.Name;
        SaveSettings();
        _histLast = Snapshot().ToJson();
    }

    [RelayCommand]
    private void OpenPresetsFolder()
    {
        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(PresetLibrary.Folder) { UseShellExecute = true });
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "MicForge"); }
    }

    [RelayCommand]
    private void SavePreset()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "MicForge preset (*.json)|*.json",
            FileName = "My preset.json",
            InitialDirectory = PresetLibrary.Folder   // default here so it shows up in the dropdown
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            Snapshot().Save(dlg.FileName);
            var name = Path.GetFileNameWithoutExtension(dlg.FileName);
            LoadPresets(name);
            _selectedPreset = Presets.FirstOrDefault(p => p.Name == name);
            OnPropertyChanged(nameof(SelectedPreset));
            _lastPreset = name;
            SaveSettings();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "MicForge"); }
    }

    [RelayCommand]
    private void LoadPreset()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        { Filter = "MicForge preset (*.json)|*.json", InitialDirectory = PresetLibrary.Folder };
        if (dlg.ShowDialog() != true) return;

        var s = Settings.Load(dlg.FileName);
        if (s == null) { MessageBox.Show("Could not read that preset.", "MicForge"); return; }

        s.ApplyTo(_engine.Chain);
        SelectDefaults(s);
        BuildStages();
        ApplyStageOrder(s.StageOrder);
        RestoreCrafting(s);

        // Copy it into the presets folder so it appears in the dropdown next time.
        var name = Path.GetFileNameWithoutExtension(dlg.FileName);
        try
        {
            if (!string.Equals(Path.GetDirectoryName(dlg.FileName), PresetLibrary.Folder, StringComparison.OrdinalIgnoreCase))
                File.Copy(dlg.FileName, Path.Combine(PresetLibrary.Folder, Path.GetFileName(dlg.FileName)), overwrite: true);
        }
        catch { }

        LoadPresets(name);
        _selectedPreset = Presets.FirstOrDefault(p => p.Name == name);
        OnPropertyChanged(nameof(SelectedPreset));
        _lastPreset = name;
        SaveSettings();
        _histLast = Snapshot().ToJson();
    }
}
