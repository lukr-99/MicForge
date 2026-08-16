using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using MicForge.Audio;

namespace MicForge;

public sealed class EqBandSetting
{
    public bool Enabled { get; set; } = true;
    public int Type { get; set; }
    public double Freq { get; set; }
    public double GainDb { get; set; }
    public double Q { get; set; }
}

/// <summary>Serializable snapshot of the whole DSP chain plus device selection.</summary>
public sealed class Settings
{
    public string InputDeviceId { get; set; }
    public string OutputDeviceId { get; set; }

    public double InputGainDb { get; set; }

    public bool HighPassEnabled { get; set; } = true;
    public double HighPassFreq { get; set; } = 80;

    public bool SuppressorEnabled { get; set; }
    public string RnnoisePath { get; set; }

    public bool GateEnabled { get; set; } = true;
    public double GateThresholdDb { get; set; } = -45;
    public double GateAttackMs { get; set; } = 3;
    public double GateHoldMs { get; set; } = 150;
    public double GateReleaseMs { get; set; } = 200;
    public double GateRangeDb { get; set; } = -70;

    public bool EqEnabled { get; set; } = true;
    public List<EqBandSetting> EqBands { get; set; } = new();

    public bool CompEnabled { get; set; } = true;
    public double CompThresholdDb { get; set; } = -18;
    public double CompRatio { get; set; } = 3;
    public double CompAttackMs { get; set; } = 10;
    public double CompReleaseMs { get; set; } = 120;
    public double CompKneeDb { get; set; } = 6;
    public double CompMakeupDb { get; set; }

    public bool DeEsserEnabled { get; set; } = true;
    public double DeEsserFreq { get; set; } = 6500;
    public double DeEsserThresholdDb { get; set; } = -28;
    public double DeEsserRatio { get; set; } = 4;

    public bool SatEnabled { get; set; }
    public double SatDriveDb { get; set; } = 6;
    public double SatMix { get; set; } = 35;

    public bool LimiterEnabled { get; set; } = true;
    public double LimiterCeilingDb { get; set; } = -1;
    public double LimiterReleaseMs { get; set; } = 60;
    public double LimiterLookaheadMs { get; set; } = 2.0;

    public double OutputGainDb { get; set; }

    public bool AutoLevel { get; set; }
    public double TargetLufs { get; set; } = -16;
    public double MaxAutoGainDb { get; set; } = 12;

    // App-level (not part of the DSP chain).
    public bool AutoStartProcessing { get; set; }
    public bool StartMinimized { get; set; } = true;
    public bool VisualMode { get; set; }
    public bool MonitorEnabled { get; set; }
    public string MonitorDeviceId { get; set; }

    public static Settings CaptureFrom(DspChain c)
    {
        var s = new Settings
        {
            InputGainDb = c.InputGain.GainDb,
            HighPassEnabled = c.HighPass.Enabled,
            HighPassFreq = c.HighPass.Frequency,
            SuppressorEnabled = c.Suppressor.Enabled,
            RnnoisePath = c.Suppressor.LoadedPath,
            GateEnabled = c.Gate.Enabled,
            GateThresholdDb = c.Gate.ThresholdDb,
            GateAttackMs = c.Gate.AttackMs,
            GateHoldMs = c.Gate.HoldMs,
            GateReleaseMs = c.Gate.ReleaseMs,
            GateRangeDb = c.Gate.RangeDb,
            EqEnabled = c.Eq.Enabled,
            CompEnabled = c.Compressor.Enabled,
            CompThresholdDb = c.Compressor.ThresholdDb,
            CompRatio = c.Compressor.Ratio,
            CompAttackMs = c.Compressor.AttackMs,
            CompReleaseMs = c.Compressor.ReleaseMs,
            CompKneeDb = c.Compressor.KneeDb,
            CompMakeupDb = c.Compressor.MakeupDb,
            DeEsserEnabled = c.DeEsser.Enabled,
            DeEsserFreq = c.DeEsser.Frequency,
            DeEsserThresholdDb = c.DeEsser.ThresholdDb,
            DeEsserRatio = c.DeEsser.Ratio,
            SatEnabled = c.Saturation.Enabled,
            SatDriveDb = c.Saturation.DriveDb,
            SatMix = c.Saturation.Mix,
            LimiterEnabled = c.Limiter.Enabled,
            LimiterCeilingDb = c.Limiter.CeilingDb,
            LimiterReleaseMs = c.Limiter.ReleaseMs,
            LimiterLookaheadMs = c.Limiter.LookaheadMs,
            OutputGainDb = c.OutputGain.GainDb,
            AutoLevel = c.Loudness.AutoLevel,
            TargetLufs = c.Loudness.TargetLufs,
            MaxAutoGainDb = c.Loudness.MaxGainDb
        };
        foreach (var b in c.Eq.Bands)
            s.EqBands.Add(new EqBandSetting
            {
                Enabled = b.Enabled,
                Type = (int)b.Type,
                Freq = b.Freq,
                GainDb = b.GainDb,
                Q = b.Q
            });
        return s;
    }

