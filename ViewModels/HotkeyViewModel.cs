using System;
using System.Collections.Generic;
using System.Windows.Input;

namespace MicForge.ViewModels;

/// <summary>One assignable action and its (optional) global hotkey combo.</summary>
public sealed class HotkeyViewModel : ViewModelBase
{
    public string ActionId { get; }
    public string Label { get; }
    public Action Invoke { get; }

    public uint Modifiers { get; private set; }
    public uint Vk { get; private set; }

    private bool _capturing;
    public bool Capturing
    {
        get => _capturing;
        set { if (Set(ref _capturing, value)) OnPropertyChanged(nameof(SetText)); }
    }

    public string SetText => Capturing ? "Press keys…" : "Set";
    public string Display => Format(Modifiers, Vk);

    public HotkeyViewModel(string actionId, string label, Action invoke, uint modifiers, uint vk)
    {
        ActionId = actionId;
        Label = label;
        Invoke = invoke;
        Modifiers = modifiers;
        Vk = vk;
    }

    public void Assign(uint modifiers, uint vk)
    {
        Modifiers = modifiers;
        Vk = vk;
        Capturing = false;
        OnPropertyChanged(nameof(Display));
    }

    public void Clear()
    {
        Modifiers = 0;
        Vk = 0;
        Capturing = false;
        OnPropertyChanged(nameof(Display));
        OnPropertyChanged(nameof(SetText));
    }

    public static string Format(uint mods, uint vk)
    {
        if (vk == 0) return "Not set";
        var parts = new List<string>();
        if ((mods & GlobalHotkeys.ModControl) != 0) parts.Add("Ctrl");
        if ((mods & GlobalHotkeys.ModAlt) != 0) parts.Add("Alt");
        if ((mods & GlobalHotkeys.ModShift) != 0) parts.Add("Shift");
        if ((mods & GlobalHotkeys.ModWin) != 0) parts.Add("Win");
        parts.Add(KeyName(vk));
        return string.Join(" + ", parts);
    }

    private static string KeyName(uint vk)
    {
        var key = KeyInterop.KeyFromVirtualKey((int)vk);
        string s = key.ToString();
        if (s.Length == 2 && s[0] == 'D' && char.IsDigit(s[1])) return s.Substring(1);
        return s;
    }
}
