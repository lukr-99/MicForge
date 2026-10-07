namespace MicForge.Tests;

/// <summary>Preview values without audio devices, network calls or user data.</summary>
public sealed class SettingsPreview
{
    public bool SettingsSaveFailed => false;
    public string[] ThemeOptions { get; } = ["System", "Light", "Dark"];
    public string ThemeMode { get; set; } = "Dark";
    public bool AutoStartProcessing { get; set; }
    public bool MinimizeToTray { get; set; } = true;
    public bool MonitorEnabled { get; set; }
    public bool FollowDefaultInput { get; set; }
    public bool StartWithWindows { get; set; }
    public string[] Outputs { get; } = ["Headphones"];
    public string SelectedMonitorDevice { get; set; } = "Headphones";
    public string DspLoadText => "2%";
    public string GlitchText => "0";
    public string VersionText => "MicForge test preview";
    public string UpdateStatus => "Checking for updates...";
    public bool CheckingUpdate => true;
    public bool UpdateAvailable => false;
}
