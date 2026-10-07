using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using MicForge.ViewModels;

namespace MicForge;

/// <summary>
/// The main window. Code-behind is split into partials by concern — tray
/// (<c>MainWindow.Tray.cs</c>), chain drag-and-drop (<c>MainWindow.DragDrop.cs</c>) and global
/// hotkeys / push-to-talk (<c>MainWindow.Hotkeys.cs</c>). This file owns construction and the
/// window lifecycle (dark chrome, show/hide, close-to-tray, exit).
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly OsdWindow _osd;
    private DeckBridge _bridge;
    private bool _exiting;
    private bool _cleanedUp;
    private bool _shownBalloon;
    private readonly AppTheme _theme;

    public MainWindow(MainViewModel vm, AppTheme theme)
    {
        InitializeComponent();
        _vm = vm;
        _theme = theme;
        _theme.Applied += OnThemeApplied;
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

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Dark title bar (DWMWA_USE_IMMERSIVE_DARK_MODE = 20; older builds use 19).
        try
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            int on = _theme.IsDark ? 1 : 0;
            if (DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, 19, ref on, sizeof(int));
        }
        catch { }

        // Hook the window message loop for global hotkeys, then arm push-to-talk.
        try
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(handle)?.AddHook(WndProc);
            _hotkeys = new GlobalHotkeys(handle);
            RegisterHotkeys();
        }
        catch { }

        UpdatePttHook();

        // Loopback control endpoint for the Relay deck companion (best-effort; never fatal).
        try { _bridge = new DeckBridge(_vm); _bridge.Start(); }
        catch (Exception ex) { Log.Error("Deck bridge failed to start", ex); }
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
        CleanUp();
    }

    private void OnMuteFlash(bool muted)
    {
        // Show the overlay when the user can't see the window (hotkey mute while elsewhere).
        if (_vm.ShowMuteOverlay && !IsActive) _osd?.Flash(muted);
    }

    private void ExitApp()
    {
        _exiting = true;
        CleanUp();
        Application.Current.Shutdown();
    }

    private void CleanUp()
    {
        if (_cleanedUp) return;
        _cleanedUp = true;
        _bridge?.Dispose();
        _hotkeys?.Dispose();
        _kbHook?.Dispose();
        _osd?.Close();
        _vm.Shutdown();
        _theme.Applied -= OnThemeApplied;
        _vm.PropertyChanged -= OnVmPropertyChanged;
        _tray?.Dispose();
    }

    private void HideToTray(bool showBalloon)
    {
        Hide();

        if (showBalloon && !_shownBalloon && _tray != null)
        {
            _tray.Notify("MicForge", "Still running. Click the tray icon to reopen.");
            _shownBalloon = true;
        }
    }

    private void OnThemeApplied(object sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
        {
            int dark = _theme.IsDark ? 1 : 0;
            DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
        }
        UpdateTrayIcon();
    }

    internal void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
    }

    internal void OnLaunchedAgain()
    {
        ShowFromTray();
        _tray?.Notify("MicForge", "MicForge is already running. The existing window is open.");
    }
}
