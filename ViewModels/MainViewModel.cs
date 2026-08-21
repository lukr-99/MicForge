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

public sealed partial class MainViewModel : ViewModelBase
{
    private readonly AudioEngine _engine;
    private readonly string _settingsPath = Settings.DefaultPath();
    private readonly DispatcherTimer _meterTimer;

    public event Action ExitRequested;
    public event Action ShowRequested;

    public MainViewModel(AudioEngine engine)
    {
        _engine = engine;

        LoadDevices();
        var saved = Settings.Load(_settingsPath);
        if (saved != null)
        {
            saved.ApplyTo(_engine.Chain);
            _autoStartProcessing = saved.AutoStartProcessing;
            _minimizeToTray = saved.StartMinimized;
            _visualMode = saved.VisualMode;
            _monitorEnabled = saved.MonitorEnabled;
            _globalHotkeys = saved.GlobalHotkeysEnabled;
            _followDefaultInput = saved.FollowDefaultInput;
            _showMuteOverlay = saved.ShowMuteOverlay;
            _lastPreset = saved.LastPreset;
            _pttEnabled = saved.PttEnabled;
            _pttHoldToTalk = saved.PttHoldToTalk;
            _pttVk = saved.PttVk;
        }
        SelectDefaults(saved);
        BuildStages();
        ApplyStageOrder(saved?.StageOrder);
        RestoreCrafting(saved);
        LoadPreviewSamples();
        LoadPresets(_lastPreset);
        BuildHotkeys(saved);
        if (_pttEnabled) ApplyPttState();
        if (_followDefaultInput) ApplyFollowDefault();

        _startWithWindows = StartupManager.IsEnabled;

        _meterTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        _meterTimer.Tick += (_, _) => UpdateMeters();
        _meterTimer.Start();

        _histLast = Snapshot().ToJson();
        _histTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _histTimer.Tick += (_, _) => CaptureHistory();
        _histTimer.Start();
    }

    // Commands are generated from the [RelayCommand] methods below (CommunityToolkit.Mvvm).
    public ObservableCollection<HotkeyViewModel> Hotkeys { get; } = new();

    public event Action HotkeysChanged;
    public event Action PttHookChanged;

    // ---- commands (the [RelayCommand] source generator emits the <Name>Command properties) ----
    [RelayCommand] private void Refresh() { LoadDevices(); SelectDefaults(null); }
    [RelayCommand] private void Show() => ShowRequested?.Invoke();
    [RelayCommand] private void Exit() => ExitRequested?.Invoke();
    [RelayCommand] private void ShowProcessor() => SetPage("processor");
    [RelayCommand] private void ShowSettings() => SetPage("settings");
    [RelayCommand] private void ShowShortcuts() => SetPage("shortcuts");
    [RelayCommand] private void ShowCrafting() => SetPage("crafting");
    [RelayCommand] private void ShowMeters() => SetPage("meters");
    [RelayCommand] private void ToggleBypass() => Bypassed = !Bypassed;
    [RelayCommand] private void ToggleMute() => Muted = !Muted;
    [RelayCommand] private void ResetLoudness() => _engine.Chain.Loudness.ResetMeasurement();
    [RelayCommand] private void ClearHotkey(HotkeyViewModel hk) { if (hk == null) return; hk.Clear(); OnHotkeysChanged(); }

    // ---- navigation ----
    private string _page = "processor";
    public bool IsProcessorPage => _page == "processor";
    public bool IsSettingsPage => _page == "settings";
    public bool IsShortcutsPage => _page == "shortcuts";
    public bool IsCraftingPage => _page == "crafting";
    public bool IsMetersPage => _page == "meters";
    private void SetPage(string p)
    {
        _page = p;
        OnPropertyChanged(nameof(IsProcessorPage));
        OnPropertyChanged(nameof(IsSettingsPage));
        OnPropertyChanged(nameof(IsShortcutsPage));
        OnPropertyChanged(nameof(IsCraftingPage));
        OnPropertyChanged(nameof(IsMetersPage));
    }

