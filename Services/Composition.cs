using System;
using System.Net.Http;
using System.Reflection;
using DotNetLib.Core.Updating;
using MicForge.Audio;
using MicForge.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace MicForge;

/// <summary>
/// The composition root: the single place the object graph is wired. The app resolves
/// <see cref="MainWindow"/> from here; its dependencies (view-model, audio engine) are
/// constructor-injected. Stateless utilities (Log, PresetLibrary, StartupManager) stay static.
/// </summary>
public static class Composition
{
    public static ServiceProvider Build()
    {
        var services = new ServiceCollection();

        services.AddSingleton<AudioEngine>();
        services.AddSingleton(sp => new AppTheme(System.Windows.Application.Current.Resources));

        // Self-update against the app's public GitHub Releases (see DotNetLib.Core.Updating).
        services.AddSingleton<HttpClient>();
        services.AddSingleton(sp =>
        {
            var http = sp.GetRequiredService<HttpClient>();
            var source = new GitHubReleaseSource(http, "lukr-99", "MicForge",
                name => name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
            return new UpdateService(source, CurrentVersion(), http);
        });

        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider();
    }

    /// <summary>Version including the development suffix, without build metadata.</summary>
    internal static string CurrentVersion()
    {
        var informational = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrEmpty(informational)) return informational.Split('+')[0];
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        return v == null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
    }
}
