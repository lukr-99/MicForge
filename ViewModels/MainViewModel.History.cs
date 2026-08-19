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

/// <summary>Coalesced undo / redo of the whole chain.</summary>
public sealed partial class MainViewModel
{
    // ---- undo / redo (coalesced snapshots of the whole chain) ----
    private readonly Stack<string> _undo = new();
    private readonly Stack<string> _redo = new();
    private string _histLast;
    private bool _applyingHistory;
    private DispatcherTimer _histTimer;

    /// <summary>Every tick, if the chain changed since the last checkpoint, push the old state.</summary>
    private void CaptureHistory()
    {
        if (_applyingHistory) return;
        string cur;
        try { cur = Snapshot().ToJson(); } catch { return; }
        if (_histLast == null) { _histLast = cur; return; }
        if (cur == _histLast) return;

        _undo.Push(_histLast);
        if (_undo.Count > 60)
        {
            var keep = _undo.ToArray();               // newest-first
            _undo.Clear();
            for (int i = 59; i >= 0; i--) _undo.Push(keep[i]);
        }
        _redo.Clear();
        _histLast = cur;
        RefreshHistoryCommands();
    }

    private bool CanUndo => _undo.Count > 0;
    private bool CanRedo => _redo.Count > 0;

    /// <summary>Re-query the Undo/Redo buttons after the stacks change.</summary>
    private void RefreshHistoryCommands()
    {
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        if (_undo.Count == 0) return;
        _applyingHistory = true;
        try
        {
            _redo.Push(Snapshot().ToJson());
            var prev = _undo.Pop();
            ApplyHistory(prev);
            _histLast = prev;
        }
        finally { _applyingHistory = false; RefreshHistoryCommands(); }
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        if (_redo.Count == 0) return;
        _applyingHistory = true;
        try
        {
            _undo.Push(Snapshot().ToJson());
            var next = _redo.Pop();
            ApplyHistory(next);
            _histLast = next;
        }
        finally { _applyingHistory = false; RefreshHistoryCommands(); }
    }

    private void ApplyHistory(string json)
    {
        var s = Settings.FromJson(json);
        if (s == null) return;
        s.ApplyTo(_engine.Chain);
        SetCraftStates(s);
        BuildStages();
        ApplyStageOrder(s.StageOrder);
        SaveSettings();
    }
}
