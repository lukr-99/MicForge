# MicForge — Roadmap & Ideas

A living backlog of possible directions. Nothing here is committed; it's a menu. Each item is
tagged **impact / effort** (low・med・high). Grouped by theme, roughly ordered by bang-for-buck
within each group.

> Already shipped (for context): the 23-stage chain, drag-reorder, Crafting (JSON cards +
> categories + preview voice), Meters (spectrogram, signal-flow, LUFS/LRA/true-peak), presets +
> preset folder + EQ-curve presets, undo/redo, profile import/export, global hotkeys + PTT,
> mute overlay, live tray icon, device auto-reconnect + follow-default + watchdog, crash logging,
> single-instance, Echo Remover, Keystroke Suppressor.

## 🔊 Audio / DSP

- **Auto-updater** (check GitHub Releases, one-click update). `high / med` — the obvious next
  quality-of-life win now that it ships as an installed app.
- **Dynamic EQ** — per-band compression (a band that only ducks when it spikes). `high / high`
- **Sidechain ducking** — duck music/game audio under your voice (needs a 2nd capture). `high / high`
- **Adaptive echo canceller (true AEC)** — use the render/loopback signal as the far-end
  reference; partitioned block-frequency-domain NLMS. `high / very high`
- **True acoustic keystroke model** — spectral template matching instead of a broadband duck,
  for cleaner key-click removal during speech. `med / high`
- **Formant-preserving pitch shift** (PSOLA / phase-vocoder) so the Voice Changer keeps natural
  timbre instead of chipmunk/robot artifacts. `high / high`
- **De-hum auto-detect** — find the mains frequency + harmonics from the noise floor. `med / med`
- **Transient shaper** — attack/sustain control for punch/softness. `med / med`
- **Stereo/ambisonic capture** support (currently mono-only). `low / high`
- **Sample-accurate single-device path** (exclusive mode, one clock) for ultra-low latency. `med / high`
- **Sidechain/keyed gate from a second mic** (e.g. a noise-reference mic). `med / high`
- **Look-ahead on the gate/expander** to stop clipped word starts. `med / med`
- **Per-stage wet/dry mix** on every processor (parallel processing). `med / med`

## 🤖 AI / ML

- **On-device ML denoise** beyond RNNoise (e.g. DeepFilterNet / NSNet2 ONNX) with a quality
  slider. `high / high`
- **ML de-reverb** and **ML echo cancellation** models. `high / very high`
- **Voice conversion / cloning** (real target-voice, not just pitch/EQ). `high / very high`
- **Auto-EQ "match"** — analyze your voice and match a target/reference curve. `high / med`
- **Smart auto-setup** — record 10 s, auto-pick gate threshold, EQ, de-ess, gain. `high / med`
- **Speech/keyboard/breath classifier** to gate/duck by *content*, not level. `high / high`

## 📈 Metering / Visualization

- **Correlation / phase meter** and **stereo scope** (if stereo lands). `low / med`
- **Per-stage inline GR/level lights** on the signal-flow diagram (live). `med / med`
- **Waveform / oscilloscope** view of in vs out. `low / med`
- **A/B compare** — snapshot two chains and toggle to hear the difference. `high / med`
- **Before/after monitor** — hear raw vs processed on a keybind. `high / low`
- **Loudness history graph** + target guide-lines. `low / low`

## 🎨 Crafting

- **Card editor UI** — build/tune cards visually instead of editing JSON. `high / med`
- **Card randomizer / "surprise me"** and **save the current mix as a new card**. `med / low`
- **More dimensions** — comfort-noise, de-reverb, transient, per-band Q as craftable deltas. `med / med`
- **Community card packs** — import/export card sets. `med / low`
- **Morph slider** between two crafted voices. `med / med`

## 🎛️ Presets & Profiles

- **Per-app auto-profiles** — detect the foreground app and switch profile (Discord vs OBS). `high / high`
- **Cloud sync** of presets/profiles (optional). `med / high`
- **Preset thumbnails** (the EQ curve as an icon) in the dropdown. `low / med`
- **A/B two presets** live. `med / low`

## 🖥️ UX / UI

- **UI component library / design pass** (see [`docs/UI-COMPONENTS.md`](docs/UI-COMPONENTS.md)) —
  factor the controls + theme into a reusable, documented kit. `med / med`
- **Light theme** + accent-color picker. `low / med`
- **Compact / mini mode** — a tiny always-on-top strip with meter + mute. `med / med`
- **First-run wizard** — VB-CABLE check, device pick, calibrate. `high / med`
- **In-app VB-CABLE detection + guided install** when missing. `high / med`
- **Resizable/collapsible cards** and remembered window layout. `low / med`
- **Search/command palette** for stages & settings. `low / med`
- **Localization** (i18n). `low / high`
- **Accessibility** pass (screen-reader labels, keyboard nav). `med / med`

## 🔌 Integrations

- **Stream Deck / MIDI / OSC** control of mute/bypass/preset/gain. `high / med`
- **Native virtual mic driver** (drop the VB-CABLE dependency). `high / very high`
- **Multiple simultaneous outputs** (e.g. cable + headphones + a second cable). `med / med`
- **Local control API** (WebSocket) for OBS/automation. `med / med`
- **VST3 hosting** — load third-party plugins into the chain. `high / very high`
- **Discord / OBS plugin** companions. `med / high`

## 📦 Distribution / Platform

- **Auto-update** (also listed under DSP-adjacent QoL). `high / med`
- **Code signing** to kill the SmartScreen warning. `med / low ($ cert)`
- **winget package** (`winget install MicForge`). `med / med`
- **Portable (no-install) build.** `low / low`
- **CI** (GitHub Actions) to build + publish the installer on tag. `med / med`
- **macOS/Linux** exploration (huge — WASAPI/WPF are Windows-only). `low / very high`

## 🛠️ Reliability / Diagnostics

- **Latency readout** (measured round-trip) + a low-latency guidance panel. `med / med`
- **Xrun/glitch timeline** and CPU-per-stage breakdown. `med / med`
- **Safe-mode / reset** if a bad config crashes on load. `med / low`
- **Log viewer** in-app + "export diagnostics" bundle. `low / low`

## 🧪 Testing / Dev

- **Unit tests** for the DSP (impulse/step responses, gain math, gate/comp curves). `high / med`
- **Offline render mode** — process a WAV through the chain from the CLI (great for testing). `med / med`
- **Benchmark harness** for DspLoad per stage. `low / med`
- **Golden-file audio regression tests.** `med / med`
