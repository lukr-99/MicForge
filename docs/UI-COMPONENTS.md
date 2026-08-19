# MicForge — UI Components

A catalog of MicForge's UI building blocks, for when the UI is factored into a reusable,
themed component library. Three parts:

1. [Theming](#1-theming--palette) — how the dark theme is wired, and the palette.
2. [Custom controls](#2-custom-controls) — the hand-drawn `FrameworkElement` renderers.
3. [Styles & resources](#3-styles--resources) — the WPF styles and value converters.

Everything is **dependency-free** (pure WPF, one hand-rolled theme). All resources live in
[`Themes/Dark.xaml`](../Themes/Dark.xaml); custom controls live in [`Controls/`](../Controls);
converters in [`Converters/`](../Converters).

---

## 1. Theming & palette

`App.xaml` merges `Themes/Dark.xaml` into the application resources, so every `{StaticResource …}`
below is available app-wide. The theme also defines the **implicit** styles (bare `TargetType`
with no key) for `TextBlock`, `Button`, `ComboBox`, and `ScrollBar`, so plain controls already
look right; **keyed** styles are opt-in variants.

The whole design is built from **10 brushes**. To re-skin (e.g. a light theme or an accent
picker), swap these and everything follows.

| Resource key | Hex | Role |
|---|---|---|
| `WindowBrush` / `WindowColor` | `#16181C` | app background |
| `SurfaceBrush` | `#20242A` | cards / panels |
| `SurfaceAltBrush` | `#2A2F36` | insets, pills, popups |
| `BorderBrush` | `#343A42` | hairline borders |
| `TrackBrush` | `#3A414A` | slider/scrollbar tracks |
| `TextBrush` | `#E7E9EC` | primary text |
| `TextMutedBrush` | `#98A0A8` | secondary/labels |
| `AccentBrush` | `#2EC4B6` | teal — primary accent |
| `StartBrush` | `#2FB86B` | green — running/OK |
| `StopBrush` | `#E5543B` | red — stop/clip/mute |

> Note: several visual accents (stage cards, category colors, meter gradients) are currently
> hard-coded hex in code/XAML rather than pulled from these brushes — a good normalization task
> when extracting the library (see [ROADMAP.md](../ROADMAP.md)).

---

## 2. Custom controls

All are `sealed` renderers that override `OnRender` (immediate-mode drawing) rather than using
templates. Two data patterns are used:

- **Model-bound + self-animating** — the control takes a view-model via a `Model` dependency
  property, reads live data (EQ bands, spectrum ring buffers) from it, and repaints itself on an
  internal `DispatcherTimer` (~30–40 ms). Used by `EqGraph`, `CompCurve`, `Spectrogram`.
- **Value-pushed** — the host binds a scalar (`Level`, `Gr`, …) that the view-model updates each
  meter tick; the control repaints on property change. Used by `LevelMeter`, `ThresholdMeter`,
  `GrHistory`.

### EqGraph (`Controls/EqGraph.cs`) — interactive
Equalizer curve with a live spectrum analyzer behind it. Handles sit **on** the combined
response curve; drag = move the band (X frequency, Y gain), mouse-wheel = Q on bell bands,
right-click = band type / enable menu. A readout shows exact freq/gain/Q.

| DP | Type | Meaning |
|---|---|---|
| `Model` | `EqStageViewModel` | bands to draw/edit + `Chain` for the spectrum |

Set `IsHitTestVisible="False"` for a read-only preview (used on the Crafting tab).

### CompCurve (`Controls/CompCurve.cs`)
Compressor transfer curve (input dB → output dB) showing threshold/ratio/knee/makeup, with a
live dot at the current input level.

| DP | Type | Meaning |
|---|---|---|
| `Model` | `CompressorStageViewModel` | compressor params |
| `LevelDb` | `double` | current input level (dB) for the dot |

### Spectrogram (`Controls/Spectrogram.cs`)
Scrolling waterfall of the processed output — time left→right (newest on the right), log-frequency
vertical axis, level → colour ramp (dark→blue→teal→amber→red). Backed by a `WriteableBitmap`.

| DP | Type | Meaning |
|---|---|---|
| `Model` | `EqStageViewModel` | provides `Chain` (output-spectrum tap) + sample rate |

### LevelMeter (`Controls/LevelMeter.cs`)
Vertical audio meter, fills bottom-up green→yellow→red with a peak-hold tick.

| DP | Type | Meaning |
|---|---|---|
| `Level` | `double` | 0..1 (already mapped from dB) |
| `Clipping` | `bool` | shows a red clip cap |

> The property is `Clipping`, not `Clip`, to avoid colliding with `UIElement.Clip`.

### ThresholdMeter (`Controls/ThresholdMeter.cs`)
Horizontal level bar with a threshold marker; glows accent when active. Used for the gate
(level vs open threshold) and de-esser (sibilance vs threshold).

| DP | Type | Meaning |
|---|---|---|
| `Level` | `double` | 0..1 |
| `Threshold` | `double` | 0..1 marker position |
| `Active` | `bool` | accent glow when the stage is acting |

### GrHistory (`Controls/GrHistory.cs`)
Scrolling gain-reduction history. Push a **positive dB** value via `Gr` each UI tick; it draws a
right-to-left filled trace (more reduction = taller from the top). Used by the compressor, gate
and limiter.

| DP | Type | Meaning |
|---|---|---|
| `Gr` | `double` | gain reduction in dB (>= 0) pushed per tick |

### MasonryPanel (`Controls/MasonryPanel.cs`) — layout
Column-balancing panel: lays children into fixed-width columns, each child dropped into the
currently-shortest column, so short cards keep their natural height instead of stretching to the
tallest in a row.

| DP | Type | Meaning |
|---|---|---|
| `ColumnWidth` | `double` | fixed column width (default 314) |

---

## 3. Styles & resources

### Buttons & toggles
| Key | Target | Use |
|---|---|---|
| *(implicit)* | `Button` | default flat button |
| `PrimaryButton` | `Button` | accent-filled primary action (Start, Save…) |
| `IconButton` | `Button` | square icon button (e.g. refresh ⟳) |
| `NavButton` | `Button` | left-rail nav item; `Tag` = "is this page active" bool |
| `ToggleSwitch` | `CheckBox` | the pill on/off switch used on every stage card |
| `BypassToggle` | `ToggleButton` | header master-bypass |
| `MuteToggle` | `ToggleButton` | header mute |
| `InfoToggle` | `ToggleButton` | round ⓘ that opens an info popup |
| `LinkToggle` | `ToggleButton` | small text toggle (Crafting "technical peek") |
| `Chip` | `RadioButton` | filter chip; `Tag` = "#RRGGBB" tint (Crafting categories) |

### Inputs & containers
| Key | Target | Use |
|---|---|---|
| `Card` | `Border` | the rounded surface panel used everywhere |
| `Fader` | `Slider` | teal-thumb parameter fader |
| *(implicit)* | `ComboBox` + `ComboToggle` | dark dropdown |
| `FieldBadge` | `Border` | small "IN"/"OUT" label pill |

### Scrollbars
| Key | Target | Use |
|---|---|---|
| *(implicit)* | `ScrollBar` | slim dark scrollbar |
| `ScrollThumb` | `Thumb` | its thumb |
| `ScrollHidden` | `ScrollBar` | zero-width (content that shouldn't show a bar) |

### Value converters (`Converters/`, exposed as resources)
| Resource key | Class | Direction / use |
|---|---|---|
| `BoolToVis` | `BooleanToVisibilityConverter` (WPF) | bool → Visibility |
| `HexToBrush` | `HexToBrushConverter` | "#RRGGBB" → frozen brush (card accents, chips) |
| `EmptyToCollapsed` | `EmptyStringToCollapsedConverter` | hide when a string is empty |
| `AllTrueVis` | `AllTrueToVisibleConverter` | multi-bind: all true → Visible |
| `AllTrueCollapsed` | `AllTrueToCollapsedConverter` | multi-bind: all true → Collapsed |
| `StrEq` | `StringEqualsConverter` | value == parameter (drives single-select chips) |

---

## Notes for building the component library

- **Theme-swappable core.** Keep the 10 brushes as the single source of truth; migrate the
  hard-coded hex accents (stage/category colors, meter gradients) to named resources so a light
  theme / accent picker is a resource swap.
- **Controls are self-contained.** The renderers only need their `Model`/value inputs and paint
  themselves; they hold no app state. That makes them clean library candidates — the only
  coupling to lift out is that `EqGraph`/`CompCurve`/`Spectrogram` bind to *MicForge* view-models
  (`EqStageViewModel`, `CompressorStageViewModel`). For a general kit, introduce small interfaces
  (e.g. `IEqBandsSource`, `ISpectrumSource`) so they don't depend on the app's VMs.
- **Immediate-mode + timer** is the pattern for anything live (meters/scopes). Keep the timer
  gated on `IsVisible` (they already are) so hidden pages don't paint.
- **One style, one job.** The keyed styles above are small and composable; a library would
  expose them as a merged `ResourceDictionary` plus a `ThemeColors` object.
