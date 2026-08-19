using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using MicForge.Audio;
using NAudio.CoreAudioApi;

namespace MicForge.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly AudioEngine _engine = new();
    private readonly string _settingsPath = Settings.DefaultPath();
    private readonly DispatcherTimer _meterTimer;

    public event Action ExitRequested;
    public event Action ShowRequested;

    public MainViewModel()
    {
        StartStopCommand = new RelayCommand(ToggleRun);
        RefreshCommand = new RelayCommand(() => { LoadDevices(); SelectDefaults(null); });
        SavePresetCommand = new RelayCommand(SavePresetDialog);
        LoadPresetCommand = new RelayCommand(LoadPresetDialog);
        ShowCommand = new RelayCommand(() => ShowRequested?.Invoke());
        ExitCommand = new RelayCommand(() => ExitRequested?.Invoke());
        ShowProcessorCommand = new RelayCommand(() => SetPage("processor"));
        ShowSettingsCommand = new RelayCommand(() => SetPage("settings"));
        ShowShortcutsCommand = new RelayCommand(() => SetPage("shortcuts"));
        ShowCraftingCommand = new RelayCommand(() => SetPage("crafting"));
        ShowMetersCommand = new RelayCommand(() => SetPage("meters"));
        ResetCraftingCommand = new RelayCommand(ResetCrafting);
        ReloadCraftCommand = new RelayCommand(ReloadCraftCards);
        OpenCraftFileCommand = new RelayCommand(OpenCraftFile);
        AddPreviewSampleCommand = new RelayCommand(AddPreviewSample);
        OpenPresetsFolderCommand = new RelayCommand(OpenPresetsFolder);
        ResetLoudnessCommand = new RelayCommand(() => _engine.Chain.Loudness.ResetMeasurement());
        UndoCommand = new RelayCommand(Undo, () => _undo.Count > 0);
        RedoCommand = new RelayCommand(Redo, () => _redo.Count > 0);
        OpenLogsCommand = new RelayCommand(OpenLogs);
        ToggleBypassCommand = new RelayCommand(() => Bypassed = !Bypassed);
        ToggleMuteCommand = new RelayCommand(() => Muted = !Muted);
        SetHotkeyCommand = new RelayCommand(p => BeginCapture(p as HotkeyVm));
        ClearHotkeyCommand = new RelayCommand(p => { if (p is HotkeyVm h) { h.Clear(); OnHotkeysChanged(); } });
        SetPttKeyCommand = new RelayCommand(BeginPttCapture);
        ResetOrderCommand = new RelayCommand(ResetStageOrder);

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

    // ---- commands ----
    public RelayCommand StartStopCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand SavePresetCommand { get; }
    public RelayCommand LoadPresetCommand { get; }
    public RelayCommand ShowCommand { get; }
    public RelayCommand ExitCommand { get; }
    public RelayCommand ShowProcessorCommand { get; }
    public RelayCommand ShowSettingsCommand { get; }
    public RelayCommand ToggleBypassCommand { get; }
    public RelayCommand ToggleMuteCommand { get; }
    public RelayCommand ShowShortcutsCommand { get; }
    public RelayCommand ShowCraftingCommand { get; }
    public RelayCommand ShowMetersCommand { get; }
    public RelayCommand ResetCraftingCommand { get; }
    public RelayCommand ReloadCraftCommand { get; }
    public RelayCommand OpenCraftFileCommand { get; }
    public RelayCommand AddPreviewSampleCommand { get; }
    public RelayCommand OpenPresetsFolderCommand { get; }
    public RelayCommand ResetLoudnessCommand { get; }
    public RelayCommand UndoCommand { get; }
    public RelayCommand RedoCommand { get; }
    public RelayCommand OpenLogsCommand { get; }
    public RelayCommand SetHotkeyCommand { get; }
    public RelayCommand ClearHotkeyCommand { get; }
    public RelayCommand SetPttKeyCommand { get; }
    public RelayCommand ResetOrderCommand { get; }
    public ObservableCollection<HotkeyVm> Hotkeys { get; } = new();

    public event Action HotkeysChanged;
    public event Action PttHookChanged;

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

    // ---- hotkeys ----
    private void BuildHotkeys(Settings saved)
    {
        Hotkeys.Clear();
        Hotkeys.Add(new HotkeyVm("mute", "Mute", () => Muted = !Muted,
            GlobalHotkeys.ModControl | GlobalHotkeys.ModAlt, 0x4D));       // Ctrl+Alt+M
        Hotkeys.Add(new HotkeyVm("bypass", "Bypass", () => Bypassed = !Bypassed,
            GlobalHotkeys.ModControl | GlobalHotkeys.ModAlt, 0x42));       // Ctrl+Alt+B
        Hotkeys.Add(new HotkeyVm("startstop", "Start / Stop", () => StartStopCommand.Execute(null), 0, 0));

        if (saved?.Hotkeys != null)
            foreach (var b in saved.Hotkeys)
                Hotkeys.FirstOrDefault(h => h.ActionId == b.Action)?.Assign(b.Modifiers, b.Vk);
    }

    private void BeginCapture(HotkeyVm hk)
    {
        if (hk == null) return;
        foreach (var h in Hotkeys) h.Capturing = false;
        hk.Capturing = true;
    }

    public bool IsCapturingHotkey => Hotkeys.Any(h => h.Capturing);

    public void CancelCapture()
    {
        foreach (var h in Hotkeys) h.Capturing = false;
    }

    /// <summary>Called by the window when a key combo is captured for the pending hotkey.</summary>
    public void AssignCapturedKey(uint modifiers, uint vk)
    {
        var hk = Hotkeys.FirstOrDefault(h => h.Capturing);
        if (hk == null) return;
        foreach (var h in Hotkeys) if (h != hk && h.Modifiers == modifiers && h.Vk == vk) h.Clear();
        hk.Assign(modifiers, vk);
        OnHotkeysChanged();
    }

    private void OnHotkeysChanged()
    {
        HotkeysChanged?.Invoke();
        SaveSettings();
    }

    // ---- push-to-talk / push-to-mute ----
    private bool _pttEnabled;
    public bool PttEnabled
    {
        get => _pttEnabled;
        set
        {
            if (!Set(ref _pttEnabled, value)) return;
            _pttHeld = false;
            if (value) ApplyPttState();
            else { _suppressMuteFlash = true; Muted = false; _suppressMuteFlash = false; }
            SaveSettings();
            PttHookChanged?.Invoke();
        }
    }

    private bool _pttHoldToTalk = true;   // true: muted until held (talk); false: live until held (mute)
    public bool PttHoldToTalk
    {
        get => _pttHoldToTalk;
        set { if (Set(ref _pttHoldToTalk, value)) { ApplyPttState(); SaveSettings(); } }
    }

    private uint _pttVk;
    public uint PttVk
    {
        get => _pttVk;
        private set { _pttVk = value; OnPropertyChanged(nameof(PttKeyDisplay)); }
    }
    public string PttKeyDisplay => _pttVk == 0 ? "Not set" : HotkeyVm.Format(0, _pttVk);

    private bool _pttHeld;

    private bool _capturingPtt;
    public bool CapturingPtt
    {
        get => _capturingPtt;
        private set { if (Set(ref _capturingPtt, value)) OnPropertyChanged(nameof(SetPttText)); }
    }
    public string SetPttText => CapturingPtt ? "Press a key…" : "Set key";

    private void BeginPttCapture()
    {
        foreach (var h in Hotkeys) h.Capturing = false;
        CapturingPtt = true;
    }

    public void CancelPttCapture() => CapturingPtt = false;

    public void AssignPttKey(uint vk)
    {
        PttVk = vk;
        CapturingPtt = false;
        if (PttEnabled) ApplyPttState();
        SaveSettings();
    }

    /// <summary>Called by the keyboard hook when the push-to-talk key is pressed/released.</summary>
    public void PttKeyEvent(uint vk, bool down)
    {
        if (!PttEnabled || _pttVk == 0 || vk != _pttVk || down == _pttHeld) return;
        _pttHeld = down;
        ApplyPttState();
    }

    private void ApplyPttState()
    {
        if (!PttEnabled) return;
        bool muted = _pttHoldToTalk ? !_pttHeld : _pttHeld;
        _suppressMuteFlash = true;
        Muted = muted;
        _suppressMuteFlash = false;
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

    // ---- meters ----
    private double _inLevel, _outLevel;
    public double InLevel { get => _inLevel; private set => Set(ref _inLevel, value); }
    public double OutLevel { get => _outLevel; private set => Set(ref _outLevel, value); }

    private bool _inClip, _outClip;
    public bool InClip { get => _inClip; private set => Set(ref _inClip, value); }
    public bool OutClip { get => _outClip; private set => Set(ref _outClip, value); }
    private DateTime _inClipT = DateTime.MinValue, _outClipT = DateTime.MinValue;

    private double _compLevelDb = -100;
    public double CompLevelDb { get => _compLevelDb; private set => Set(ref _compLevelDb, value); }

    private string _grText = "0.0 dB";
    public string GrText { get => _grText; private set => Set(ref _grText, value); }

    private string _lufsText = "—";
    public string LufsText { get => _lufsText; private set => Set(ref _lufsText, value); }

    // Live stage visuals.
    private double _gateLevel, _gateThreshold;
    private bool _gateOpen;
    public double GateLevel { get => _gateLevel; private set => Set(ref _gateLevel, value); }
    public double GateThreshold { get => _gateThreshold; private set => Set(ref _gateThreshold, value); }
    public bool GateOpen { get => _gateOpen; private set => Set(ref _gateOpen, value); }

    private double _deLevel, _deThreshold;
    private bool _deActive;
    public double DeEsserLevel { get => _deLevel; private set => Set(ref _deLevel, value); }
    public double DeEsserThreshold { get => _deThreshold; private set => Set(ref _deThreshold, value); }
    public bool DeEsserActive { get => _deActive; private set => Set(ref _deActive, value); }

    private double _compGrDb;
    public double CompGrDb { get => _compGrDb; private set => Set(ref _compGrDb, value); }

    private double _gateGrDb;
    public double GateGrDb { get => _gateGrDb; private set => Set(ref _gateGrDb, value); }

    // ---- mic health ----
    private double _hiEnv = -100, _loEnv = 0;
    private string _micHealthText = "—";
    public string MicHealthText { get => _micHealthText; private set => Set(ref _micHealthText, value); }
    private string _micHealthColor = "#6C7A89";
    public string MicHealthColor { get => _micHealthColor; private set => Set(ref _micHealthColor, value); }
    private string _micHealthTip = "Start processing to check your mic.";
    public string MicHealthTip { get => _micHealthTip; private set => Set(ref _micHealthTip, value); }

    private double _limiterGrDb;
    public double LimiterGrDb { get => _limiterGrDb; private set => Set(ref _limiterGrDb, value); }

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

    // Gate noise-floor learning.
    private bool _learning;
    private DateTime _learnEnd;
    private double _learnMaxDb;

    private void LearnNoise()
    {
        if (!(_engine.Running || _engine.Reconnecting))
        {
            MessageBox.Show("Start processing first, then stay quiet for a moment while MicForge samples the room.",
                "Learn noise floor");
            return;
        }
        _learnMaxDb = -120;
        _learnEnd = DateTime.UtcNow.AddMilliseconds(1200);
        _learning = true;
    }

    // Two-step auto-calibration: measure the room, then measure normal speech.
    private enum CalPhase { None, Quiet, Talk }
    private CalPhase _cal = CalPhase.None;
    private DateTime _calEnd;
    private double _calNoiseMax, _calNoiseFloor, _calSpeechSum;
    private int _calSpeechCount;

    private void Calibrate()
    {
        if (!(_engine.Running || _engine.Reconnecting))
        {
            MessageBox.Show("Start processing first. Then the wizard asks you to stay quiet for a moment, then talk normally.",
                "Auto-calibrate");
            return;
        }
        _cal = CalPhase.Quiet;
        _calNoiseMax = -120;
        _calEnd = DateTime.UtcNow.AddMilliseconds(1500);
    }

    private void UpdateCalibration(double ip, DateTime now)
    {
        if (_cal == CalPhase.None) return;
        double db = ip <= 0.00001 ? -120 : 20 * Math.Log10(ip);

        if (_cal == CalPhase.Quiet)
        {
            if (db > _calNoiseMax) _calNoiseMax = db;
            StatusText = "Calibrating — stay quiet…";
            if (now >= _calEnd)
            {
                _calNoiseFloor = _calNoiseMax;
                _calSpeechSum = 0; _calSpeechCount = 0;
                _cal = CalPhase.Talk;
                _calEnd = now.AddMilliseconds(2800);
            }
        }
        else
        {
            if (db > -40) { _calSpeechSum += db; _calSpeechCount++; }
            StatusText = "Calibrating — talk normally…";
            if (now >= _calEnd)
            {
                _cal = CalPhase.None;
                ApplyCalibration();
            }
        }
    }

    private void ApplyCalibration()
    {
        var c = _engine.Chain;
        double speechAvg = _calSpeechCount > 0 ? _calSpeechSum / _calSpeechCount : -18;
        double curGain = c.InputGain.GainDb;
        double newGain = Math.Clamp(curGain + (-18 - speechAvg), -24, 24);
        double gainDelta = newGain - curGain;

        c.InputGain.GainDb = newGain;
        c.Gate.Enabled = true;
        c.Gate.UseVad = false;
        c.Gate.ThresholdDb = Math.Clamp(_calNoiseFloor + gainDelta + 6, -80, 0);
        c.Compressor.Enabled = true;
        c.Compressor.ThresholdDb = -16;
        c.Compressor.MakeupDb = 3;
        BuildStages();
    }

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

    private void OpenPresetsFolder()
    {
        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(PresetLibrary.Folder) { UseShellExecute = true });
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "MicForge"); }
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

    // ---- reliability ----
    private DefaultDeviceWatcher _defaultWatcher;

    private bool _followDefaultInput;
    public bool FollowDefaultInput
    {
        get => _followDefaultInput;
        set { if (Set(ref _followDefaultInput, value)) { ApplyFollowDefault(); SaveSettings(); } }
    }

    private string _dspLoadText = "—";
    public string DspLoadText { get => _dspLoadText; private set => Set(ref _dspLoadText, value); }

    private string _glitchText = "0";
    public string GlitchText { get => _glitchText; private set => Set(ref _glitchText, value); }

    // ---- loudness suite (Meters page) ----
    private string _momLufsText = "—", _intLufsText = "—", _lraText = "—", _truePeakText = "—";
    public string MomentaryLufsText { get => _momLufsText; private set => Set(ref _momLufsText, value); }
    public string IntegratedLufsText { get => _intLufsText; private set => Set(ref _intLufsText, value); }
    public string LraText { get => _lraText; private set => Set(ref _lraText, value); }
    public string TruePeakText { get => _truePeakText; private set => Set(ref _truePeakText, value); }

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

    public string VersionText => "MicForge · v1.6.1 · PolyForm Noncommercial 1.0.0";

    // ---- stages ----
    public ObservableCollection<StageViewModel> Stages { get; } = new();

    private void BuildStages()
    {
        var c = _engine.Chain;
        Stages.Clear();

        var input = new StageViewModel("Input", "#6C7A89", () => true, _ => { }, canToggle: false)
        {
            Info = "Sets the level of the raw microphone signal before any processing. Use it to bring a quiet mic up or tame a hot one so the input meter sits around the middle."
        };
        input.Add("Gain", -24, 24, 0.5, () => c.InputGain.GainDb, v => c.InputGain.GainDb = v, "0.0", " dB",
            "Boosts or cuts the incoming signal. Aim for speech peaks that don't slam the top of the meter.");
        input.ShowAction = true;
        input.ActionText = "Auto-calibrate";
        input.ActionCommand = new RelayCommand(Calibrate);
        Stages.Add(input);

        var agc = new StageViewModel("Auto Gain", "#7FB069", () => c.InputAgc.Enabled, v => c.InputAgc.Enabled = v)
        {
            Info = "Automatically rides the input gain to hold a steady speech level as your mic distance or volume change (gated on silence). Complements the output loudness leveler."
        };
        agc.Add("Target", -30, -6, 0.5, () => c.InputAgc.TargetDb, v => c.InputAgc.TargetDb = v, "0.0", " dB",
            "The average speech level the auto-gain aims for.");
        agc.Add("Max gain", 0, 18, 1, () => c.InputAgc.MaxGainDb, v => c.InputAgc.MaxGainDb = v, "0", " dB",
            "How much boost or cut the auto-gain may apply.");
        Stages.Add(agc);

        var hp = new StageViewModel("High-Pass", "#4FA3E3", () => c.HighPass.Enabled, v => c.HighPass.Enabled = v)
        {
            Info = "Rolls off low-frequency rumble — desk thumps, footsteps, AC hum, plosives — below the cutoff, cleaning the low end without thinning your voice."
        };
        hp.Add("Frequency", 20, 300, 5, () => c.HighPass.Frequency, v => c.HighPass.Frequency = v, "0", " Hz",
            "Everything below this frequency is removed. 80–100 Hz suits most voices.");
        Stages.Add(hp);

        var hum = new StageViewModel("Hum Remover", "#5C8A72", () => c.Hum.Enabled, v => c.Hum.Enabled = v)
        {
            Info = "Removes electrical mains hum and its harmonics with a stack of narrow notch filters. Pick 50 Hz (most of the world) or 60 Hz (North America) to match your grid."
        };
        hum.Add("Base freq", 40, 70, 10, () => c.Hum.Frequency, v => c.Hum.Frequency = v, "0", " Hz",
            "Your mains frequency: 50 Hz in most of the world, 60 Hz in North America.");
        hum.Add("Harmonics", 1, 10, 1, () => c.Hum.Harmonics, v => c.Hum.Harmonics = (int)v, "0", "",
            "How many multiples of the base to notch out. More catches buzzier hum but costs a little tone.");
        hum.Add("Sharpness", 5, 60, 1, () => c.Hum.Q, v => c.Hum.Q = v, "0", "",
            "Notch narrowness (Q). Higher removes hum with less effect on the surrounding voice.");
        Stages.Add(hum);

        var dep = new StageViewModel("De-Plosive", "#6C8AB0", () => c.DePlosive.Enabled, v => c.DePlosive.Enabled = v)
        {
            Info = "Softens 'P' and 'B' pops — the bursts of low-frequency energy that thump the mic — by ducking only the low band for the instant a plosive hits."
        };
        dep.Add("Frequency", 80, 300, 10, () => c.DePlosive.Frequency, v => c.DePlosive.Frequency = v, "0", " Hz",
            "The pop energy lives below this. 120–180 Hz suits most voices.");
        dep.Add("Threshold", -60, 0, 1, () => c.DePlosive.ThresholdDb, v => c.DePlosive.ThresholdDb = v, "0", " dB",
            "How loud the low band must get before it's treated as a pop.");
        dep.Add("Strength", 0, 100, 1, () => c.DePlosive.Strength, v => c.DePlosive.Strength = v, "0", " %",
            "How hard the pop is ducked when detected.");
        Stages.Add(dep);

        var ns = new StageViewModel("Noise Suppression", "#8E7CE6", () => c.Suppressor.Enabled, v => c.Suppressor.Enabled = v)
        {
            Info = "AI (RNNoise) removal of steady background noise like fans, hiss and hum, while leaving speech intact. This is the modern replacement for a plain driver's noise reduction."
        };
        if (!c.Suppressor.Available)
        {
            ns.ToggleEnabled = false;
            ns.Note = "Needs a 64-bit rnnoise.dll. Load one you downloaded or built, or drop it next to MicForge.exe.";
            ns.ShowAction = true;
            ns.ActionText = "Load rnnoise.dll…";
            ns.ActionCommand = new RelayCommand(BrowseRnnoise);
        }
        else
        {
            ns.Note = "RNNoise loaded — enable the toggle to remove steady background noise.";
        }
        Stages.Add(ns);

        var gate = new StageViewModel("Noise Gate", "#E0864F", () => c.Gate.Enabled, v => c.Gate.Enabled = v)
        {
            Info = "Mutes the mic when you're not talking, cutting keyboard clatter, mouse clicks and room tone in the gaps between words."
        };
        gate.Add("Threshold", -80, 0, 1, () => c.Gate.ThresholdDb, v => c.Gate.ThresholdDb = v, "0", " dB",
            "The level you must speak above to open the gate. Set it just above your background noise.");
        gate.Add("Attack", 0.1, 50, 0.1, () => c.Gate.AttackMs, v => c.Gate.AttackMs = v, "0.0", " ms",
            "How quickly the gate opens when you start speaking. Too fast can click; too slow clips word starts.");
        gate.Add("Hold", 0, 500, 5, () => c.Gate.HoldMs, v => c.Gate.HoldMs = v, "0", " ms",
            "How long the gate stays open after your level drops, so short pauses don't chop the sound.");
        gate.Add("Release", 20, 1000, 10, () => c.Gate.ReleaseMs, v => c.Gate.ReleaseMs = v, "0", " ms",
            "How gradually the gate closes after the hold time. Longer sounds more natural.");
        gate.Add("Range", -90, 0, 2, () => c.Gate.RangeDb, v => c.Gate.RangeDb = v, "0", " dB",
            "How much the signal is attenuated while the gate is closed. More negative = more complete silence.");
        gate.IsGate = true;
        gate.ShowAction = true;
        gate.ActionText = "Learn noise floor";
        gate.ActionCommand = new RelayCommand(LearnNoise);
        if (c.Suppressor.Available)
        {
            gate.SetExtraToggle("Smart — open on voice (RNNoise)", () => c.Gate.UseVad, v => c.Gate.UseVad = v);
            gate.Add("Voice sens", 0.1, 0.9, 0.05, () => c.Gate.VadThreshold, v => c.Gate.VadThreshold = v, "0.00", "",
                "How sure RNNoise must be that it's your voice before the smart gate opens.");
        }
        Stages.Add(gate);

        var expander = new StageViewModel("Expander", "#8AB06C", () => c.Expander.Enabled, v => c.Expander.Enabled = v)
        {
            Info = "A gentler alternative to the gate: instead of slamming shut, it turns quiet sounds (room tone, bleed) down gradually once they fall below the threshold."
        };
        expander.Add("Threshold", -80, 0, 1, () => c.Expander.ThresholdDb, v => c.Expander.ThresholdDb = v, "0", " dB",
            "Below this level the signal starts to be turned down.");
        expander.Add("Ratio", 1, 8, 0.5, () => c.Expander.Ratio, v => c.Expander.Ratio = v, "0.0", ":1",
            "How aggressively quiet parts are reduced. Higher is closer to a hard gate.");
        expander.Add("Release", 20, 800, 10, () => c.Expander.ReleaseMs, v => c.Expander.ReleaseMs = v, "0", " ms",
            "How quickly it recovers as your level comes back up.");
        expander.Add("Range", -60, 0, 2, () => c.Expander.RangeDb, v => c.Expander.RangeDb = v, "0", " dB",
            "The most it will attenuate.");
        Stages.Add(expander);

        var declick = new StageViewModel("De-Click", "#B08A6C", () => c.DeClicker.Enabled, v => c.DeClicker.Enabled = v)
        {
            Info = "Reduces mouth clicks and lip-smacks — the little tick sounds between words — by spotting fast high-frequency spikes and briefly ducking the high band."
        };
        declick.Add("Frequency", 1500, 6000, 100, () => c.DeClicker.Frequency, v => c.DeClicker.Frequency = v, "0", " Hz",
            "The band where clicks are detected — usually 2–4 kHz.");
        declick.Add("Sensitivity", 3, 18, 0.5, () => c.DeClicker.Sensitivity, v => c.DeClicker.Sensitivity = v, "0.0", " dB",
            "How much a spike must jump above the running average to count as a click. Lower catches more (and risks softening consonants).");
        declick.Add("Strength", 0, 100, 1, () => c.DeClicker.Strength, v => c.DeClicker.Strength = v, "0", " %",
            "How hard a detected click is ducked.");
        Stages.Add(declick);

        var keys = new StageViewModel("Keystroke Suppressor", "#9AA06C", () => c.Keystroke.Enabled, v => c.Keystroke.Enabled = v)
        {
            Info = "Knocks down mechanical keyboard clicks and clacks. It watches a high band for the sharp spike a key press makes and briefly ducks it, leaving your voice (which rises more gradually) intact. Tune the detector frequency to your keyboard and the sensitivity so it catches clacks without chewing consonants."
        };
        keys.Add("Detect freq", 1500, 6000, 100, () => c.Keystroke.DetectFreq, v => c.Keystroke.DetectFreq = v, "0", " Hz",
            "The band where the click is detected. Higher for sharper/clickier boards, lower for thockier ones.");
        keys.Add("Sensitivity", 3, 18, 0.5, () => c.Keystroke.Sensitivity, v => c.Keystroke.Sensitivity = v, "0.0", " dB",
            "How far a spike must jump above the running average to count as a click. Lower catches more clicks (and risks softening consonants).");
        keys.Add("Strength", 0, 100, 1, () => c.Keystroke.Strength, v => c.Keystroke.Strength = v, "0", " %",
            "How hard a detected click is ducked.");
        keys.Add("Release", 10, 200, 5, () => c.Keystroke.ReleaseMs, v => c.Keystroke.ReleaseMs = v, "0", " ms",
            "How quickly the level recovers after a click.");
        Stages.Add(keys);

        var derev = new StageViewModel("De-Reverb", "#6CA0B0", () => c.DeReverb.Enabled, v => c.DeReverb.Enabled = v)
        {
            Info = "Tightens up a boomy or echoey room by pulling down the reverb tail — the energy lingering after each word — while keeping the direct voice. A light reducer, not a full acoustic dereverb."
        };
        derev.Add("Amount", 0, 100, 1, () => c.DeReverb.Amount, v => c.DeReverb.Amount = v, "0", " %",
            "How hard the room tail is suppressed.");
        derev.Add("Decay", 40, 400, 10, () => c.DeReverb.DecayMs, v => c.DeReverb.DecayMs = v, "0", " ms",
            "The room's tail time — match it to how long echoes ring out.");
        Stages.Add(derev);

        var echo = new StageViewModel("Echo Remover", "#6C9AB0", () => c.EchoRemover.Enabled, v => c.EchoRemover.Enabled = v)
        {
            Info = "Cancels a distinct echo of your own voice — a slap-back off a wall, or your speakers bleeding back into the mic. An adaptive filter learns the delayed copy and subtracts it. Set Delay to roughly the echo time and raise Strength until the echo drops. (Not full acoustic echo cancellation — it has no reference to the far-end audio.)"
        };
        echo.Add("Delay", 20, 300, 5, () => c.EchoRemover.DelayMs, v => c.EchoRemover.DelayMs = v, "0", " ms",
            "Roughly how long after your voice the echo arrives. Sweep this until the echo locks on and drops.");
        echo.Add("Strength", 0, 100, 1, () => c.EchoRemover.Strength, v => c.EchoRemover.Strength = v, "0", " %",
            "How much of the estimated echo to remove.");
        Stages.Add(echo);

        var eq = new EqStageViewModel(c.Eq, c, AudioEngine.SampleRate, "#2EC4B6",
            () => c.Eq.Enabled, v => c.Eq.Enabled = v)
        {
            Info = "Shapes tone by boosting or cutting frequency bands: add warmth or presence, cut boxiness, tame harshness. In Graph view, drag the dots (mouse-wheel changes the width of a bell band)."
        };
        eq.Add("Low shelf", -18, 18, 0.5, () => c.Eq.Bands[0].GainDb, v => { c.Eq.Bands[0].GainDb = v; c.Eq.UpdateAll(); }, "0.0", " dB",
            "Boosts or cuts everything below its frequency — body and warmth.");
        eq.Add("P1 freq", 100, 1500, 10, () => c.Eq.Bands[1].Freq, v => { c.Eq.Bands[1].Freq = v; c.Eq.UpdateAll(); }, "0", " Hz",
            "Center frequency of the first bell band.");
        eq.Add("P1 gain", -18, 18, 0.5, () => c.Eq.Bands[1].GainDb, v => { c.Eq.Bands[1].GainDb = v; c.Eq.UpdateAll(); }, "0.0", " dB",
            "Boost or cut around the P1 frequency.");
        eq.Add("P2 freq", 500, 6000, 50, () => c.Eq.Bands[2].Freq, v => { c.Eq.Bands[2].Freq = v; c.Eq.UpdateAll(); }, "0", " Hz",
            "Center frequency of the second bell band.");
        eq.Add("P2 gain", -18, 18, 0.5, () => c.Eq.Bands[2].GainDb, v => { c.Eq.Bands[2].GainDb = v; c.Eq.UpdateAll(); }, "0.0", " dB",
            "Boost or cut around the P2 frequency.");
        eq.Add("P3 freq", 2000, 16000, 100, () => c.Eq.Bands[3].Freq, v => { c.Eq.Bands[3].Freq = v; c.Eq.UpdateAll(); }, "0", " Hz",
            "Center frequency of the third bell band.");
        eq.Add("P3 gain", -18, 18, 0.5, () => c.Eq.Bands[3].GainDb, v => { c.Eq.Bands[3].GainDb = v; c.Eq.UpdateAll(); }, "0.0", " dB",
            "Boost or cut around the P3 frequency.");
        eq.Add("High shelf", -18, 18, 0.5, () => c.Eq.Bands[4].GainDb, v => { c.Eq.Bands[4].GainDb = v; c.Eq.UpdateAll(); }, "0.0", " dB",
            "Boosts or cuts everything above its frequency — air and brightness.");
        _eqStage = eq;
        Stages.Add(eq);

        var comp = new CompressorStageViewModel(c.Compressor, "#E3B23C",
            () => c.Compressor.Enabled, v => c.Compressor.Enabled = v)
        {
            Info = "Evens out your volume — quiet words come up, loud peaks come down — for a steady, professional level. The Graph view shows the input→output transfer curve with a live dot."
        };
        comp.Add("Threshold", -60, 0, 1, () => c.Compressor.ThresholdDb, v => c.Compressor.ThresholdDb = v, "0", " dB",
            "Level above which compression starts working.");
        comp.Add("Ratio", 1, 20, 0.5, () => c.Compressor.Ratio, v => c.Compressor.Ratio = v, "0.0", ":1",
            "How hard it compresses above threshold. 3:1 means 3 dB in becomes 1 dB out.");
        comp.Add("Attack", 0.1, 100, 0.5, () => c.Compressor.AttackMs, v => c.Compressor.AttackMs = v, "0.0", " ms",
            "How fast it clamps down once you cross the threshold.");
        comp.Add("Release", 20, 1000, 10, () => c.Compressor.ReleaseMs, v => c.Compressor.ReleaseMs = v, "0", " ms",
            "How fast it lets go after your level drops back down.");
        comp.Add("Knee", 0, 24, 1, () => c.Compressor.KneeDb, v => c.Compressor.KneeDb = v, "0", " dB",
            "Softens the transition around the threshold for a smoother, less obvious sound.");
        comp.Add("Makeup", 0, 24, 0.5, () => c.Compressor.MakeupDb, v => c.Compressor.MakeupDb = v, "0.0", " dB",
            "Adds level back after compression to restore loudness.");
        Stages.Add(comp);

        var mb = new StageViewModel("Multiband", "#D39A5F", () => c.Multiband.Enabled, v => c.Multiband.Enabled = v)
        {
            Info = "Compresses the low, mid and high bands separately, so you can tame boomy lows, honky mids and harsh highs independently for a controlled, broadcast-style sound."
        };
        mb.Add("Low/mid split", 100, 800, 10, () => c.Multiband.CrossLow, v => c.Multiband.CrossLow = v, "0", " Hz",
            "Crossover frequency between the low and mid bands.");
        mb.Add("Mid/high split", 1500, 8000, 100, () => c.Multiband.CrossHigh, v => c.Multiband.CrossHigh = v, "0", " Hz",
            "Crossover frequency between the mid and high bands.");
        mb.Add("Low thr", -48, 0, 1, () => c.Multiband.ThreshLowDb, v => c.Multiband.ThreshLowDb = v, "0", " dB",
            "Compression threshold for the low band.");
        mb.Add("Mid thr", -48, 0, 1, () => c.Multiband.ThreshMidDb, v => c.Multiband.ThreshMidDb = v, "0", " dB",
            "Compression threshold for the mid band.");
        mb.Add("High thr", -48, 0, 1, () => c.Multiband.ThreshHighDb, v => c.Multiband.ThreshHighDb = v, "0", " dB",
            "Compression threshold for the high band.");
        mb.Add("Ratio", 1, 12, 0.5, () => c.Multiband.Ratio, v => c.Multiband.Ratio = v, "0.0", ":1",
            "Compression ratio applied to every band.");
        mb.Add("Makeup", 0, 18, 0.5, () => c.Multiband.MakeupDb, v => c.Multiband.MakeupDb = v, "0.0", " dB",
            "Level added back after compression.");
        Stages.Add(mb);

        var de = new StageViewModel("De-Esser", "#E36CA0", () => c.DeEsser.Enabled, v => c.DeEsser.Enabled = v)
        {
            Info = "Tames harsh 'sss' and 'sh' sibilance by compressing only the high sibilant band, so bright mics don't sound spitty."
        };
        de.Add("Frequency", 3000, 10000, 100, () => c.DeEsser.Frequency, v => c.DeEsser.Frequency = v, "0", " Hz",
            "Where the sibilance lives — usually 5–8 kHz.");
        de.Add("Threshold", -60, 0, 1, () => c.DeEsser.ThresholdDb, v => c.DeEsser.ThresholdDb = v, "0", " dB",
            "How loud sibilance must get before it's reduced.");
        de.Add("Ratio", 1, 10, 0.5, () => c.DeEsser.Ratio, v => c.DeEsser.Ratio = v, "0.0", ":1",
            "How hard the sibilant band is reduced.");
        de.IsDeEsser = true;
        Stages.Add(de);

        var sat = new StageViewModel("Saturation", "#D9A05B", () => c.Saturation.Enabled, v => c.Saturation.Enabled = v)
        {
            Info = "Adds gentle harmonic warmth and character by softly saturating the signal. Great for thin or clinical mics — keep it subtle."
        };
        sat.Add("Drive", 0, 24, 0.5, () => c.Saturation.DriveDb, v => c.Saturation.DriveDb = v, "0.0", " dB",
            "How hard the signal is pushed into the saturation curve. More drive = more harmonics.");
        sat.Add("Mix", 0, 100, 1, () => c.Saturation.Mix, v => c.Saturation.Mix = v, "0", " %",
            "Blend between the dry signal and the saturated signal.");
        Stages.Add(sat);

        var exc = new StageViewModel("Exciter", "#E3C85F", () => c.Exciter.Enabled, v => c.Exciter.Enabled = v)
        {
            Info = "Generates brand-new high harmonics to add air, sparkle and presence — a livelier top end than just boosting EQ. Keep it subtle."
        };
        exc.Add("Frequency", 1500, 8000, 100, () => c.Exciter.Frequency, v => c.Exciter.Frequency = v, "0", " Hz",
            "Harmonics are generated above this frequency.");
        exc.Add("Amount", 0, 100, 1, () => c.Exciter.Amount, v => c.Exciter.Amount = v, "0", " %",
            "How much of the generated sparkle to blend in.");
        Stages.Add(exc);

        var voice = new StageViewModel("Voice Changer", "#9B6CE3", () => c.VoiceChanger.Enabled, v => c.VoiceChanger.Enabled = v)
        {
            Info = "Shifts your pitch up or down in real time — deep villain, chipmunk, or a subtle disguise. 0 semitones passes through untouched; ±12 is a full octave. (Also driven by the Crafting tab.)"
        };
        voice.Add("Pitch", -12, 12, 1, () => c.VoiceChanger.Semitones, v => c.VoiceChanger.Semitones = v, "0", " st",
            "How many semitones to shift. Negative = deeper, positive = higher.");
        voice.Add("Mix", 0, 100, 1, () => c.VoiceChanger.Mix, v => c.VoiceChanger.Mix = v, "0", " %",
            "Blend between your natural voice and the shifted voice.");
        Stages.Add(voice);

        var cn = new StageViewModel("Comfort Noise", "#7C8AA0", () => c.ComfortNoise.Enabled, v => c.ComfortNoise.Enabled = v)
        {
            Info = "Fills gate/expander silence with a faint, soft noise bed so callers don't think you dropped off. It fades in only when you're not speaking, so it never muddies your voice."
        };
        cn.Add("Level", -80, -40, 1, () => c.ComfortNoise.LevelDb, v => c.ComfortNoise.LevelDb = v, "0", " dB",
            "How loud the noise bed sits. Keep it just audible.");
        cn.Add("Tone", 500, 6000, 100, () => c.ComfortNoise.ToneHz, v => c.ComfortNoise.ToneHz = v, "0", " Hz",
            "Softens the hiss — lower is duller and warmer.");
        Stages.Add(cn);

        var lim = new StageViewModel("Limiter", "#E5543B", () => c.Limiter.Enabled, v => c.Limiter.Enabled = v)
        {
            Info = "A safety net that stops the output from ever exceeding the ceiling, preventing digital clipping and distortion on sudden peaks."
        };
        lim.Add("Ceiling", -12, 0, 0.1, () => c.Limiter.CeilingDb, v => c.Limiter.CeilingDb = v, "0.0", " dB",
            "The maximum output level. Nothing gets past this.");
        lim.Add("Release", 10, 500, 5, () => c.Limiter.ReleaseMs, v => c.Limiter.ReleaseMs = v, "0", " ms",
            "How quickly the limiter recovers after catching a peak.");
        lim.Add("Lookahead", 0.5, 10, 0.5, () => c.Limiter.LookaheadMs, v => c.Limiter.LookaheadMs = v, "0.0", " ms",
            "How far ahead the limiter looks so it can ramp down before a peak (adds this much latency).");
        lim.IsLimiter = true;
        Stages.Add(lim);

        var output = new StageViewModel("Output", "#6C7A89", () => true, _ => { }, canToggle: false)
        {
            Info = "Final level sent to the virtual mic. Set it so your loudest speech peaks land around -6 to -3 dB on the output meter."
        };
        output.Add("Gain", -24, 24, 0.5, () => c.OutputGain.GainDb, v => c.OutputGain.GainDb = v, "0.0", " dB",
            "Overall output level in decibels.");
        Stages.Add(output);

        var loud = new StageViewModel("Loudness", "#5FA8D3", () => c.Loudness.AutoLevel, v => c.Loudness.AutoLevel = v)
        {
            Info = "Measures perceived loudness (LUFS) and, when enabled, slowly rides the gain to keep you at a consistent target — great for streaming and podcasts. The live reading is on the meter panel."
        };
        loud.Add("Target", -30, -10, 0.5, () => c.Loudness.TargetLufs, v => c.Loudness.TargetLufs = v, "0.0", " LUFS",
            "The loudness the auto-leveler aims for. Around -16 LUFS is a common streaming target.");
        loud.Add("Max gain", 0, 18, 1, () => c.Loudness.MaxGainDb, v => c.Loudness.MaxGainDb = v, "0", " dB",
            "How much the auto-leveler may boost or cut to reach the target.");
        Stages.Add(loud);

        // Map each card to its processor (built in the chain's default order).
        var procs = new IAudioProcessor[]
        {
            c.InputGain, c.InputAgc, c.HighPass, c.Hum, c.DePlosive, c.Suppressor, c.Gate, c.Expander, c.DeClicker,
            c.Keystroke, c.DeReverb, c.EchoRemover, c.Eq, c.Compressor, c.Multiband, c.DeEsser, c.Saturation,
            c.Exciter, c.VoiceChanger, c.ComfortNoise, c.Limiter, c.OutputGain, c.Loudness
        };
        for (int i = 0; i < Stages.Count && i < procs.Length; i++) Stages[i].Processor = procs[i];
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

    private void ResetStageOrder()
    {
        BuildStages();
        RenumberAndApplyChain(save: true);
    }

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
    private void ReloadCraftCards()
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

    // ---- undo / redo (coalesced snapshots of the whole chain) ----
    private readonly Stack<string> _undo = new();
    private readonly Stack<string> _redo = new();
    private string _histLast;
    private bool _applyingHistory;
    private DispatcherTimer _histTimer;

    /// <summary>Every tick, if the chain changed since the last checkpoint, push the old state.</summary>
    private void CaptureHistory()
    {
        if (_applyingHistory) return;
        string cur;
        try { cur = Snapshot().ToJson(); } catch { return; }
        if (_histLast == null) { _histLast = cur; return; }
        if (cur == _histLast) return;

        _undo.Push(_histLast);
        if (_undo.Count > 60)
        {
            var keep = _undo.ToArray();               // newest-first
            _undo.Clear();
            for (int i = 59; i >= 0; i--) _undo.Push(keep[i]);
        }
        _redo.Clear();
        _histLast = cur;
    }

    private void Undo()
    {
        if (_undo.Count == 0) return;
        _applyingHistory = true;
        try
        {
            _redo.Push(Snapshot().ToJson());
            var prev = _undo.Pop();
            ApplyHistory(prev);
            _histLast = prev;
        }
        finally { _applyingHistory = false; }
    }

    private void Redo()
    {
        if (_redo.Count == 0) return;
        _applyingHistory = true;
        try
        {
            _undo.Push(Snapshot().ToJson());
            var next = _redo.Pop();
            ApplyHistory(next);
            _histLast = next;
        }
        finally { _applyingHistory = false; }
    }

    private void ApplyHistory(string json)
    {
        var s = Settings.FromJson(json);
        if (s == null) return;
        s.ApplyTo(_engine.Chain);
        SetCraftStates(s);
        BuildStages();
        ApplyStageOrder(s.StageOrder);
        SaveSettings();
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
    private void ToggleRun()
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

    private void UpdateMeters()
    {
        bool running = _engine.Running;
        bool recon = _engine.Reconnecting;
        IsRunning = running || recon;
        IsProcessing = running && !recon;
        IsReconnecting = recon;
        StatusText = recon ? "Reconnecting…" : (running ? "Processing" : "Stopped");

        var chain = _engine.Chain;
        float ip = chain.InputPeak;
        float op = chain.OutputPeak;
        InLevel = ToMeter(ip);
        OutLevel = ToMeter(op);
        CompLevelDb = ip <= 0.00001f ? -100 : 20 * Math.Log10(ip);

        var now = DateTime.UtcNow;
        if (ip >= 0.999f) _inClipT = now;
        if (op >= 0.999f) _outClipT = now;
        InClip = (now - _inClipT).TotalMilliseconds < 1200;
        OutClip = (now - _outClipT).TotalMilliseconds < 1200;

        UpdateMicHealth(ip, running && !recon);

        if (_learning)
        {
            double db = ip <= 0.00001f ? -120 : 20 * Math.Log10(ip);
            if (db > _learnMaxDb) _learnMaxDb = db;
            if (now >= _learnEnd)
            {
                _learning = false;
                var gt = chain.Gate;
                gt.ThresholdDb = Math.Clamp(_learnMaxDb + 5, -80, 0);
                gt.Enabled = true;
                BuildStages();
            }
            else StatusText = "Sampling room…";
        }

        UpdateCalibration(ip, now);

        var comp = chain.Compressor;
        GrText = $"{comp.GainReductionDb:0.0} dB";
        CompGrDb = comp.GainReductionDb;

        var loud = chain.Loudness;
        double lufs = loud.ShortTermLufs;
        LufsText = lufs <= -60 ? "—" : $"{lufs:0.0}";
        MomentaryLufsText = loud.MomentaryLufs <= -60 ? "—" : $"{loud.MomentaryLufs:0.0}";
        IntegratedLufsText = loud.IntegratedLufs <= -60 ? "—" : $"{loud.IntegratedLufs:0.0}";
        LraText = $"{loud.LoudnessRange:0.0}";
        TruePeakText = loud.TruePeakDb <= -60 ? "—" : $"{loud.TruePeakDb:+0.0;-0.0;0.0}";

        DspLoadText = (running && !recon) ? $"{chain.DspLoad * 100:0} %" : "—";
        GlitchText = _engine.Glitches.ToString();

        var g = chain.Gate;
        GateThreshold = Norm(g.ThresholdDb, -80, 0);
        GateLevel = Norm(g.DetectorDb, -80, 0);
        GateOpen = g.Enabled && g.IsOpen;
        GateGrDb = g.Enabled ? -g.ReductionDb : 0;   // attenuation while closing, as a positive dB

        var d = chain.DeEsser;
        DeEsserThreshold = Norm(d.ThresholdDb, -60, 0);
        DeEsserLevel = Norm(d.DetectorDb, -60, 0);
        DeEsserActive = d.Enabled && d.ReductionDb < -0.3;

        LimiterGrDb = chain.Limiter.GainReductionDb;
    }

    private void UpdateMicHealth(float ip, bool processing)
    {
        if (!processing)
        {
            _hiEnv = -100; _loEnv = 0;
            SetHealth("—", "#6C7A89", "Start processing to check your mic.");
            return;
        }

        double db = ip <= 1e-5f ? -100 : 20 * Math.Log10(ip);
        _hiEnv = db > _hiEnv ? db : _hiEnv - 0.5;   // speech-peak proxy (decays ~15 dB/s)
        _loEnv = db < _loEnv ? db : _loEnv + 0.2;   // noise-floor proxy (rises slowly)

        if (InClip)                 SetHealth("Too hot", "#E5543B", "Peaks are clipping — lower the input gain or back off the mic.");
        else if (_hiEnv < -40)      SetHealth("Quiet", "#E3B23C", "Your voice is low — raise the input gain, or run Auto-calibrate.");
        else if (_loEnv > -45)      SetHealth("Noisy", "#E3B23C", "High background noise — try the gate, or enable noise suppression.");
        else                        SetHealth("Good", "#7FB069", "Levels look healthy.");
    }

    private void SetHealth(string text, string color, string tip)
    {
        MicHealthText = text; MicHealthColor = color; MicHealthTip = tip;
    }

    private static double ToMeter(float peak)
    {
        if (peak <= 0.00001f) return 0;
        double db = 20 * Math.Log10(peak);
        return Math.Clamp((db + 60) / 60.0, 0, 1);
    }

    private static double Norm(double db, double min, double max)
        => Math.Clamp((db - min) / (max - min), 0, 1);

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

    private void SavePresetDialog()
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

    private void LoadPresetDialog()
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
