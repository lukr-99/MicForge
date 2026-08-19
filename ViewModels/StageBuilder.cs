using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.Input;
using MicForge.Audio;

namespace MicForge.ViewModels;

/// <summary>The action-button callbacks a stage card can trigger, supplied by the owner view-model.</summary>
public sealed record StageActions(Action Calibrate, Action BrowseRnnoise, Action LearnNoise);

/// <summary>
/// Builds the ordered list of processing-stage cards for a <see cref="DspChain"/>. These
/// definitions are long and purely declarative (title, colour, info text, and each parameter's
/// range/format/help wired straight to a chain processor), so they live here instead of bloating
/// <see cref="MainViewModel"/>. Rebuilt whenever the chain identity changes (device switch, order
/// reset, RNNoise (un)loaded).
/// </summary>
public sealed class StageBuilder
{
    private readonly DspChain _c;
    private readonly StageActions _actions;
    private readonly List<StageViewModel> _stages = new();

    public StageBuilder(DspChain chain, StageActions actions)
    {
        _c = chain;
        _actions = actions;
    }

    /// <summary>The EQ card produced by <see cref="Build"/> (the graph view and crafting need it).</summary>
    public EqStageViewModel EqStage { get; private set; }

    /// <summary>Create every stage card in the chain's default order and map each to its processor.</summary>
    public List<StageViewModel> Build()
    {
        var c = _c;
        var stages = _stages;
        stages.Clear();

        var input = new StageViewModel("Input", "#6C7A89", () => true, _ => { }, canToggle: false)
        {
            Info = "Sets the level of the raw microphone signal before any processing. Use it to bring a quiet mic up or tame a hot one so the input meter sits around the middle."
        };
        input.Add("Gain", -24, 24, 0.5, () => c.InputGain.GainDb, v => c.InputGain.GainDb = v, "0.0", " dB",
            "Boosts or cuts the incoming signal. Aim for speech peaks that don't slam the top of the meter.");
        input.ShowAction = true;
        input.ActionText = "Auto-calibrate";
        input.ActionCommand = new RelayCommand(_actions.Calibrate);
        stages.Add(input);

        var agc = new StageViewModel("Auto Gain", "#7FB069", () => c.InputAgc.Enabled, v => c.InputAgc.Enabled = v)
        {
            Info = "Automatically rides the input gain to hold a steady speech level as your mic distance or volume change (gated on silence). Complements the output loudness leveler."
        };
        agc.Add("Target", -30, -6, 0.5, () => c.InputAgc.TargetDb, v => c.InputAgc.TargetDb = v, "0.0", " dB",
            "The average speech level the auto-gain aims for.");
        agc.Add("Max gain", 0, 18, 1, () => c.InputAgc.MaxGainDb, v => c.InputAgc.MaxGainDb = v, "0", " dB",
            "How much boost or cut the auto-gain may apply.");
        stages.Add(agc);

        var hp = new StageViewModel("High-Pass", "#4FA3E3", () => c.HighPass.Enabled, v => c.HighPass.Enabled = v)
        {
            Info = "Rolls off low-frequency rumble — desk thumps, footsteps, AC hum, plosives — below the cutoff, cleaning the low end without thinning your voice."
        };
        hp.Add("Frequency", 20, 300, 5, () => c.HighPass.Frequency, v => c.HighPass.Frequency = v, "0", " Hz",
            "Everything below this frequency is removed. 80–100 Hz suits most voices.");
        stages.Add(hp);

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
        stages.Add(hum);

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
        stages.Add(dep);

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
            ns.ActionCommand = new RelayCommand(_actions.BrowseRnnoise);
        }
        else
        {
            ns.Note = "RNNoise loaded — enable the toggle to remove steady background noise.";
        }
        stages.Add(ns);

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
        gate.ActionCommand = new RelayCommand(_actions.LearnNoise);
        if (c.Suppressor.Available)
        {
            gate.SetExtraToggle("Smart — open on voice (RNNoise)", () => c.Gate.UseVad, v => c.Gate.UseVad = v);
            gate.Add("Voice sens", 0.1, 0.9, 0.05, () => c.Gate.VadThreshold, v => c.Gate.VadThreshold = v, "0.00", "",
                "How sure RNNoise must be that it's your voice before the smart gate opens.");
        }
        stages.Add(gate);

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
        stages.Add(expander);

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
        stages.Add(declick);

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
        stages.Add(keys);

        var derev = new StageViewModel("De-Reverb", "#6CA0B0", () => c.DeReverb.Enabled, v => c.DeReverb.Enabled = v)
        {
            Info = "Tightens up a boomy or echoey room by pulling down the reverb tail — the energy lingering after each word — while keeping the direct voice. A light reducer, not a full acoustic dereverb."
        };
        derev.Add("Amount", 0, 100, 1, () => c.DeReverb.Amount, v => c.DeReverb.Amount = v, "0", " %",
            "How hard the room tail is suppressed.");
        derev.Add("Decay", 40, 400, 10, () => c.DeReverb.DecayMs, v => c.DeReverb.DecayMs = v, "0", " ms",
            "The room's tail time — match it to how long echoes ring out.");
        stages.Add(derev);

