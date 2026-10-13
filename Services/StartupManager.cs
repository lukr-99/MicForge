using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace MicForge;

/// <summary>Manages the "start with Windows" entry under HKCU ...\Run.</summary>
public static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "MicForge";

    public static string ExePath
    {
        get
        {
            try { return Process.GetCurrentProcess().MainModule?.FileName; }
            catch { return null; }
        }
    }

    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(ValueName) != null;
            }
            catch { return false; }
        }
    }

    /// <summary>Adds or removes the Run entry. Returns false when the registry write failed.</summary>
    public static bool SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled)
            {
                var exe = ExePath;
                if (string.IsNullOrEmpty(exe)) return false;
                key.SetValue(ValueName, $"\"{exe}\" --tray");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Could not update the Windows startup entry", ex);
            return false;
        }
    }
}
