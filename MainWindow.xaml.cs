using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using MicForge.ViewModels;
using WinForms = System.Windows.Forms;

namespace MicForge;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private WinForms.NotifyIcon _tray;
    private WinForms.ToolStripMenuItem _startupItem;
    private bool _exiting;
    private bool _shownBalloon;
    private GlobalHotkeys _hotkeys;
    private WinForms.ToolStripMenuItem _muteItem, _bypassItem;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel();
        DataContext = _vm;

        _vm.ExitRequested += ExitApp;
        _vm.ShowRequested += ShowFromTray;
        _vm.HotkeysChanged += RegisterHotkeys;

        SetupTray();
        try { Icon = IconFactory.CreateImageSource(); } catch { }

        Loaded += OnLoaded;
        StateChanged += OnStateChanged;
        Closing += OnClosing;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        try
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            int on = 1;
            // DWMWA_USE_IMMERSIVE_DARK_MODE = 20 (older builds: 19)
            if (DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, 19, ref on, sizeof(int));
        }
        catch { }

        try
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(handle)?.AddHook(WndProc);
            _hotkeys = new GlobalHotkeys(handle);
            RegisterHotkeys();
        }
        catch { }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_hotkeys != null && _hotkeys.TryHandle(msg, wParam)) handled = true;
        return IntPtr.Zero;
    }

    private void RegisterHotkeys()
    {
        if (_hotkeys == null) return;
        _hotkeys.UnregisterAll();
        if (!_vm.GlobalHotkeysEnabled) return;

        uint mod = GlobalHotkeys.ModControl | GlobalHotkeys.ModAlt;
        _hotkeys.Register(mod, 0x4D, () => _vm.ToggleMuteCommand.Execute(null));    // Ctrl+Alt+M
        _hotkeys.Register(mod, 0x42, () => _vm.ToggleBypassCommand.Execute(null));  // Ctrl+Alt+B
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_vm.AutoStartProcessing && !_vm.IsRunning)
            _vm.StartStopCommand.Execute(null);

        if (App.StartHidden)
            HideToTray(showBalloon: false);
    }

    private void OnStateChanged(object sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && _vm.MinimizeToTray)
            HideToTray(showBalloon: true);
    }

    private void OnClosing(object sender, CancelEventArgs e)
    {
        if (!_exiting && _vm.MinimizeToTray)
        {
            e.Cancel = true;
            HideToTray(showBalloon: true);
            return;
        }
        _vm.Shutdown();
        _tray?.Dispose();
    }

    private void ExitApp()
    {
        _exiting = true;
        _hotkeys?.Dispose();
        _vm.Shutdown();
        _tray?.Dispose();
        Application.Current.Shutdown();
    }

    private void HideToTray(bool showBalloon)
    {
        Hide();
        if (_tray != null) _tray.Visible = true;
        if (showBalloon && !_shownBalloon && _tray != null)
        {
            _tray.ShowBalloonTip(1500, "MicForge", "Still running — click the tray icon to reopen.",
                WinForms.ToolTipIcon.None);
            _shownBalloon = true;
        }
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
    }

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
}
