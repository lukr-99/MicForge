# MicForge — Architecture

MicForge is a real-time microphone processor for Windows. It captures from a WASAPI input,
runs a configurable DSP chain (gate, EQ, compressor, de-esser, character effects, …), and
renders the shaped signal to an output device — normally the VB-CABLE virtual mic, so any
app (Discord/OBS/games) hears the processed voice.

- **Stack:** C# / .NET 10 (`net10.0-windows`), WPF (MVVM) for the UI, WinForms only for the
  tray `NotifyIcon`. Single third-party dependency: **NAudio 2.2.1** (WASAPI capture/render).
  All DSP is hand-written — no VST/plugin host.
- **Internal audio format:** mono, 32-bit float, 48 kHz.

## Project layout

Folders group code by responsibility. Namespaces are intentionally *flat per top-level area*
(not one-per-folder), so a file can move between sub-folders without churning `using`
directives — the sub-folders are organizational, the namespaces are stable.

```
Audio/                     namespace MicForge.Audio
  Core/                    the engine-agnostic DSP primitives
    IAudioProcessor.cs       the stage contract (Strategy interface)
    Biquad.cs                RBJ biquad filter (+ FilterType enum)
    Fft.cs                   radix-2 FFT for the analyzers
    DspChain.cs              the ordered, reorderable pipeline + metering taps
    DspSampleProvider.cs     ISampleProvider that pulls the source and runs the chain
  Processors/             one IAudioProcessor per file (the "stages")
    GainStage, HighPassStage, HumRemover, DePlosive, NoiseSuppressor, NoiseGate,
    Expander, DeClicker, KeystrokeSuppressor, DeReverb, EchoRemover, ParametricEq,
    Compressor, MultibandCompressor, DeEsser, Saturation, Exciter, VoiceChanger,
    ComfortNoise, Limiter, InputAgc, LoudnessProcessor
  Engine/                device I/O and playback plumbing
    AudioEngine.cs           WASAPI capture → mono48k → chain → render, auto-reconnect, watchdog
    DeviceInfo.cs            lightweight device POCO for the UI (no live COM in bindings)
    DefaultDeviceWatcher.cs  IMMNotificationClient → follow the Windows default mic
    TeeSampleProvider.cs     taps the processed signal for headphone monitoring
    PreviewPlayer.cs         plays a sample voice through the chain (Crafting preview)
  Presets/
    BuiltInPresets.cs        full-chain voice presets (Broadcast, Podcast, Gaming, …)
    EqPresets.cs             quick EQ-curve presets (Bass Boost, Vocal, Loudness, …)
    VoiceSample.cs           preview-voice library + synthesised fallback
    PreviewSample.cs         one selectable preview voice (POCO)

Controls/                  namespace MicForge.Controls
    custom FrameworkElement renderers: EqGraph, CompCurve, LevelMeter, ThresholdMeter,
    GrHistory, Spectrogram, and MasonryPanel (column-balancing layout)

ViewModels/                namespace MicForge.ViewModels
    MainViewModel.cs         the app's central VM (pages, stages, crafting, presets, hotkeys…)
    StageViewModel.cs        one processing-stage card (+ EqStageViewModel / CompressorStageViewModel)
    ParamViewModel.cs        one labelled slider bound to a get/set
    CraftCard.cs / CraftCardConfig.cs / CraftCatalog.cs   the Crafting macro cards
    HotkeyViewModel.cs, RelayCommand.cs, ViewModelBase.cs

Converters/                namespace MicForge.Converters   (one IValueConverter per file)
Models/                    namespace MicForge              serializable data (Settings, PresetItem, …)
Services/                  namespace MicForge              Log, StartupManager, GlobalHotkeys,
                                                           KeyboardHook, IconFactory, PresetLibrary
Views/                     namespace MicForge              MainWindow (XAML), OsdWindow (mute overlay)
Themes/Dark.xaml           the dark theme + styles + converter resources
App.xaml(.cs)              application entry, single-instance guard, crash logging
```

## Signal flow & threading

```
mic ─▶ WasapiCapture ─▶ BufferedWaveProvider ─▶ (stereo→mono, resample→48k)
      ─▶ DspSampleProvider(DspChain) ─▶ (resample/channel-map) ─▶ WasapiOut ─▶ virtual mic
                              └─▶ TeeSampleProvider ─▶ WasapiOut ─▶ headphones (monitor)
```

`DspChain.Process(buffer, offset, count)` runs each `IAudioProcessor` in the current order,
in place, on the **audio render thread**. It also fills lock-guarded ring buffers for the
spectrum analyzers and updates volatile peak/`DspLoad` fields.

The **UI thread** never touches the audio buffers. It reads the volatile meter fields on a
`DispatcherTimer` and writes processor *parameters* (plain property sets) live. Parameter
writes are single primitive assignments, so no locking is needed between the two threads.

## Design patterns

- **Strategy / pipeline** — every stage implements `IAudioProcessor`; `DspChain` holds an
  ordered `IAudioProcessor[]` and can be reordered at runtime (drag-and-drop). Adding an
  effect never touches the chain's loop.
- **MVVM** — Views bind to ViewModels; `ViewModelBase` implements `INotifyPropertyChanged`
  (Observer). No code-behind logic beyond window/tray plumbing.
- **Command** — `RelayCommand` (`ICommand`) wires buttons/hotkeys to VM methods.
- **Template method** — `StageViewModel` is the base card; `EqStageViewModel` /
  `CompressorStageViewModel` specialise it with graph data.
- **Catalog / library** — `CraftCatalog`, `PresetLibrary`, `VoiceSample`, `BuiltInPresets`,
  `EqPresets` each own a set of definitions loaded from disk or code.
- **Factory** — `IconFactory` draws tray/window icons at runtime.
- **Macro layer** — Crafting cards are additive deltas summed onto the EQ + Voice Changer +
  Saturation + Exciter + high-pass/low-pass stages (`MainViewModel.ApplyCrafting`).

## Persistence & user data

All user state lives under `%AppData%\MicForge\` so it survives uninstall/reinstall:

| File | Contents |
|------|----------|
| `micforge.json` | the full `Settings` snapshot (chain params, device ids, stage order, hotkeys, crafting card states, last preset) |
| `craftcards.json` (+ `.version`) | Crafting card definitions; built-ins refresh when the catalog version bumps |
| `presets\*.json` | user presets, auto-loaded into the dropdown |
| `samples\*.wav` | user preview voices |
| `logs\micforge.log` | rolling log + crash capture |

## Adding a new DSP stage

1. Add `Audio/Processors/MyStage.cs` implementing `IAudioProcessor` (`Name`, `Enabled`,
   `Process`, `Reset`).
2. In `Audio/Core/DspChain.cs`: add a property, construct it, and place it in the `_chain`
   array at its default position.
3. In `Models/Settings.cs`: add persisted fields and wire them in `CaptureFrom` / `ApplyTo`.
4. In `ViewModels/MainViewModel.BuildStages`: add a `StageViewModel` card (info + params) and
   include the processor in the `procs` array (same order as the cards are added).

Existing saved chain orders automatically slot the new stage near its default index.
