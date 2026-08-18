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
        ResetCraftingCommand = new RelayCommand(ResetCrafting);
        UndoCommand = new RelayCommand(Undo, () => _undo.Count > 0);
        RedoCommand = new RelayCommand(Redo, () => _redo.Count > 0);
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
            _showMuteOverlay = saved.ShowMuteOverlay;
            _pttEnabled = saved.PttEnabled;
            _pttHoldToTalk = saved.PttHoldToTalk;
            _pttVk = saved.PttVk;
        }
        SelectDefaults(saved);
        BuildStages();
        ApplyStageOrder(saved?.StageOrder);
        RestoreCrafting(saved);
        BuildHotkeys(saved);
        if (_pttEnabled) ApplyPttState();

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
    public RelayCommand ResetCraftingCommand { get; }
    public RelayCommand UndoCommand { get; }
    public RelayCommand RedoCommand { get; }
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
    private void SetPage(string p)
    {
        _page = p;
        OnPropertyChanged(nameof(IsProcessorPage));
        OnPropertyChanged(nameof(IsSettingsPage));
        OnPropertyChanged(nameof(IsShortcutsPage));
        OnPropertyChanged(nameof(IsCraftingPage));
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

    public string[] PresetNames => BuiltInPresets.Names;

    private string _selectedPresetName;
    public string SelectedPresetName
    {
        get => _selectedPresetName;
        set
        {
            if (!Set(ref _selectedPresetName, value) || string.IsNullOrEmpty(value)) return;
            BuiltInPresets.Apply(value, _engine.Chain);
            BuildStages();
        }
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

    public string VersionText => "MicForge · v1.0.1 · PolyForm Noncommercial 1.0.0";

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

        var voice = new StageViewModel("Voice Changer", "#9B6CE3", () => c.VoiceChanger.Enabled, v => c.VoiceChanger.Enabled = v)
        {
            Info = "Shifts your pitch up or down in real time — deep villain, chipmunk, or a subtle disguise. 0 semitones passes through untouched; ±12 is a full octave. (Also driven by the Crafting tab.)"
        };
        voice.Add("Pitch", -12, 12, 1, () => c.VoiceChanger.Semitones, v => c.VoiceChanger.Semitones = v, "0", " st",
            "How many semitones to shift. Negative = deeper, positive = higher.");
        voice.Add("Mix", 0, 100, 1, () => c.VoiceChanger.Mix, v => c.VoiceChanger.Mix = v, "0", " %",
            "Blend between your natural voice and the shifted voice.");
        Stages.Add(voice);

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
            c.InputGain, c.InputAgc, c.HighPass, c.Hum, c.DePlosive, c.Suppressor, c.Gate, c.DeClicker,
            c.Eq, c.Compressor, c.DeEsser, c.Saturation, c.VoiceChanger, c.Limiter, c.OutputGain, c.Loudness
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
            foreach (var s in current) if (!Stages.Contains(s)) Stages.Add(s);
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

    private void BuildCraftCards()
    {
        if (_craftingBuilt) return;
        _craftingBuilt = true;

        void Add(string id, string icon, string title, string blurb, double pitch, double[] eq, double drive)
            => CraftCards.Add(new CraftCard(ApplyCrafting, id, icon, title, blurb, pitch, eq, drive));

        //   id       icon  title         blurb                              pitch  EQ: low,lomid,mid,pres,air        drive
        Add("bass",   "🔊", "Bass Boost", "Fuller, deeper low end.",             0, new[]{  6.0, 0.0, 0.0, 0.0,  0.0 }, 0);
        Add("thin",   "🍃", "Bass Cut",   "Lighter, thinner — less rumble.",     0, new[]{ -6.0, 0.0, 0.0, 0.0,  0.0 }, 0);
        Add("warm",   "🔥", "Warm",       "Cozy, radio-warm tone.",              0, new[]{  2.0, 1.0, 0.0,-1.0,  0.0 }, 0);
        Add("bright", "✨", "Bright",     "Crisp and clear up top.",             0, new[]{  0.0, 0.0, 0.0, 3.0,  4.0 }, 0);
        Add("pres",   "🎯", "Presence",   "Voice pushed forward.",               0, new[]{  0.0, 0.0, 2.0, 3.0,  0.0 }, 0);
        Add("air",    "💨", "Air",        "Open, airy sheen.",                   0, new[]{  0.0, 0.0, 0.0, 0.0,  5.0 }, 0);
        Add("radio",  "📻", "Radio",      "Old-school broadcast band.",          0, new[]{ -6.0,-1.0, 3.0, 1.0, -6.0 }, 6);
        Add("phone",  "☎️", "Telephone",  "Tinny call-quality voice.",           0, new[]{-14.0, 0.0, 5.0, 2.0,-14.0 }, 0);
        Add("mega",   "📢", "Megaphone",  "Loud-hailer honk.",                   0, new[]{ -8.0, 0.0, 6.0, 2.0, -4.0 }, 5);
        Add("water",  "🌊", "Underwater", "Muffled and submerged.",              0, new[]{  2.0, 0.0,-4.0,-8.0,-10.0 }, 0);
        Add("deep",   "🧛", "Deep Voice", "Lower, bigger, villainous.",         -4, new[]{  3.0, 0.0, 0.0, 0.0,  0.0 }, 0);
        Add("chip",   "🐿️", "Chipmunk",   "High and squeaky.",                   5, new[]{  0.0, 0.0, 0.0, 0.0,  0.0 }, 0);
        Add("robot",  "🤖", "Robot",      "Gritty machine voice.",              -2, new[]{  0.0, 0.0, 2.0, 1.0,  0.0 }, 8);
        Add("whisp",  "👻", "Whisper",    "Soft, breathy, ghostly.",             0, new[]{ -3.0, 0.0, 0.0, 2.0,  4.0 }, 0);
        Add("pod",    "🎙️", "Podcast",    "Smooth, full, professional.",         0, new[]{  3.0, 1.0, 0.0, 1.0,  1.0 }, 0);
    }

    /// <summary>Sum the enabled cards onto the EQ + Voice Changer + Saturation stages, live.</summary>
    private void ApplyCrafting()
    {
        var c = _engine.Chain;
        double pitch = 0, drive = 0;
        var eq = new double[5];
        bool any = false;
        foreach (var card in CraftCards)
        {
            double s = card.Scale;
            if (s <= 0) continue;
            any = true;
            pitch += card.Pitch * s;
            drive += card.Drive * s;
            for (int i = 0; i < 5; i++) eq[i] += card.Eq[i] * s;
        }

        for (int i = 0; i < 5 && i < c.Eq.Bands.Count; i++)
            c.Eq.Bands[i].GainDb = Math.Clamp(eq[i], -18, 18);
        c.Eq.UpdateAll();
        if (any) c.Eq.Enabled = true;

        double semi = Math.Clamp(pitch, -12, 12);
        c.VoiceChanger.Semitones = semi;
        c.VoiceChanger.Enabled = Math.Abs(semi) >= 0.05;

        if (drive > 0.5)
        {
            c.Saturation.DriveDb = Math.Clamp(drive, 0, 24);
            c.Saturation.Mix = 60;
            c.Saturation.Enabled = true;
        }
        else if (any)
        {
            c.Saturation.Enabled = false;
        }

        RefreshParamDisplays();
        SaveSettings();
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

        double lufs = chain.Loudness.ShortTermLufs;
        LufsText = lufs <= -60 ? "—" : $"{lufs:0.0}";

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
        s.ShowMuteOverlay = ShowMuteOverlay;
        s.PttEnabled = PttEnabled;
        s.PttHoldToTalk = PttHoldToTalk;
        s.PttVk = PttVk;
        s.Hotkeys = Hotkeys.Select(h => new HotkeyBinding { Action = h.ActionId, Modifiers = h.Modifiers, Vk = h.Vk }).ToList();
        s.CraftCards = CraftCards.Select(x => new CraftCardState { Id = x.Id, Enabled = x.Enabled, Intensity = x.Intensity }).ToList();
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
        { Filter = "MicForge preset (*.json)|*.json", FileName = "preset.json" };
        if (dlg.ShowDialog() == true)
        {
            try { Snapshot().Save(dlg.FileName); } catch (Exception ex) { MessageBox.Show(ex.Message, "MicForge"); }
        }
    }

    private void LoadPresetDialog()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "MicForge preset (*.json)|*.json" };
        if (dlg.ShowDialog() != true) return;

        var s = Settings.Load(dlg.FileName);
        if (s == null) { MessageBox.Show("Could not read that preset.", "MicForge"); return; }

        s.ApplyTo(_engine.Chain);
        SelectDefaults(s);
        BuildStages();
        ApplyStageOrder(s.StageOrder);
        RestoreCrafting(s);
        _histLast = Snapshot().ToJson();
    }
}
