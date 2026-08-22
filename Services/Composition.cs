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

    /// <summary>Marketing version (Major.Minor.Patch) read from the assembly, set by the csproj.</summary>
    private static string CurrentVersion()
    {
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        return v == null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
    }
}
