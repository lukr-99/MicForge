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
    private readonly string _settingsPath = Path.Combine(AppContext.BaseDirectory, "micforge.json");
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

        LoadDevices();
        var saved = Settings.Load(_settingsPath);
        if (saved != null)
        {
            saved.ApplyTo(_engine.Chain);
            _autoStartProcessing = saved.AutoStartProcessing;
            _minimizeToTray = saved.StartMinimized;
            _visualMode = saved.VisualMode;
        }
        SelectDefaults(saved);
        BuildStages();

        _startWithWindows = StartupManager.IsEnabled;

        _meterTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        _meterTimer.Tick += (_, _) => UpdateMeters();
        _meterTimer.Start();
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

    // ---- navigation ----
    private string _page = "processor";
    public bool IsProcessorPage => _page == "processor";
    public bool IsSettingsPage => _page == "settings";
    private void SetPage(string p)
    {
        _page = p;
        OnPropertyChanged(nameof(IsProcessorPage));
        OnPropertyChanged(nameof(IsSettingsPage));
    }

    // ---- devices ----
    public List<MMDevice> Inputs { get; private set; } = new();
    public List<MMDevice> Outputs { get; private set; } = new();

    private MMDevice _selectedInput;
    public MMDevice SelectedInput { get => _selectedInput; set => Set(ref _selectedInput, value); }

    private MMDevice _selectedOutput;
    public MMDevice SelectedOutput { get => _selectedOutput; set => Set(ref _selectedOutput, value); }

    // ---- run state ----
    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (Set(ref _isRunning, value))
            {
                OnPropertyChanged(nameof(StartStopText));
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(NotRunning));
            }
        }
    }

    public bool NotRunning => !_isRunning;
    public string StartStopText => _isRunning ? "Stop" : "Start";
    public string StatusText => _isRunning ? "Processing" : "Stopped";

    // ---- meters ----
    private double _inLevel, _outLevel;
    public double InLevel { get => _inLevel; private set => Set(ref _inLevel, value); }
    public double OutLevel { get => _outLevel; private set => Set(ref _outLevel, value); }

    private double _compLevelDb = -100;
    public double CompLevelDb { get => _compLevelDb; private set => Set(ref _compLevelDb, value); }

    private string _grText = "0.0 dB";
    public string GrText { get => _grText; private set => Set(ref _grText, value); }

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

    public string VersionText => "MicForge · v0.2 · PolyForm Noncommercial 1.0.0";

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
        Stages.Add(input);

        var hp = new StageViewModel("High-Pass", "#4FA3E3", () => c.HighPass.Enabled, v => c.HighPass.Enabled = v)
        {
            Info = "Rolls off low-frequency rumble — desk thumps, footsteps, AC hum, plosives — below the cutoff, cleaning the low end without thinning your voice."
        };
        hp.Add("Frequency", 20, 300, 5, () => c.HighPass.Frequency, v => c.HighPass.Frequency = v, "0", " Hz",
            "Everything below this frequency is removed. 80–100 Hz suits most voices.");
        Stages.Add(hp);

        var ns = new StageViewModel("Noise Suppression", "#8E7CE6", () => c.Suppressor.Enabled, v => c.Suppressor.Enabled = v)
        {
            Info = "AI (RNNoise) removal of steady background noise like fans, hiss and hum, while leaving speech intact. This is the modern replacement for a plain driver's noise reduction."
        };
        if (!c.Suppressor.Available)
        {
            ns.ToggleEnabled = false;
            ns.Note = "Drop rnnoise.dll next to MicForge.exe to enable AI noise suppression.";
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
        Stages.Add(gate);

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

        var lim = new StageViewModel("Limiter", "#E5543B", () => c.Limiter.Enabled, v => c.Limiter.Enabled = v)
        {
            Info = "A safety net that stops the output from ever exceeding the ceiling, preventing digital clipping and distortion on sudden peaks."
        };
        lim.Add("Ceiling", -12, 0, 0.1, () => c.Limiter.CeilingDb, v => c.Limiter.CeilingDb = v, "0.0", " dB",
            "The maximum output level. Nothing gets past this.");
        lim.Add("Release", 10, 500, 5, () => c.Limiter.ReleaseMs, v => c.Limiter.ReleaseMs = v, "0", " ms",
            "How quickly the limiter recovers after catching a peak.");
        Stages.Add(lim);

        var output = new StageViewModel("Output", "#6C7A89", () => true, _ => { }, canToggle: false)
        {
            Info = "Final level sent to the virtual mic. Set it so your loudest speech peaks land around -6 to -3 dB on the output meter."
        };
        output.Add("Gain", -24, 24, 0.5, () => c.OutputGain.GainDb, v => c.OutputGain.GainDb = v, "0.0", " dB",
            "Overall output level in decibels.");
        Stages.Add(output);
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
        SelectedInput = Inputs.FirstOrDefault(d => d.ID == saved?.InputDeviceId)
                        ?? Inputs.FirstOrDefault(d => d.ID == AudioEngine.DefaultInputId())
                        ?? Inputs.FirstOrDefault();

        SelectedOutput = Outputs.FirstOrDefault(d => d.ID == saved?.OutputDeviceId)
                         ?? Outputs.FirstOrDefault(d =>
                             d.FriendlyName.IndexOf("CABLE Input", StringComparison.OrdinalIgnoreCase) >= 0)
                         ?? Outputs.FirstOrDefault();
    }

    // ---- run ----
    private void ToggleRun()
    {
        if (IsRunning)
        {
            _engine.Stop();
            IsRunning = false;
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
            IsRunning = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Could not start audio");
        }
    }

    private void UpdateMeters()
    {
        var chain = _engine.Chain;
        float ip = chain.InputPeak;
        InLevel = ToMeter(ip);
        OutLevel = ToMeter(chain.OutputPeak);
        CompLevelDb = ip <= 0.00001f ? -100 : 20 * Math.Log10(ip);

        var comp = chain.Compressor;
        GrText = $"{comp.GainReductionDb:0.0} dB";
        CompGrDb = comp.GainReductionDb;

        var g = chain.Gate;
        GateThreshold = Norm(g.ThresholdDb, -80, 0);
        GateLevel = Norm(g.DetectorDb, -80, 0);
        GateOpen = g.Enabled && g.DetectorDb >= g.ThresholdDb;

        var d = chain.DeEsser;
        DeEsserThreshold = Norm(d.ThresholdDb, -60, 0);
        DeEsserLevel = Norm(d.DetectorDb, -60, 0);
        DeEsserActive = d.Enabled && d.ReductionDb < -0.3;
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
        s.InputDeviceId = SelectedInput?.ID;
        s.OutputDeviceId = SelectedOutput?.ID;
        s.AutoStartProcessing = AutoStartProcessing;
        s.StartMinimized = MinimizeToTray;
        s.VisualMode = VisualMode;
        return s;
    }

    public void SaveSettings()
    {
        try { Snapshot().Save(_settingsPath); } catch { }
    }

    public void Shutdown()
    {
        _meterTimer?.Stop();
        SaveSettings();
        _engine.Dispose();
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
    }
}
