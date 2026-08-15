# MicForge

A real-time microphone DSP processor for Windows. It captures your mic, runs a full
processing chain, and outputs to a virtual audio device so **any** app (Discord, OBS,
games, browser) can use the cleaned-up, shaped signal as its "microphone".

Written in C# / .NET 10 / WinForms using [NAudio](https://github.com/naudio/NAudio)
for WASAPI capture and render. The DSP is hand-written (no VST dependency).

## Signal chain

```
Mic ─▶ Input gain ─▶ High-pass ─▶ Noise suppression (RNNoise*) ─▶ Gate
    ─▶ Parametric EQ ─▶ Compressor ─▶ De-esser ─▶ Limiter ─▶ Output gain ─▶ virtual mic
```

Every stage can be toggled and tuned live. Settings auto-save on exit and reload on
launch; you can also Save/Load named presets (`.json`).

## Requirements

1. **.NET 10 Desktop Runtime** (already covered if you have the SDK).
2. **[VB-CABLE](https://vb-audio.com/Cable/)** (free) — creates the virtual mic.
   After installing, MicForge sends audio to **"CABLE Input"**, and in your app you
   select **"CABLE Output"** as the microphone.

## Usage

1. Install VB-CABLE and reboot.
2. Run MicForge.
3. **Input** = your real mic. **Output** = `CABLE Input (VB-Audio Virtual Cable)`
   (auto-selected if found).
4. Click **Start**. Speak — watch the In/Out meters and the compressor GR readout.
5. In Discord/OBS/etc., set the microphone to `CABLE Output`.

To monitor yourself while tuning, set Output to your headphones instead of the cable
(expect a bit of latency — that's normal for the two-device WASAPI path).

## Optional: RNNoise AI noise suppression

The Noise Suppression stage is disabled and greyed out until a `rnnoise.dll` is placed
next to `MicForge.exe`. Use a 64-bit build of [RNNoise](https://github.com/xiph/rnnoise)
(or a prebuilt `librnnoise`/`rnnoise.dll`). Once present, the stage enables itself.

## Notes / roadmap

- Capture and render run on two independent device clocks; a 500 ms input buffer plus
  overflow discard absorbs the drift. Fine for voice; not sample-accurate.
- Ideas: spectrum/gain-reduction visualisation, per-band EQ type selection in the UI,
  A/B bypass, VST3 hosting, exclusive-mode low-latency path.

## License

[PolyForm Noncommercial 1.0.0](LICENSE.md) — free for noncommercial use.
