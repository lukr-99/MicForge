using System;
using System.Runtime.InteropServices;

namespace MicForge;

/// <summary>
/// Low-level keyboard hook (WH_KEYBOARD_LL) that raises key-down/up for a single key,
/// used for push-to-talk / push-to-mute (RegisterHotKey can't report key release).
/// Installed only while push-to-talk is enabled.
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    private const int WhKeyboardLL = 13;
    private const int WmKeyDown = 0x100, WmKeyUp = 0x101, WmSysKeyDown = 0x104, WmSysKeyUp = 0x105;

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")] private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Auto)] private static extern IntPtr GetModuleHandle(string name);

    private readonly HookProc _proc;   // keep the delegate alive
    private IntPtr _hook;

    public event Action<uint> KeyDown;
    public event Action<uint> KeyUp;

    public KeyboardHook()
    {
        _proc = HookCallback;
        _hook = SetWindowsHookEx(WhKeyboardLL, _proc, GetModuleHandle(null), 0);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            uint vk = (uint)Marshal.ReadInt32(lParam);   // KBDLLHOOKSTRUCT.vkCode
            if (msg == WmKeyDown || msg == WmSysKeyDown) KeyDown?.Invoke(vk);
            else if (msg == WmKeyUp || msg == WmSysKeyUp) KeyUp?.Invoke(vk);
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero) { UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
    }
}