    // ---- devices ----
    public List<DeviceInfo> Inputs { get; private set; } = new();
    public List<DeviceInfo> Outputs { get; private set; } = new();

    private DeviceInfo _selectedInput;
    public DeviceInfo SelectedInput { get => _selectedInput; set => Set(ref _selectedInput, value); }

    private DeviceInfo _selectedOutput;
    public DeviceInfo SelectedOutput { get => _selectedOutput; set => Set(ref _selectedOutput, value); }

    // ---- run state (polled from the engine each meter tick) ----
    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (Set(ref _isRunning, value))
            {
                OnPropertyChanged(nameof(StartStopText));
                OnPropertyChanged(nameof(NotRunning));
            }
        }
    }

    public bool NotRunning => !_isRunning;
    public string StartStopText => _isRunning ? "Stop" : "Start";

    private bool _isProcessing;
    public bool IsProcessing { get => _isProcessing; private set => Set(ref _isProcessing, value); }

    private bool _isReconnecting;
    public bool IsReconnecting { get => _isReconnecting; private set => Set(ref _isReconnecting, value); }

    private string _statusText = "Stopped";
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }

    // ---- options ----
    private bool _startWithWindows;
    public bool StartWithWindows
    {
        get => _startWithWindows;
        set { if (Set(ref _startWithWindows, value)) StartupManager.SetEnabled(value); }
    }

    private bool _autoStartProcessing;
    public bool AutoStartProcessing { get => _autoStartProcessing; set => Set(ref _autoStartProcessing, value); }

    private bool _minimizeToTray = true;
    public bool MinimizeToTray { get => _minimizeToTray; set => Set(ref _minimizeToTray, value); }

    private bool _visualMode;
    public bool VisualMode { get => _visualMode; set => Set(ref _visualMode, value); }

    private bool _bypassed;
    public bool Bypassed
    {
        get => _bypassed;
        set { if (Set(ref _bypassed, value)) _engine.Chain.Bypass = value; }
    }

    private bool _muted;
    private bool _suppressMuteFlash;
    public bool Muted
    {
        get => _muted;
        set
        {
            if (!Set(ref _muted, value)) return;
            _engine.Chain.Mute = value;
            if (!_suppressMuteFlash) MuteFlashRequested?.Invoke(value);
        }
    }
    public event Action<bool> MuteFlashRequested;

    private bool _showMuteOverlay = true;
    public bool ShowMuteOverlay { get => _showMuteOverlay; set => Set(ref _showMuteOverlay, value); }

    private bool _globalHotkeys;
    public bool GlobalHotkeysEnabled
    {
        get => _globalHotkeys;
        set { if (Set(ref _globalHotkeys, value)) HotkeysChanged?.Invoke(); }
    }

    private bool _monitorEnabled;
    public bool MonitorEnabled
    {
        get => _monitorEnabled;
        set { if (Set(ref _monitorEnabled, value)) ApplyMonitor(); }
    }

    private DeviceInfo _selectedMonitorDevice;
    public DeviceInfo SelectedMonitorDevice
    {
        get => _selectedMonitorDevice;
        set { if (Set(ref _selectedMonitorDevice, value)) ApplyMonitor(); }
    }

    private void ApplyMonitor() => _engine.ConfigureMonitor(_selectedMonitorDevice, _monitorEnabled);

    // ---- follow the Windows default mic ----
    private DefaultDeviceWatcher _defaultWatcher;

    private bool _followDefaultInput;
    public bool FollowDefaultInput
    {
        get => _followDefaultInput;
        set { if (Set(ref _followDefaultInput, value)) { ApplyFollowDefault(); SaveSettings(); } }
    }

    private void ApplyFollowDefault()
    {
        if (_followDefaultInput)
        {
            if (_defaultWatcher == null)
            {
                _defaultWatcher = new DefaultDeviceWatcher();
                _defaultWatcher.DefaultCaptureChanged += OnDefaultCaptureChanged;
            }
            _defaultWatcher.Start();
        }
        else
        {
            _defaultWatcher?.Dispose();
            _defaultWatcher = null;
        }
    }

    private void OnDefaultCaptureChanged(string newId)
    {
        var disp = System.Windows.Application.Current?.Dispatcher;
        if (disp == null) return;
        disp.BeginInvoke(() =>
        {
            if (!_followDefaultInput) return;
            LoadDevices();
            var dev = Inputs.FirstOrDefault(d => d.Id == newId);
            if (dev == null || (SelectedInput != null && SelectedInput.Id == newId)) return;

            Log.Info($"Following new default mic: {dev.Name}");
            bool wasRunning = _engine.Running;
            SelectedInput = dev;
            if (wasRunning)
            {
                _engine.Stop();
                try { _engine.Start(SelectedInput, SelectedOutput); }
                catch (Exception ex) { Log.Error("Follow-default restart failed", ex); }
            }
            SaveSettings();
        });
    }

    [RelayCommand]
    private void OpenLogs()
    {
        try
        {
            var path = Log.FilePath;
            var psi = System.IO.File.Exists(path)
                ? new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
                : new System.Diagnostics.ProcessStartInfo(Log.Folder) { UseShellExecute = true };
            System.Diagnostics.Process.Start(psi);
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "MicForge"); }
    }

    public string VersionText => "MicForge · v1.6.3 · PolyForm Noncommercial 1.0.0";

    // ---- stages ----
    public ObservableCollection<StageViewModel> Stages { get; } = new();

    /// <summary>(Re)builds the stage cards from the current chain via <see cref="StageBuilder"/>.</summary>
    private void BuildStages()
    {
        var builder = new StageBuilder(_engine.Chain,
            new StageActions(Calibrate, BrowseRnnoise, LearnNoise));

        Stages.Clear();
        foreach (var stage in builder.Build())
            Stages.Add(stage);

        _eqStage = builder.EqStage;
    }

    // ---- stage reordering (drag & drop changes the processing order) ----
    public void SetDragging(StageViewModel stage, bool on)
    {
        if (stage != null) stage.IsDragging = on;
    }

    /// <summary>Live preview during a drag: reorder cards + renumber, without touching the chain yet.</summary>
    public void MoveStageLive(StageViewModel dragged, StageViewModel target)
    {
        if (dragged == null || target == null || dragged == target) return;
        int from = Stages.IndexOf(dragged), to = Stages.IndexOf(target);
        if (from < 0 || to < 0) return;
        Stages.Move(from, to);
        for (int i = 0; i < Stages.Count; i++) Stages[i].Order = i + 1;
    }

    /// <summary>Apply the previewed order to the chain and persist it (on drop).</summary>
    public void CommitOrder() => RenumberAndApplyChain(save: true);

    /// <summary>Revert to the given order (drag cancelled).</summary>
    public void RestoreOrder(List<StageViewModel> order)
    {
        Stages.Clear();
        foreach (var s in order) Stages.Add(s);
        for (int i = 0; i < Stages.Count; i++) Stages[i].Order = i + 1;
    }

    private void RenumberAndApplyChain(bool save)
    {
        for (int i = 0; i < Stages.Count; i++) Stages[i].Order = i + 1;
        _engine.Chain.SetOrder(Stages.Select(s => s.Processor).Where(p => p != null).ToList());
        if (save) SaveSettings();
    }

    private void ApplyStageOrder(List<string> order)
    {
        if (order != null && order.Count > 0)
        {
            var current = Stages.ToList();
            Stages.Clear();
            foreach (var id in order)
            {
                var s = current.FirstOrDefault(x => x.ProcessorId == id && !Stages.Contains(x));
                if (s != null) Stages.Add(s);
            }
            // Stages new since the order was saved: slot them in near their default position
            // (not at the very end), so e.g. click/echo removal lands early where it belongs.
            foreach (var s in current)
                if (!Stages.Contains(s))
                    Stages.Insert(Math.Min(current.IndexOf(s), Stages.Count), s);
        }
        RenumberAndApplyChain(save: false);
    }

    [RelayCommand]
    private void ResetOrder()
    {
        BuildStages();
        RenumberAndApplyChain(save: true);
    }

    // ---- device helpers ----
    private void LoadDevices()
    {
        Inputs = AudioEngine.InputDevices();
        Outputs = AudioEngine.OutputDevices();
        OnPropertyChanged(nameof(Inputs));
        OnPropertyChanged(nameof(Outputs));
    }

    private void SelectDefaults(Settings saved)
    {
        SelectedInput = Inputs.FirstOrDefault(d => d.Id == saved?.InputDeviceId)
                        ?? Inputs.FirstOrDefault(d => d.Id == AudioEngine.DefaultInputId())
                        ?? Inputs.FirstOrDefault();

        SelectedOutput = Outputs.FirstOrDefault(d => d.Id == saved?.OutputDeviceId)
                         ?? Outputs.FirstOrDefault(d =>
                             d.Name.IndexOf("CABLE Input", StringComparison.OrdinalIgnoreCase) >= 0)
                         ?? Outputs.FirstOrDefault();

        SelectedMonitorDevice = Outputs.FirstOrDefault(d => d.Id == saved?.MonitorDeviceId)
                                ?? Outputs.FirstOrDefault(d => d.Id == AudioEngine.DefaultOutputId())
                                ?? Outputs.FirstOrDefault();
    }

    // ---- run ----
    [RelayCommand]
    private void StartStop()
    {
        if (_engine.Running || _engine.Reconnecting)
        {
            _engine.Stop();
            return;
        }

        if (SelectedInput == null || SelectedOutput == null)
        {
            MessageBox.Show("Select an input and an output device first.", "MicForge");
            return;
        }

        PreviewSampleActive = false;   // sample preview and live mic share the one chain

        try
        {
            _engine.Start(SelectedInput, SelectedOutput);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Could not start audio");
        }
    }

    // ---- presets / persistence ----
    private Settings Snapshot()
    {
        var s = Settings.CaptureFrom(_engine.Chain);
        s.InputDeviceId = SelectedInput?.Id;
        s.OutputDeviceId = SelectedOutput?.Id;
        s.AutoStartProcessing = AutoStartProcessing;
        s.StartMinimized = MinimizeToTray;
        s.VisualMode = VisualMode;
        s.StageOrder = Stages.Select(x => x.ProcessorId).ToList();
        s.MonitorEnabled = MonitorEnabled;
        s.MonitorDeviceId = SelectedMonitorDevice?.Id;
        s.GlobalHotkeysEnabled = GlobalHotkeysEnabled;
        s.FollowDefaultInput = FollowDefaultInput;
        s.ShowMuteOverlay = ShowMuteOverlay;
        s.PttEnabled = PttEnabled;
        s.PttHoldToTalk = PttHoldToTalk;
        s.PttVk = PttVk;
        s.Hotkeys = Hotkeys.Select(h => new HotkeyBinding { Action = h.ActionId, Modifiers = h.Modifiers, Vk = h.Vk }).ToList();
        s.CraftCards = CraftCards.Select(x => new CraftCardState { Id = x.Id, Enabled = x.Enabled, Intensity = x.Intensity }).ToList();
        s.LastPreset = _lastPreset;
        return s;
    }

    public void SaveSettings()
    {
        try { Snapshot().Save(_settingsPath); } catch { }
    }

    public void Shutdown()
    {
        _meterTimer?.Stop();
        _histTimer?.Stop();
        _defaultWatcher?.Dispose();
        _preview?.Dispose();
        SaveSettings();
        _engine.Dispose();
    }

    private void BrowseRnnoise()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select rnnoise.dll (64-bit)",
            Filter = "rnnoise library (*.dll)|*.dll"
        };
        if (dlg.ShowDialog() != true) return;

        if (_engine.Chain.Suppressor.TryLoad(dlg.FileName))
        {
            _engine.Chain.Suppressor.Enabled = true;
            SaveSettings();
            BuildStages();   // refresh the Noise Suppression card
        }
        else
        {
            MessageBox.Show(
                "That file isn't a compatible RNNoise library.\n\nIt must be a 64-bit rnnoise.dll exporting " +
                "rnnoise_create, rnnoise_destroy and rnnoise_process_frame.",
                "MicForge");
        }
    }

}
