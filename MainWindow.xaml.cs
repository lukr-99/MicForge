using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
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
    private OsdWindow _osd;
    private KeyboardHook _kbHook;
    private Point _dragStart;
    private StageViewModel _dragStage;
    private System.Collections.Generic.List<StageViewModel> _dragOriginalOrder;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel();
        DataContext = _vm;

        _vm.ExitRequested += ExitApp;
        _vm.ShowRequested += ShowFromTray;
        _vm.HotkeysChanged += RegisterHotkeys;
        _vm.MuteFlashRequested += OnMuteFlash;
        _vm.PttHookChanged += UpdatePttHook;
        _vm.PropertyChanged += OnVmPropertyChanged;

        _osd = new OsdWindow();

        SetupTray();
        UpdateTrayIcon();
        try { Icon = IconFactory.CreateImageSource(); } catch { }

        Loaded += OnLoaded;
        StateChanged += OnStateChanged;
        Closing += OnClosing;
        PreviewKeyDown += OnPreviewKeyDown;

        StagesList.PreviewMouseLeftButtonDown += StagesPreviewMouseDown;
        StagesList.PreviewMouseMove += StagesPreviewMouseMove;
        StagesList.DragOver += StagesDragOver;
        StagesList.Drop += StagesDrop;
    }

    // ---- drag & drop reordering of the processing chain ----
    private void StagesPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        var handle = FindTagged(e.OriginalSource as DependencyObject, "draghandle");
        _dragStage = (handle as FrameworkElement)?.DataContext as StageViewModel;
        if (_dragStage != null) _dragStart = e.GetPosition(null);
    }

    private void StagesPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStage == null || e.LeftButton != MouseButtonState.Pressed) return;
        var p = e.GetPosition(null);
        if (Math.Abs(p.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(p.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        var stage = _dragStage;
        _dragStage = null;
        _dragOriginalOrder = new System.Collections.Generic.List<StageViewModel>(_vm.Stages);
        _vm.SetDragging(stage, true);

        var effect = DragDrop.DoDragDrop(StagesList, new DataObject("MicForgeStage", stage), DragDropEffects.Move);

        _vm.SetDragging(stage, false);
        if (effect == DragDropEffects.Move) _vm.CommitOrder();
        else _vm.RestoreOrder(_dragOriginalOrder);
        _dragOriginalOrder = null;
    }

    private void StagesDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (e.Data.GetData("MicForgeStage") is not StageViewModel dragged)
        {
            e.Effects = DragDropEffects.None;
            return;
        }
        e.Effects = DragDropEffects.Move;

        // Live preview: reflow the cards as the cursor moves over a different one.
        var target = StageAt(e.GetPosition(StagesList));
        if (target != null && target != dragged) _vm.MoveStageLive(dragged, target);
    }

    private void StagesDrop(object sender, DragEventArgs e) => e.Handled = true;

    private StageViewModel StageAt(Point p)
    {
        var hit = StagesList.InputHitTest(p) as DependencyObject;
        while (hit != null)
        {
            if (hit is FrameworkElement fe && fe.DataContext is StageViewModel s && _vm.Stages.Contains(s))
                return s;
            hit = VisualTreeHelper.GetParent(hit);
        }
        return null;
    }

    private static DependencyObject FindTagged(DependencyObject d, string tag)
    {
        while (d != null)
        {
            if (d is FrameworkElement fe && (fe.Tag as string) == tag) return d;
            d = VisualTreeHelper.GetParent(d);
        }
        return null;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_vm.CapturingPtt)
        {
            Key pk = e.Key == Key.System ? e.SystemKey : e.Key;
            if (pk is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System)
                return;
            if (pk == Key.Escape) { _vm.CancelPttCapture(); e.Handled = true; return; }
            _vm.AssignPttKey((uint)KeyInterop.VirtualKeyFromKey(pk));
            e.Handled = true;
            return;
        }

        if (!_vm.IsCapturingHotkey) return;

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System)
            return;

        if (key == Key.Escape) { _vm.CancelCapture(); e.Handled = true; return; }

        var m = Keyboard.Modifiers;
        uint mods = 0;
        if ((m & ModifierKeys.Control) != 0) mods |= GlobalHotkeys.ModControl;
        if ((m & ModifierKeys.Alt) != 0) mods |= GlobalHotkeys.ModAlt;
        if ((m & ModifierKeys.Shift) != 0) mods |= GlobalHotkeys.ModShift;
        if ((m & ModifierKeys.Windows) != 0) mods |= GlobalHotkeys.ModWin;

        bool isFunc = key >= Key.F1 && key <= Key.F24;
        if (mods == 0 && !isFunc) return;   // require a modifier for ordinary keys

        _vm.AssignCapturedKey(mods, (uint)KeyInterop.VirtualKeyFromKey(key));
        e.Handled = true;
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

        UpdatePttHook();
    }

    private void UpdatePttHook()
    {
        if (_vm.PttEnabled && _kbHook == null)
        {
            _kbHook = new KeyboardHook();
            _kbHook.KeyDown += vk => Dispatcher.BeginInvoke(() => _vm.PttKeyEvent(vk, true));
            _kbHook.KeyUp += vk => Dispatcher.BeginInvoke(() => _vm.PttKeyEvent(vk, false));
        }
        else if (!_vm.PttEnabled && _kbHook != null)
        {
            _kbHook.Dispose();
            _kbHook = null;
        }
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

        foreach (var h in _vm.Hotkeys)
            if (h.Vk != 0) _hotkeys.Register(h.Modifiers, h.Vk, h.Invoke);
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

    private void OnMuteFlash(bool muted)
    {
        // Show the overlay when the user can't see the window (hotkey mute while elsewhere).
        if (_vm.ShowMuteOverlay && !IsActive) _osd?.Flash(muted);
    }

    private void ExitApp()
    {
        _exiting = true;
        _hotkeys?.Dispose();
        _kbHook?.Dispose();
        _osd?.Close();
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

    internal void ShowFromTray()
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

    private IconFactory.TrayState? _trayState;
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
