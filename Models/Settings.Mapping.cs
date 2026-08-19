using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using MicForge.Audio;

namespace MicForge;

/// <summary>Maps the <see cref="Settings"/> DTO to and from a live <see cref="DspChain"/>:
/// <see cref="CaptureFrom"/> reads the chain into a snapshot, <see cref="ApplyTo"/> writes one back.</summary>
public sealed partial class Settings
{
    public static Settings CaptureFrom(DspChain c)
    {
        var s = new Settings
        {
            InputGainDb = c.InputGain.GainDb,
            AgcEnabled = c.InputAgc.Enabled,
            AgcTargetDb = c.InputAgc.TargetDb,
            AgcMaxGainDb = c.InputAgc.MaxGainDb,
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
            GateUseVad = c.Gate.UseVad,
            GateVadThreshold = c.Gate.VadThreshold,
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
            MaxAutoGainDb = c.Loudness.MaxGainDb,
            HumEnabled = c.Hum.Enabled,
            HumFreq = c.Hum.Frequency,
            HumHarmonics = c.Hum.Harmonics,
            HumQ = c.Hum.Q,
            DePlosiveEnabled = c.DePlosive.Enabled,
            DePlosiveFreq = c.DePlosive.Frequency,
            DePlosiveThresholdDb = c.DePlosive.ThresholdDb,
            DePlosiveStrength = c.DePlosive.Strength,
            DeClickEnabled = c.DeClicker.Enabled,
            DeClickFreq = c.DeClicker.Frequency,
            DeClickSensitivity = c.DeClicker.Sensitivity,
            DeClickStrength = c.DeClicker.Strength,
            VoiceEnabled = c.VoiceChanger.Enabled,
            VoiceSemitones = c.VoiceChanger.Semitones,
            VoiceMix = c.VoiceChanger.Mix,
            ExpanderEnabled = c.Expander.Enabled,
            ExpanderThresholdDb = c.Expander.ThresholdDb,
            ExpanderRatio = c.Expander.Ratio,
            ExpanderReleaseMs = c.Expander.ReleaseMs,
            ExpanderRangeDb = c.Expander.RangeDb,
            DeReverbEnabled = c.DeReverb.Enabled,
            DeReverbAmount = c.DeReverb.Amount,
            DeReverbDecayMs = c.DeReverb.DecayMs,
            MultibandEnabled = c.Multiband.Enabled,
            MbCrossLow = c.Multiband.CrossLow,
            MbCrossHigh = c.Multiband.CrossHigh,
            MbThreshLowDb = c.Multiband.ThreshLowDb,
            MbThreshMidDb = c.Multiband.ThreshMidDb,
            MbThreshHighDb = c.Multiband.ThreshHighDb,
            MbRatio = c.Multiband.Ratio,
            MbMakeupDb = c.Multiband.MakeupDb,
            ExciterEnabled = c.Exciter.Enabled,
            ExciterFreq = c.Exciter.Frequency,
            ExciterAmount = c.Exciter.Amount,
            ComfortNoiseEnabled = c.ComfortNoise.Enabled,
            ComfortNoiseLevelDb = c.ComfortNoise.LevelDb,
            ComfortNoiseToneHz = c.ComfortNoise.ToneHz,
            KeystrokeEnabled = c.Keystroke.Enabled,
            KeystrokeDetectFreq = c.Keystroke.DetectFreq,
            KeystrokeSensitivity = c.Keystroke.Sensitivity,
            KeystrokeStrength = c.Keystroke.Strength,
            KeystrokeReleaseMs = c.Keystroke.ReleaseMs,
            EchoEnabled = c.EchoRemover.Enabled,
            EchoDelayMs = c.EchoRemover.DelayMs,
            EchoStrength = c.EchoRemover.Strength
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
        c.InputAgc.Enabled = AgcEnabled;
        c.InputAgc.TargetDb = AgcTargetDb;
        c.InputAgc.MaxGainDb = AgcMaxGainDb;
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
        c.Gate.UseVad = GateUseVad;
        c.Gate.VadThreshold = GateVadThreshold;
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
        c.Hum.Enabled = HumEnabled;
        c.Hum.Frequency = HumFreq;
        c.Hum.Harmonics = HumHarmonics;
        c.Hum.Q = HumQ;
        c.DePlosive.Enabled = DePlosiveEnabled;
        c.DePlosive.Frequency = DePlosiveFreq;
        c.DePlosive.ThresholdDb = DePlosiveThresholdDb;
        c.DePlosive.Strength = DePlosiveStrength;
        c.DeClicker.Enabled = DeClickEnabled;
        c.DeClicker.Frequency = DeClickFreq;
        c.DeClicker.Sensitivity = DeClickSensitivity;
        c.DeClicker.Strength = DeClickStrength;
        c.VoiceChanger.Enabled = VoiceEnabled;
        c.VoiceChanger.Semitones = VoiceSemitones;
        c.VoiceChanger.Mix = VoiceMix;
        c.Expander.Enabled = ExpanderEnabled;
        c.Expander.ThresholdDb = ExpanderThresholdDb;
        c.Expander.Ratio = ExpanderRatio;
        c.Expander.ReleaseMs = ExpanderReleaseMs;
        c.Expander.RangeDb = ExpanderRangeDb;
        c.DeReverb.Enabled = DeReverbEnabled;
        c.DeReverb.Amount = DeReverbAmount;
        c.DeReverb.DecayMs = DeReverbDecayMs;
        c.Multiband.Enabled = MultibandEnabled;
        c.Multiband.CrossLow = MbCrossLow;
        c.Multiband.CrossHigh = MbCrossHigh;
        c.Multiband.ThreshLowDb = MbThreshLowDb;
        c.Multiband.ThreshMidDb = MbThreshMidDb;
        c.Multiband.ThreshHighDb = MbThreshHighDb;
        c.Multiband.Ratio = MbRatio;
        c.Multiband.MakeupDb = MbMakeupDb;
        c.Exciter.Enabled = ExciterEnabled;
        c.Exciter.Frequency = ExciterFreq;
        c.Exciter.Amount = ExciterAmount;
        c.ComfortNoise.Enabled = ComfortNoiseEnabled;
        c.ComfortNoise.LevelDb = ComfortNoiseLevelDb;
        c.ComfortNoise.ToneHz = ComfortNoiseToneHz;
        c.Keystroke.Enabled = KeystrokeEnabled;
        c.Keystroke.DetectFreq = KeystrokeDetectFreq;
        c.Keystroke.Sensitivity = KeystrokeSensitivity;
        c.Keystroke.Strength = KeystrokeStrength;
        c.Keystroke.ReleaseMs = KeystrokeReleaseMs;
        c.EchoRemover.Enabled = EchoEnabled;
        c.EchoRemover.DelayMs = EchoDelayMs;
        c.EchoRemover.Strength = EchoStrength;

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
}