    public void ApplyTo(DspChain c)
    {
        c.InputGain.GainDb = InputGainDb;
        c.HighPass.Enabled = HighPassEnabled;
        c.HighPass.Frequency = HighPassFreq;
        if (!string.IsNullOrEmpty(RnnoisePath)) c.Suppressor.TryLoad(RnnoisePath);
        c.Suppressor.Enabled = SuppressorEnabled && c.Suppressor.Available;
        c.Gate.Enabled = GateEnabled;
        c.Gate.ThresholdDb = GateThresholdDb;
        c.Gate.AttackMs = GateAttackMs;
        c.Gate.HoldMs = GateHoldMs;
        c.Gate.ReleaseMs = GateReleaseMs;
        c.Gate.RangeDb = GateRangeDb;
        c.Eq.Enabled = EqEnabled;
        c.Compressor.Enabled = CompEnabled;
        c.Compressor.ThresholdDb = CompThresholdDb;
        c.Compressor.Ratio = CompRatio;
        c.Compressor.AttackMs = CompAttackMs;
        c.Compressor.ReleaseMs = CompReleaseMs;
        c.Compressor.KneeDb = CompKneeDb;
        c.Compressor.MakeupDb = CompMakeupDb;
        c.DeEsser.Enabled = DeEsserEnabled;
        c.DeEsser.Frequency = DeEsserFreq;
        c.DeEsser.ThresholdDb = DeEsserThresholdDb;
        c.DeEsser.Ratio = DeEsserRatio;
        c.Saturation.Enabled = SatEnabled;
        c.Saturation.DriveDb = SatDriveDb;
        c.Saturation.Mix = SatMix;
        c.Limiter.Enabled = LimiterEnabled;
        c.Limiter.CeilingDb = LimiterCeilingDb;
        c.Limiter.ReleaseMs = LimiterReleaseMs;
        c.Limiter.LookaheadMs = LimiterLookaheadMs;
        c.OutputGain.GainDb = OutputGainDb;
        c.Loudness.AutoLevel = AutoLevel;
        c.Loudness.TargetLufs = TargetLufs;
        c.Loudness.MaxGainDb = MaxAutoGainDb;

        for (int i = 0; i < EqBands.Count && i < c.Eq.Bands.Count; i++)
        {
            var src = EqBands[i];
            var dst = c.Eq.Bands[i];
            dst.Enabled = src.Enabled;
            dst.Type = (Biquad.FilterType)src.Type;
            dst.Freq = src.Freq;
            dst.GainDb = src.GainDb;
            dst.Q = src.Q;
        }
        c.Eq.UpdateAll();
    }

    public void Save(string path)
    {
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    public static Settings Load(string path)
    {
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(path)); }
        catch { return null; }
    }
}
