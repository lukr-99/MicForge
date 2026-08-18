using System;
using System.Windows;
using System.Windows.Threading;

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

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error("Fatal unhandled exception", args.ExceptionObject as Exception ?? new Exception("unknown"));

        Log.Info($"MicForge starting (v{System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}).");
        base.OnStartup(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Unhandled UI exception", e.Exception);
        MessageBox.Show(
            "MicForge hit an unexpected error and logged the details.\n\n" + e.Exception.Message,
            "MicForge", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;   // keep the app alive; the error is logged
    }
}