        var echo = new StageViewModel("Echo Remover", "#6C9AB0", () => c.EchoRemover.Enabled, v => c.EchoRemover.Enabled = v)
        {
            Info = "Cancels a distinct echo of your own voice — a slap-back off a wall, or your speakers bleeding back into the mic. An adaptive filter learns the delayed copy and subtracts it. Set Delay to roughly the echo time and raise Strength until the echo drops. (Not full acoustic echo cancellation — it has no reference to the far-end audio.)"
        };
        echo.Add("Delay", 20, 300, 5, () => c.EchoRemover.DelayMs, v => c.EchoRemover.DelayMs = v, "0", " ms",
            "Roughly how long after your voice the echo arrives. Sweep this until the echo locks on and drops.");
        echo.Add("Strength", 0, 100, 1, () => c.EchoRemover.Strength, v => c.EchoRemover.Strength = v, "0", " %",
            "How much of the estimated echo to remove.");
        stages.Add(echo);

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
        EqStage = eq;
        stages.Add(eq);

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
        stages.Add(comp);

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
        stages.Add(mb);

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
        stages.Add(de);

        var sat = new StageViewModel("Saturation", "#D9A05B", () => c.Saturation.Enabled, v => c.Saturation.Enabled = v)
        {
            Info = "Adds gentle harmonic warmth and character by softly saturating the signal. Great for thin or clinical mics — keep it subtle."
        };
        sat.Add("Drive", 0, 24, 0.5, () => c.Saturation.DriveDb, v => c.Saturation.DriveDb = v, "0.0", " dB",
            "How hard the signal is pushed into the saturation curve. More drive = more harmonics.");
        sat.Add("Mix", 0, 100, 1, () => c.Saturation.Mix, v => c.Saturation.Mix = v, "0", " %",
            "Blend between the dry signal and the saturated signal.");
        stages.Add(sat);

        var exc = new StageViewModel("Exciter", "#E3C85F", () => c.Exciter.Enabled, v => c.Exciter.Enabled = v)
        {
            Info = "Generates brand-new high harmonics to add air, sparkle and presence — a livelier top end than just boosting EQ. Keep it subtle."
        };
        exc.Add("Frequency", 1500, 8000, 100, () => c.Exciter.Frequency, v => c.Exciter.Frequency = v, "0", " Hz",
            "Harmonics are generated above this frequency.");
        exc.Add("Amount", 0, 100, 1, () => c.Exciter.Amount, v => c.Exciter.Amount = v, "0", " %",
            "How much of the generated sparkle to blend in.");
        stages.Add(exc);

        var voice = new StageViewModel("Voice Changer", "#9B6CE3", () => c.VoiceChanger.Enabled, v => c.VoiceChanger.Enabled = v)
        {
            Info = "Shifts your pitch up or down in real time — deep villain, chipmunk, or a subtle disguise. 0 semitones passes through untouched; ±12 is a full octave. (Also driven by the Crafting tab.)"
        };
        voice.Add("Pitch", -12, 12, 1, () => c.VoiceChanger.Semitones, v => c.VoiceChanger.Semitones = v, "0", " st",
            "How many semitones to shift. Negative = deeper, positive = higher.");
        voice.Add("Mix", 0, 100, 1, () => c.VoiceChanger.Mix, v => c.VoiceChanger.Mix = v, "0", " %",
            "Blend between your natural voice and the shifted voice.");
        stages.Add(voice);

        var cn = new StageViewModel("Comfort Noise", "#7C8AA0", () => c.ComfortNoise.Enabled, v => c.ComfortNoise.Enabled = v)
        {
            Info = "Fills gate/expander silence with a faint, soft noise bed so callers don't think you dropped off. It fades in only when you're not speaking, so it never muddies your voice."
        };
        cn.Add("Level", -80, -40, 1, () => c.ComfortNoise.LevelDb, v => c.ComfortNoise.LevelDb = v, "0", " dB",
            "How loud the noise bed sits. Keep it just audible.");
        cn.Add("Tone", 500, 6000, 100, () => c.ComfortNoise.ToneHz, v => c.ComfortNoise.ToneHz = v, "0", " Hz",
            "Softens the hiss — lower is duller and warmer.");
        stages.Add(cn);

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
        stages.Add(lim);

        var output = new StageViewModel("Output", "#6C7A89", () => true, _ => { }, canToggle: false)
        {
            Info = "Final level sent to the virtual mic. Set it so your loudest speech peaks land around -6 to -3 dB on the output meter."
        };
        output.Add("Gain", -24, 24, 0.5, () => c.OutputGain.GainDb, v => c.OutputGain.GainDb = v, "0.0", " dB",
            "Overall output level in decibels.");
        stages.Add(output);

        var loud = new StageViewModel("Loudness", "#5FA8D3", () => c.Loudness.AutoLevel, v => c.Loudness.AutoLevel = v)
        {
            Info = "Measures perceived loudness (LUFS) and, when enabled, slowly rides the gain to keep you at a consistent target — great for streaming and podcasts. The live reading is on the meter panel."
        };
        loud.Add("Target", -30, -10, 0.5, () => c.Loudness.TargetLufs, v => c.Loudness.TargetLufs = v, "0.0", " LUFS",
            "The loudness the auto-leveler aims for. Around -16 LUFS is a common streaming target.");
        loud.Add("Max gain", 0, 18, 1, () => c.Loudness.MaxGainDb, v => c.Loudness.MaxGainDb = v, "0", " dB",
            "How much the auto-leveler may boost or cut to reach the target.");
        stages.Add(loud);

        // Map each card to its processor (built in the chain's default order).
        var procs = new IAudioProcessor[]
        {
            c.InputGain, c.InputAgc, c.HighPass, c.Hum, c.DePlosive, c.Suppressor, c.Gate, c.Expander, c.DeClicker,
            c.Keystroke, c.DeReverb, c.EchoRemover, c.Eq, c.Compressor, c.Multiband, c.DeEsser, c.Saturation,
            c.Exciter, c.VoiceChanger, c.ComfortNoise, c.Limiter, c.OutputGain, c.Loudness
        };
        for (int i = 0; i < stages.Count && i < procs.Length; i++) stages[i].Processor = procs[i];

        return stages;
    }
}
