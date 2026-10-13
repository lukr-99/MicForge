# Changelog

## [Unreleased]

- Fix following the Windows default mic. The restart failed with "The selected audio device
  is no longer available" because reloading the device lists cleared the selected output.
- Follow a new default mic also while the engine is reconnecting, instead of reconnecting to
  the old mic.

## 1.10.0

- Run the capture and processing threads at Windows "Pro Audio" priority, so audio keeps up under
  CPU load. Buffer latency is unchanged.
- Rebuild the tray icon and menu only when what they show changes.
- Write each settings change once. "Saved" now shows the result of that write, and Start with
  Windows reports when its registry entry could not be changed.
- Show a message with the link when the release notes cannot be opened in the browser.
- Show at most one error dialog when errors repeat, instead of stacking them.
- Update Microsoft.Extensions.DependencyInjection to 10.0.12.
- Update NAudio to 3.1.0. Capture and output now use NAudio's new WASAPI recorder and player,
  which give their threads "Pro Audio" priority themselves. Latencies are unchanged. The
  voice preview also runs at that priority now and no longer allocates memory per sample.

## 1.9.0

- Move the tray icon, status menu and notifications to DotNetLib.Tray 0.4.1.
- Replace the Settings tab with the shared scrolling page, section navigation and row controls.
- Add System, Light and Dark themes with live semantic colors and Windows high-contrast support.
- Save settings changes immediately and show failed saves with a Retry action.
- Keep older settings and presets compatible, including the existing AppData locations.
