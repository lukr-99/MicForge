using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using MicForge.Audio;

namespace MicForge;

/// <summary>
/// Serializable snapshot of the whole DSP chain plus app-level state (device selection,
/// stage order, hotkeys, crafting card states, last preset…). Persisted as
/// <c>%AppData%\MicForge\micforge.json</c> (see <see cref="DefaultPath"/>).
/// </summary>
public sealed partial class Settings
{
    public string InputDeviceId { get; set; }
    public string OutputDeviceId { get; set; }

    public double InputGainDb { get; set; }

    public bool AgcEnabled { get; set; }
    public double AgcTargetDb { get; set; } = -18;
    public double AgcMaxGainDb { get; set; } = 12;

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
    public bool GateUseVad { get; set; }
    public double GateVadThreshold { get; set; } = 0.6;

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

    public bool HumEnabled { get; set; }
    public double HumFreq { get; set; } = 50;
    public int HumHarmonics { get; set; } = 4;
    public double HumQ { get; set; } = 30;

    public bool DePlosiveEnabled { get; set; }
    public double DePlosiveFreq { get; set; } = 150;
    public double DePlosiveThresholdDb { get; set; } = -30;
    public double DePlosiveStrength { get; set; } = 70;

    public bool DeClickEnabled { get; set; }
    public double DeClickFreq { get; set; } = 3000;
    public double DeClickSensitivity { get; set; } = 6;
    public double DeClickStrength { get; set; } = 70;

    public bool VoiceEnabled { get; set; }
    public double VoiceSemitones { get; set; }
    public double VoiceMix { get; set; } = 100;

    public bool ExpanderEnabled { get; set; }
    public double ExpanderThresholdDb { get; set; } = -45;
    public double ExpanderRatio { get; set; } = 2.5;
    public double ExpanderReleaseMs { get; set; } = 150;
    public double ExpanderRangeDb { get; set; } = -24;

    public bool DeReverbEnabled { get; set; }
    public double DeReverbAmount { get; set; } = 50;
    public double DeReverbDecayMs { get; set; } = 150;

    public bool MultibandEnabled { get; set; }
    public double MbCrossLow { get; set; } = 250;
    public double MbCrossHigh { get; set; } = 3000;
    public double MbThreshLowDb { get; set; } = -24;
    public double MbThreshMidDb { get; set; } = -20;
    public double MbThreshHighDb { get; set; } = -22;
    public double MbRatio { get; set; } = 3;
    public double MbMakeupDb { get; set; }

    public bool ExciterEnabled { get; set; }
    public double ExciterFreq { get; set; } = 3000;
    public double ExciterAmount { get; set; } = 25;

    public bool ComfortNoiseEnabled { get; set; }
    public double ComfortNoiseLevelDb { get; set; } = -60;
    public double ComfortNoiseToneHz { get; set; } = 2000;

    public bool KeystrokeEnabled { get; set; }
    public double KeystrokeDetectFreq { get; set; } = 2800;
    public double KeystrokeSensitivity { get; set; } = 8;
    public double KeystrokeStrength { get; set; } = 70;
    public double KeystrokeReleaseMs { get; set; } = 45;

    public bool EchoEnabled { get; set; }
    public double EchoDelayMs { get; set; } = 120;
    public double EchoStrength { get; set; } = 60;

    // App-level (not part of the DSP chain).
    public bool AutoStartProcessing { get; set; }
    public bool StartMinimized { get; set; } = true;
    public bool VisualMode { get; set; }
    public List<string> StageOrder { get; set; }
    public bool MonitorEnabled { get; set; }
    public string MonitorDeviceId { get; set; }
    public bool GlobalHotkeysEnabled { get; set; }
    public bool FollowDefaultInput { get; set; }
    public bool ShowMuteOverlay { get; set; } = true;
    public bool PttEnabled { get; set; }
    public bool PttHoldToTalk { get; set; } = true;
    public uint PttVk { get; set; }
    public List<HotkeyBinding> Hotkeys { get; set; }
    public List<CraftCardState> CraftCards { get; set; }
    public string LastPreset { get; set; }

    /// <summary>
    /// Where settings live: <c>%AppData%\MicForge\micforge.json</c>. Kept out of the install
    /// folder so it survives uninstall / reinstall / update. On first use, migrates an existing
    /// file from the old next-to-exe location.
    /// </summary>
    public static string DefaultPath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MicForge");
        try { Directory.CreateDirectory(dir); } catch { }
        var path = Path.Combine(dir, "micforge.json");

        if (!File.Exists(path))
        {
            var legacy = Path.Combine(AppContext.BaseDirectory, "micforge.json");
            if (File.Exists(legacy))
            {
                try { File.Copy(legacy, path); } catch { }
            }
        }
        return path;
    }

    public void Save(string path) => File.WriteAllText(path, ToJson());

    public string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });

    public static Settings FromJson(string json)
    {
        try { return JsonSerializer.Deserialize<Settings>(json); }
        catch { return null; }
    }

    public static Settings Load(string path)
    {
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(path)); }
        catch { return null; }
    }
}
