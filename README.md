# MicForge

MicForge is a real-time microphone processor for Windows. It reads your mic, runs it through a
configurable audio chain, and sends the cleaned-up result to a virtual microphone. Any app that
records a mic, such as Discord, OBS, a game, or a browser call, can then use the processed voice.

## Status

- Version 1.10.1, released 2026-10-13.
- The main branch has an unreleased About page in Settings. See [CHANGELOG.md](CHANGELOG.md).

## Features

- A 23-stage audio chain. Each stage can be turned on or off, tuned while audio runs, and moved
  by dragging its card.
- Crafting tab: stack voice effects such as Deep, Bright, Radio, and Robot, each with an intensity
  slider. Card definitions are JSON. A preview voice lets you hear the result.
- Meters tab: live spectrogram, signal-flow diagram, and loudness readings (momentary,
  short-term, integrated LUFS, loudness range, and true peak).
- Presets: built-in voice profiles, plus your own preset folder that loads automatically.
- Reliability: reconnects when a device drops, follows the default mic, has an audio watchdog,
  writes a crash log, and allows one running instance.
- Daily use: global hotkeys, push-to-talk, push-to-mute, a mute overlay, a tray icon, and an
  optional start with Windows.

## Signal chain

```text
Input gain > Auto gain > High-pass > Hum remover > De-plosive
  > Noise suppression (RNNoise) > Gate > Expander > De-click
  > Keystroke suppressor > De-reverb > Echo remover > Equalizer
  > Compressor > Multiband > De-esser > Saturation > Exciter
  > Voice changer > Comfort noise > Limiter > Output gain > Loudness > virtual mic
```

This is the default order. Drag the cards to change it. The chain runs internally as mono,
32-bit float, 48 kHz audio.

## Requirements

- Windows 10 or 11, 64-bit.
- [VB-CABLE](https://vb-audio.com/Cable/), a free virtual audio device. MicForge sends audio to
  "CABLE Input", and the app you use picks "CABLE Output" as its microphone.
- To run the installer, nothing else is needed. The installer includes the .NET runtime.
- To build from source, the .NET 10 SDK.

## Quick start

1. Install VB-CABLE and restart Windows.
2. Start MicForge.
3. Set Input to your real mic. Output should be "CABLE Input (VB-Audio Virtual Cable)". MicForge
   selects it when it finds it.
4. Click Start, speak, and adjust. A preset is a good starting point.
5. In Discord, OBS, or another app, set the microphone to "CABLE Output".

To hear yourself while you tune, turn on Monitoring in Settings and pick your headphones. Expect
some delay. The two-device WASAPI path adds it.

## Build and test

Run these from the repository root. You need the .NET 10 SDK.

```powershell
dotnet restore MicForge.csproj
dotnet build -c Release
dotnet run -c Release
dotnet test tests/MicForge.Tests/MicForge.Tests.csproj -c Release
```

Restore needs access to the private DotNetLib package feed on GitHub Packages. Set the
`NuGetPackageSourceCredentials_dotnetlib` environment variable, or add the credentials to your
user NuGet configuration. Do not put tokens in this repository.

Formatting check, as CI runs it:

```powershell
dotnet format MicForge.csproj --verify-no-changes
dotnet format tests/MicForge.Tests/MicForge.Tests.csproj --verify-no-changes
```

## Architecture

The code layout, the audio engine, and the DSP stages are described in
[ARCHITECTURE.md](ARCHITECTURE.md). Planned work is listed in [ROADMAP.md](ROADMAP.md). Those
items are not built yet.

## Data safety

All data lives under `%AppData%\MicForge\`, so it survives an uninstall and reinstall:

- `micforge.json`: your setup, including chain settings, devices, stage order, hotkeys, theme,
  and the last preset.
- `craftcards.json`: Crafting card definitions. You can edit them. Built-in cards refresh on update.
- `presets\*.json`: your saved presets.
- `samples\*.wav`: your own Crafting preview voices.
- `logs\micforge.log`: a rolling log and crash capture.

The app checks GitHub for a newer release when it starts and when you check in Settings.
Back up `%AppData%\MicForge\` before you install a new version. See
[docs/RELEASING.md](docs/RELEASING.md) for the update process.

## Delivery

Releases are built with [installer/build-installer.ps1](installer/build-installer.ps1). It
publishes a self-contained win-x64 build and compiles the Inno Setup script
[installer/MicForge.iss](installer/MicForge.iss) into `MicForge-Setup-<version>.exe`. The
installer is a per-user install and does not need admin rights. The steps for publishing a release
are in [docs/RELEASING.md](docs/RELEASING.md).

## Notes

- Capture and output run on two separate device clocks. A 500 ms input buffer and overflow
  discard absorb the drift. This works well for voice, but the timing is not sample-accurate.
- The Echo Remover reduces slap-back and speaker bleed of your own voice. It uses one channel and
  no far-end reference, so it is not full acoustic echo cancellation.
- The RNNoise noise suppressor is a 64-bit `rnnoise.dll` built from the xiph/rnnoise source. It
  sits in [native/](native/), and its BSD license is in
  [native/RNNOISE-LICENSE.txt](native/RNNOISE-LICENSE.txt). You can load a different `rnnoise.dll`
  with the card's Load button. The app checks its exports before it enables the stage.
- The bundled Crafting preview voice is a CC BY-NC excerpt. See
  [assets/VOICE-SAMPLE-LICENSE.txt](assets/VOICE-SAMPLE-LICENSE.txt) for attribution.

## License

[PolyForm Noncommercial 1.0.0](LICENSE.md). You may use it for noncommercial purposes.
