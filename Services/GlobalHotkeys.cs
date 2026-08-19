using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace MicForge;

/// <summary>Registers system-wide hotkeys (RegisterHotKey) and dispatches WM_HOTKEY.</summary>
public sealed class GlobalHotkeys : IDisposable
{
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int WmHotkey = 0x0312;
    public const uint ModAlt = 0x1, ModControl = 0x2, ModShift = 0x4, ModWin = 0x8, ModNoRepeat = 0x4000;

    private readonly IntPtr _hwnd;
    private readonly Dictionary<int, Action> _actions = new();
    private int _nextId = 1;

    public GlobalHotkeys(IntPtr hwnd) => _hwnd = hwnd;

    /// <summary>Register a hotkey; returns false if the combo is already taken by another app.</summary>
    public bool Register(uint modifiers, uint vk, Action action)
    {
        int id = _nextId++;
        if (!RegisterHotKey(_hwnd, id, modifiers | ModNoRepeat, vk)) return false;
        _actions[id] = action;
        return true;
    }

    /// <summary>Handle a window message; returns true if it was a registered hotkey.</summary>
    public bool TryHandle(int msg, IntPtr wParam)
    {
        if (msg != WmHotkey) return false;
        if (_actions.TryGetValue(wParam.ToInt32(), out var action)) { action(); return true; }
        return false;
    }

    public void UnregisterAll()
    {
        foreach (var id in _actions.Keys) UnregisterHotKey(_hwnd, id);
        _actions.Clear();
        _nextId = 1;
    }

    public void Dispose() => UnregisterAll();
}
