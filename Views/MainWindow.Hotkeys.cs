using System;
using System.Windows.Input;

namespace MicForge;

/// <summary>
/// Global hotkeys (dispatched from the window message hook) and the push-to-talk low-level
/// keyboard hook, plus the in-app key capture used to assign shortcuts and the PTT key.
/// </summary>
public partial class MainWindow
{
    private GlobalHotkeys _hotkeys;
    private KeyboardHook _kbHook;

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Capturing a push-to-talk key?
        if (_vm.CapturingPtt)
        {
            Key pk = e.Key == Key.System ? e.SystemKey : e.Key;
            if (IsModifierKey(pk)) return;
            if (pk == Key.Escape) { _vm.CancelPttCapture(); e.Handled = true; return; }
            _vm.AssignPttKey((uint)KeyInterop.VirtualKeyFromKey(pk));
            e.Handled = true;
            return;
        }

        // Capturing a global shortcut?
        if (!_vm.IsCapturingHotkey) return;

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (IsModifierKey(key)) return;
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

    private static bool IsModifierKey(Key k) =>
        k is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
          or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System;

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
}
