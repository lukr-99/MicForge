using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using DotNetLib.Tray;

namespace MicForge;

public partial class App : Application
{
    /// <summary>True when launched with --tray (e.g. from the Windows startup entry).</summary>
    public static bool StartHidden { get; private set; }

    private const string InstanceName = "MicForge_SingleInstance_9a1f";
    private SingleInstance _instance;
    private ServiceProvider _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        foreach (var a in e.Args)
            if (string.Equals(a, "--tray", StringComparison.OrdinalIgnoreCase))
                StartHidden = true;

        // Wake an older installed version during the transition to the shared instance lock.
        if (Mutex.TryOpenExisting(InstanceName, out var legacy))
        {
            legacy.Dispose();
            using var signal = new EventWaitHandle(false, EventResetMode.AutoReset, "MicForge_ShowWindow_9a1f");
            signal.Set();
            Shutdown();
            return;
        }

        _instance = SingleInstance.TryAcquire("MicForge");
        if (_instance == null)
        {
            SingleInstance.Knock("MicForge");
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error("Fatal unhandled exception", args.ExceptionObject as Exception ?? new Exception("unknown"));

        Log.Info($"MicForge starting (v{System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}).");
        base.OnStartup(e);

        var appStyles = Resources.MergedDictionaries[0];
        Resources.MergedDictionaries.Remove(appStyles);
        DotNetLib.Tray.TrayResources.Merge(Resources);
        Resources.MergedDictionaries.Add(appStyles);

        // Compose the object graph and show the main window (constructor-injected).
        _services = Composition.Build();
        var window = _services.GetRequiredService<MainWindow>();
        MainWindow = window;
        _instance.Listen(() => Dispatcher.BeginInvoke(() => window.OnLaunchedAgain()));
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Unhandled UI exception", e.Exception);
        TrayMessageWindow.Inform("MicForge",
            "MicForge hit an unexpected error and logged the details.\n\n" + e.Exception.Message,
            prepare: window => _services?.GetService<AppTheme>()?.Attach(window));
        e.Handled = true;   // keep the app alive; the error is logged
    }
}
