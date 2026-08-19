using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using MicForge.Audio;
using NAudio.CoreAudioApi;

namespace MicForge.ViewModels;

/// <summary>Global hotkeys and push-to-talk / push-to-mute.</summary>
public sealed partial class MainViewModel
{
    // ---- hotkeys ----
    private void BuildHotkeys(Settings saved)
    {
        Hotkeys.Clear();
        Hotkeys.Add(new HotkeyViewModel("mute", "Mute", () => Muted = !Muted,
            GlobalHotkeys.ModControl | GlobalHotkeys.ModAlt, 0x4D));       // Ctrl+Alt+M
        Hotkeys.Add(new HotkeyViewModel("bypass", "Bypass", () => Bypassed = !Bypassed,
            GlobalHotkeys.ModControl | GlobalHotkeys.ModAlt, 0x42));       // Ctrl+Alt+B
        Hotkeys.Add(new HotkeyViewModel("startstop", "Start / Stop", () => StartStopCommand.Execute(null), 0, 0));

        if (saved?.Hotkeys != null)
            foreach (var b in saved.Hotkeys)
                Hotkeys.FirstOrDefault(h => h.ActionId == b.Action)?.Assign(b.Modifiers, b.Vk);
    }

    [RelayCommand]
    private void SetHotkey(HotkeyViewModel hk)
    {
        if (hk == null) return;
        foreach (var h in Hotkeys) h.Capturing = false;
        hk.Capturing = true;
    }

    public bool IsCapturingHotkey => Hotkeys.Any(h => h.Capturing);

    public void CancelCapture()
    {
        foreach (var h in Hotkeys) h.Capturing = false;
    }

    /// <summary>Called by the window when a key combo is captured for the pending hotkey.</summary>
    public void AssignCapturedKey(uint modifiers, uint vk)
    {
        var hk = Hotkeys.FirstOrDefault(h => h.Capturing);
        if (hk == null) return;
        foreach (var h in Hotkeys) if (h != hk && h.Modifiers == modifiers && h.Vk == vk) h.Clear();
        hk.Assign(modifiers, vk);
        OnHotkeysChanged();
    }

    private void OnHotkeysChanged()
    {
        HotkeysChanged?.Invoke();
        SaveSettings();
    }

    // ---- push-to-talk / push-to-mute ----
    private bool _pttEnabled;
    public bool PttEnabled
    {
        get => _pttEnabled;
        set
        {
            if (!Set(ref _pttEnabled, value)) return;
            _pttHeld = false;
            if (value) ApplyPttState();
            else { _suppressMuteFlash = true; Muted = false; _suppressMuteFlash = false; }
            SaveSettings();
            PttHookChanged?.Invoke();
        }
    }

    private bool _pttHoldToTalk = true;   // true: muted until held (talk); false: live until held (mute)
    public bool PttHoldToTalk
    {
        get => _pttHoldToTalk;
        set { if (Set(ref _pttHoldToTalk, value)) { ApplyPttState(); SaveSettings(); } }
    }

    private uint _pttVk;
    public uint PttVk
    {
        get => _pttVk;
        private set { _pttVk = value; OnPropertyChanged(nameof(PttKeyDisplay)); }
    }
    public string PttKeyDisplay => _pttVk == 0 ? "Not set" : HotkeyViewModel.Format(0, _pttVk);

    private bool _pttHeld;

    private bool _capturingPtt;
    public bool CapturingPtt
    {
        get => _capturingPtt;
        private set { if (Set(ref _capturingPtt, value)) OnPropertyChanged(nameof(SetPttText)); }
    }
    public string SetPttText => CapturingPtt ? "Press a key…" : "Set key";

    [RelayCommand]
    private void SetPttKey()
    {
        foreach (var h in Hotkeys) h.Capturing = false;
        CapturingPtt = true;
    }

    public void CancelPttCapture() => CapturingPtt = false;

    public void AssignPttKey(uint vk)
    {
        PttVk = vk;
        CapturingPtt = false;
        if (PttEnabled) ApplyPttState();
        SaveSettings();
    }

    /// <summary>Called by the keyboard hook when the push-to-talk key is pressed/released.</summary>
    public void PttKeyEvent(uint vk, bool down)
    {
        if (!PttEnabled || _pttVk == 0 || vk != _pttVk || down == _pttHeld) return;
        _pttHeld = down;
        ApplyPttState();
    }

    private void ApplyPttState()
    {
        if (!PttEnabled) return;
        bool muted = _pttHoldToTalk ? !_pttHeld : _pttHeld;
        _suppressMuteFlash = true;
        Muted = muted;
        _suppressMuteFlash = false;
    }
}
