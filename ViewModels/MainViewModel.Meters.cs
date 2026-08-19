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

/// <summary>Live meter, mic-health, reliability and loudness read-outs, and the per-tick update pass that fills them.</summary>
public sealed partial class MainViewModel
{
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
}
