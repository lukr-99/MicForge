# MicForge

A real-time microphone DSP processor for Windows. It captures your mic, runs a full
processing chain, and outputs to a virtual audio device so **any** app (Discord, OBS,
games, browser) can use the cleaned-up, shaped signal as its "microphone".

Written in C# / .NET 10 / WPF using [NAudio](https://github.com/naudio/NAudio)
for WASAPI capture and render. The DSP is hand-written (no VST dependency), and the
dark UI is a hand-rolled theme (no control-library dependency).

Features: dark theme, per-stage on/off toggles + faders with live readouts, In/Out
level meters + compressor gain-reduction readout, JSON preset save/load, minimize/
close to the system tray, and an optional "Start with Windows" entry.

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

## RNNoise AI noise suppression

A 64-bit `rnnoise.dll` — built from the official [xiph/rnnoise](https://github.com/xiph/rnnoise)
source — is bundled in [`native/`](native/) and copied next to the exe on build, so the
Noise Suppression stage works out of the box. See [`native/RNNOISE-LICENSE.txt`](native/RNNOISE-LICENSE.txt)
(BSD) for its license. You can also point the app at a different `rnnoise.dll` via the
card's **Load…** button; the loader validates the exports before enabling the stage.

To rebuild the DLL: clone the repo, run `download_model.sh` to fetch the pretrained model,
then compile the scalar sources (`denoise, rnn, pitch, kiss_fft, celt_lpc, nnet,
nnet_default, parse_lpcnet_weights, rnnoise_data, rnnoise_tables`) into a DLL with
`RNNOISE_BUILD` + `DLL_EXPORT` defined.

## Notes / roadmap

- Capture and render run on two independent device clocks; a 500 ms input buffer plus
  overflow discard absorbs the drift. Fine for voice; not sample-accurate.
- Ideas: per-band EQ type selection in the UI, A/B bypass, VST3 hosting,
  exclusive-mode low-latency path.

## License

[PolyForm Noncommercial 1.0.0](LICENSE.md) — free for noncommercial use.
