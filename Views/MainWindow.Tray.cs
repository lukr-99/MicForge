using MicForge.ViewModels;
using WinForms = System.Windows.Forms;

namespace MicForge;

/// <summary>Tray icon + context menu, and keeping the icon/tooltip in sync with run/mute state.</summary>
public partial class MainWindow
{
    private WinForms.NotifyIcon _tray;
    private WinForms.ToolStripMenuItem _startupItem;
    private WinForms.ToolStripMenuItem _muteItem, _bypassItem;
    private IconFactory.TrayState? _trayState;

    private void SetupTray()
    {
        _tray = new WinForms.NotifyIcon
        {
            Icon = IconFactory.CreateIcon(),
            Text = "MicForge",
            Visible = true
        };
        _tray.DoubleClick += (_, _) => ShowFromTray();

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Open MicForge", null, (_, _) => ShowFromTray());
        menu.Items.Add("Start / Stop", null, (_, _) => _vm.StartStopCommand.Execute(null));

        _muteItem = new WinForms.ToolStripMenuItem("Mute", null, (_, _) => _vm.ToggleMuteCommand.Execute(null));
        _bypassItem = new WinForms.ToolStripMenuItem("Bypass", null, (_, _) => _vm.ToggleBypassCommand.Execute(null));
        menu.Items.Add(_muteItem);
        menu.Items.Add(_bypassItem);
        menu.Opening += (_, _) => { _muteItem.Checked = _vm.Muted; _bypassItem.Checked = _vm.Bypassed; };

        menu.Items.Add(new WinForms.ToolStripSeparator());

        _startupItem = new WinForms.ToolStripMenuItem("Start with Windows")
        {
            Checked = _vm.StartWithWindows,
            CheckOnClick = true
        };
        _startupItem.Click += (_, _) => _vm.StartWithWindows = _startupItem.Checked;
        menu.Items.Add(_startupItem);

        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());

        _tray.ContextMenuStrip = menu;
    }

    private void OnVmPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.Muted) or nameof(MainViewModel.IsProcessing)
            or nameof(MainViewModel.IsRunning) or nameof(MainViewModel.IsReconnecting))
            UpdateTrayIcon();
    }

    private void UpdateTrayIcon()
    {
        if (_tray == null) return;
        var st = _vm.Muted ? IconFactory.TrayState.Muted
               : _vm.IsProcessing ? IconFactory.TrayState.Live
               : IconFactory.TrayState.Stopped;
        if (st == _trayState) return;
        _trayState = st;

        var old = _tray.Icon;
        try { _tray.Icon = IconFactory.CreateIcon(st); } catch { }
        old?.Dispose();
        _tray.Text = st switch
        {
            IconFactory.TrayState.Muted => "MicForge — muted",
            IconFactory.TrayState.Live => "MicForge — live",
            _ => "MicForge"
        };
    }
}
