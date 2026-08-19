using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;

namespace MicForge;

public partial class App : Application
{
    /// <summary>True when launched with --tray (e.g. from the Windows startup entry).</summary>
    public static bool StartHidden { get; private set; }

    private const string InstanceName = "MicForge_SingleInstance_9a1f";
    private const string ShowSignalName = "MicForge_ShowWindow_9a1f";
    private Mutex _instanceMutex;
    private EventWaitHandle _showSignal;
    private ServiceProvider _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        foreach (var a in e.Args)
            if (string.Equals(a, "--tray", StringComparison.OrdinalIgnoreCase))
                StartHidden = true;

        // Single instance: if MicForge is already running, wake it and exit this copy.
        _instanceMutex = new Mutex(true, InstanceName, out bool isPrimary);
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalName);
        if (!isPrimary)
        {
            try { _showSignal.Set(); } catch { }   // ask the running instance to come forward
            Shutdown();
            return;
        }
        StartShowSignalListener();

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error("Fatal unhandled exception", args.ExceptionObject as Exception ?? new Exception("unknown"));

        Log.Info($"MicForge starting (v{System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}).");
        base.OnStartup(e);

        // Compose the object graph and show the main window (constructor-injected).
        _services = Composition.Build();
        var window = _services.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }

    /// <summary>Background wait for a second launch asking us to show the window.</summary>
    private void StartShowSignalListener()
    {
        var t = new Thread(() =>
        {
            while (true)
            {
                try { _showSignal.WaitOne(); }
                catch { return; }
                Dispatcher.BeginInvoke(() => (MainWindow as MainWindow)?.ShowFromTray());
            }
        })
        { IsBackground = true, Name = "MicForge-ShowSignal" };
        t.Start();
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
