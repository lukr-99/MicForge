using System;
using System.Windows;

namespace MicForge;

public partial class App : Application
{
    /// <summary>True when launched with --tray (e.g. from the Windows startup entry).</summary>
    public static bool StartHidden { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        foreach (var a in e.Args)
            if (string.Equals(a, "--tray", StringComparison.OrdinalIgnoreCase))
                StartHidden = true;

        base.OnStartup(e);
    }
}
