using System.ComponentModel;
using DotNetLib.Tray;
using MicForge.ViewModels;

namespace MicForge;

/// <summary>Adapts processing commands and state to the shared tray host.</summary>
public partial class MainWindow
{
    private TrayIconHost _tray;
    private TrayMenuState _trayState;

    /// <summary>Everything the tray icon, tooltip and menu show. Rebuild only when it changes.</summary>
    private sealed record TrayMenuState(
        bool Muted, bool Bypassed, bool Processing, bool Running, bool Reconnecting,
        bool StartWithWindows, string ThemeMode);

    private void SetupTray()
    {
        _tray = new TrayIconHost("MicForge", leftClick: ShowFromTray);
        UpdateTrayIcon();
    }

    private void OnVmPropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.Muted) or nameof(MainViewModel.Bypassed)
            or nameof(MainViewModel.IsProcessing) or nameof(MainViewModel.IsRunning)
            or nameof(MainViewModel.IsReconnecting) or nameof(MainViewModel.StartWithWindows)
            or nameof(MainViewModel.ThemeMode))
            UpdateTrayIcon();
    }

    /// <param name="force">Rebuild even when the state is the same, for example after a theme switch.</param>
    private void UpdateTrayIcon(bool force = false)
    {
        if (_tray == null) return;
        var key = new TrayMenuState(_vm.Muted, _vm.Bypassed, _vm.IsProcessing, _vm.IsRunning,
            _vm.IsReconnecting, _vm.StartWithWindows, _vm.ThemeMode);
        if (!force && key == _trayState) return;
        _trayState = key;
        var state = new TrayStatus(_vm.Muted, _vm.IsProcessing, _vm.IsReconnecting);
        _tray.SetIcon(IconFactory.CreateTrayIcon(_vm.Muted ? IconFactory.TrayState.Muted
            : _vm.IsProcessing ? IconFactory.TrayState.Live : IconFactory.TrayState.Stopped));
        _tray.SetToolTip(state.ToolTip);
        var menu = new TrayMenuBuilder();
        menu.Label(state.ToolTip);
        menu.Separator();
        menu.Item("Open MicForge", ShowFromTray, isDefault: true);
        menu.Item("Settings", () => { _vm.ShowSettingsCommand.Execute(null); ShowFromTray(); });
        menu.Item(_vm.IsRunning ? "Stop processing" : "Start processing", () => _vm.StartStopCommand.Execute(null));
        menu.Item("Mute", () => _vm.ToggleMuteCommand.Execute(null), isChecked: _vm.Muted);
        menu.Item("Bypass", () => _vm.ToggleBypassCommand.Execute(null), isChecked: _vm.Bypassed);
        menu.Separator();
        menu.Item("Start with Windows", () => _vm.StartWithWindows = !_vm.StartWithWindows,
            isChecked: _vm.StartWithWindows);
        menu.Submenu("Theme", themes =>
        {
            foreach (var option in _vm.ThemeOptions)
                themes.Item(option, () => _vm.ThemeMode = option, isChecked: _vm.ThemeMode == option);
        });
        menu.Separator();
        menu.Item("Quit MicForge", ExitApp);
        _tray.SetMenu(menu.Build());
    }
}
