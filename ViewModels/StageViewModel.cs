using System;
using System.Collections.ObjectModel;

namespace MicForge.ViewModels;

/// <summary>A processing stage: header, optional on/off toggle, and its parameters.</summary>
public class StageViewModel : ViewModelBase
{
    private readonly Func<bool> _getEnabled;
    private readonly Action<bool> _setEnabled;

    public StageViewModel(string title, string accent,
        Func<bool> getEnabled, Action<bool> setEnabled, bool canToggle = true)
    {
        Title = title;
        Accent = accent;
        _getEnabled = getEnabled;
        _setEnabled = setEnabled;
        CanToggle = canToggle;
    }

    public string Title { get; }
    public string Accent { get; }
    public bool CanToggle { get; }
    public bool ToggleEnabled { get; set; } = true;   // false when a feature is unavailable (e.g. RNNoise)
    public string Note { get; set; }
    public string Info { get; set; }                  // long "what this does" text for the info button

    // Which specialised visual (if any) this stage offers in Graph view.
    public virtual bool IsEq => false;
    public virtual bool IsCompressor => false;
    public bool IsGate { get; set; }
    public bool IsDeEsser { get; set; }

    public ObservableCollection<ParamViewModel> Params { get; } = new();

    public bool Enabled
    {
        get => _getEnabled();
        set { _setEnabled(value); OnPropertyChanged(); }
    }

    public ParamViewModel Add(string label, double min, double max, double step,
        Func<double> get, Action<double> set, string format = "0.#", string unit = "", string help = "")
    {
        var p = new ParamViewModel(label, min, max, step, get, set, format, unit, help);
        Params.Add(p);
        return p;
    }
}
