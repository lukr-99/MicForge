# MicForge

A real-time microphone processor for Windows. It captures your mic, runs a deep, fully
configurable DSP chain, and outputs to a virtual audio device so **any** app (Discord, OBS,
games, browser, calls) uses the cleaned-up, shaped signal as its "microphone".

Written in **C# / .NET 10 / WPF** using [NAudio](https://github.com/naudio/NAudio) for WASAPI
capture and render. All DSP is **hand-written** (no VST/plugin host), and the dark UI is a
**hand-rolled theme** (no control-library dependency).

> See [ARCHITECTURE.md](ARCHITECTURE.md) for the code layout and design, and
> [ROADMAP.md](ROADMAP.md) for where it's going.

## Highlights

- **23-stage DSP chain**, every stage toggle-able, tunable live, and **drag-to-reorder**.
- **Crafting tab** — stack "voice character" cards (Deep, Bright, Radio, Megaphone, Robot…)
  with intensity sliders to design a voice with no technical knobs; JSON-configurable, with a
  built-in preview voice so you can hear it without talking.
- **Meters/analyzer tab** — live spectrogram, signal-flow diagram, and a full loudness suite
  (momentary / short-term / integrated LUFS, LRA, true-peak).
- **Presets** — built-in voice profiles + your own preset folder that auto-loads; plus quick
  EQ-curve presets on the equalizer.
- **Reliability** — device auto-reconnect, follow-the-default-mic, an audio watchdog, crash
  logging, and single-instance launch.
- **Daily use** — global hotkeys, push-to-talk / push-to-mute, a mute overlay, a live tray
  icon, and minimize/close to tray with an optional "Start with Windows".

## Signal chain

```
Input gain ─▶ Auto gain ─▶ High-pass ─▶ Hum remover ─▶ De-plosive
  ─▶ Noise suppression (RNNoise) ─▶ Gate ─▶ Expander ─▶ De-click
  ─▶ Keystroke suppressor ─▶ De-reverb ─▶ Echo remover ─▶ Equalizer
  ─▶ Compressor ─▶ Multiband ─▶ De-esser ─▶ Saturation ─▶ Exciter
  ─▶ Voice changer ─▶ Comfort noise ─▶ Limiter ─▶ Output gain ─▶ Loudness ─▶ virtual mic
```

The order above is the default — you can rearrange stages by dragging their cards. Everything
runs internally at **mono, 32-bit float, 48 kHz**.

## The tabs

- **Processor** — the chain as cards (Sliders ⇄ Graphs view). Interactive EQ curve + spectrum,
  compressor transfer curve, gate/de-esser/limiter meters, in/out level meters, clip
  indicators, mic-health readout.
- **Crafting** — character cards, filterable by category (Tone / Polish / Fun / FX), with a
  live preview voice and per-card info + "technical peek".
- **Meters** — spectrogram, signal-flow, loudness suite.
- **Shortcuts** — configure global hotkeys and push-to-talk.
- **Settings** — startup/tray, self-monitoring device, reliability & diagnostics, profile
  export/import, presets folder, undo/redo.

## Requirements

1. **Windows 10/11 (x64).**
2. **[VB-CABLE](https://vb-audio.com/Cable/)** (free) — creates the virtual mic. After
   installing, MicForge sends audio to **"CABLE Input"**, and in your app you pick
   **"CABLE Output"** as the microphone.

The installer is self-contained (bundles the .NET runtime), so no separate .NET install is
needed to *run* it. To *build*, you need the **.NET 10 SDK**.

## Usage

1. Install VB-CABLE and reboot.
2. Run MicForge.
3. **Input** = your real mic. **Output** = `CABLE Input (VB-Audio Virtual Cable)` (auto-selected
   if found).
4. Click **Start**, speak, and tune. Pick a preset to get going fast.
5. In Discord/OBS/etc., set the microphone to `CABLE Output`.

To monitor yourself while tuning, enable **Monitoring** in Settings and choose your headphones
(expect a little latency — normal for the two-device WASAPI path).

## RNNoise AI noise suppression

A 64-bit `rnnoise.dll` — built from the official [xiph/rnnoise](https://github.com/xiph/rnnoise)
source — is bundled in [`native/`](native/) and copied next to the exe on build, so the Noise
Suppression stage works out of the box (BSD license in
[`native/RNNOISE-LICENSE.txt`](native/RNNOISE-LICENSE.txt)). You can also point the app at a
different `rnnoise.dll` via the card's **Load…** button; the loader validates the exports
before enabling the stage.

## Where your data lives

Everything is under `%AppData%\MicForge\` so it survives uninstall/reinstall:

- `micforge.json` — your full setup (chain params, devices, stage order, hotkeys, crafting,
  last preset)
- `craftcards.json` — Crafting card definitions (editable; built-ins refresh on update)
- `presets\*.json` — your saved presets (auto-loaded into the dropdown)
- `samples\*.wav` — your own Crafting preview voices
- `logs\micforge.log` — rolling log + crash capture

## Building & packaging

```bash
# build / run
dotnet build -c Release
dotnet run   -c Release

# self-contained publish (what the installer bundles)
dotnet publish -c Release -r win-x64 --self-contained true

# installer (Inno Setup 6): installer/MicForge.iss  ->  MicForge-Setup-x.y.z.exe
```

The bundled preview voice clip is a CC BY-NC excerpt — see
[`assets/VOICE-SAMPLE-LICENSE.txt`](assets/VOICE-SAMPLE-LICENSE.txt) for attribution.

## Notes

- Capture and render run on two independent device clocks; a 500 ms input buffer plus overflow
  discard absorbs the drift. Great for voice; not sample-accurate.
- The Echo Remover targets slap-back / speaker-bleed echo of your *own* voice (single-channel,
  no far-end reference) — it is not full duplex acoustic echo cancellation.

## License

[PolyForm Noncommercial 1.0.0](LICENSE.md) — free for noncommercial use.
