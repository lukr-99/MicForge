using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Controls;
using DotNetLib.Tray;
using MicForge.ViewModels;

namespace MicForge;

/// <summary>Displays save feedback using the settings kit's row controls.</summary>
public partial class SettingsView : UserControl
{
    private readonly List<SettingsRow> _failedRows = new();
    private MainViewModel _subscribed;

    public SettingsView()
    {
        InitializeComponent();
        Loaded += (_, _) => Subscribe();
        DataContextChanged += (_, _) => Subscribe();
        Unloaded += (_, _) => Unsubscribe();
    }

    private void Subscribe()
    {
        Unsubscribe();
        _subscribed = DataContext as MainViewModel;
        if (_subscribed != null) _subscribed.PropertyChanged += OnSaveStateChanged;
    }

    private void Unsubscribe()
    {
        if (_subscribed != null) _subscribed.PropertyChanged -= OnSaveStateChanged;
        _subscribed = null;
    }

    private void OnSaveStateChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.SettingsSaveFailed) || _subscribed.SettingsSaveFailed) return;
        foreach (var row in _failedRows) row.ClearResult();
        _failedRows.Clear();
    }

    private void Toggle_Changed(object sender, bool value) => ShowSaveFeedback((SettingsRow)sender);
    private void Choice_Changed(object sender, object value) => ShowSaveFeedback((SettingsRow)sender);

    // The bound setter has already written the change by the time the row raises its event
    // (the row sets its value first), so only report that write's result here.
    private void ShowSaveFeedback(SettingsRow row)
    {
        if (DataContext is not MainViewModel vm) return;
        if (vm.LastWriteSucceeded)
        {
            row.ClearResult();
            row.ShowSaved();
        }
        else
        {
            if (!_failedRows.Contains(row)) _failedRows.Add(row);
            row.ShowResult("Couldn't save. Use Retry in Problems.", isError: true);
        }
    }
}
