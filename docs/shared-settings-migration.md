# Shared tray and settings migration

Implemented on `feat/shared-tray-settings` for the two MicForge GoalMaker items:

- `61a22b01-7a49-41ec-9bcc-a6ce8781040f`: tray migration.
- `f84b0dd3-028f-4437-a7eb-cb790fd79a8c`: settings kit and CodePrint settings rule.

The app restores existing profiles before enabling immediate persistence. Old profiles without
a Theme field use Dark. Presets, device selections and user data remain in their existing folders.
The tray host receives multi-size icons made by IconFile from MicForge's drawing and owns its
copies. MainWindow unregisters its theme and property-change subscriptions on exit. SingleInstance
owns the per-user lock and the pipe that brings the running window forward on another launch.
An older running version is detected and receives its legacy show-window signal during an upgrade.

Validation: Release build and tests for settings compatibility, persisted theme round-trips,
tray status precedence and a real WPF settings window. The window check covers navigation,
two-way toggle binding, light and dark resource changes, a busy and a disabled row, and narrow
chip navigation. Local rendered previews go in the ignored artifacts folder.

The migration was reviewed against the tray/settings tasks before the 1.9.0 local update. The
isolated WPF test uses preview data; installed-app startup and live audio need their own smoke
check when deploying. Publishing a GitHub release is separate from a local install.
