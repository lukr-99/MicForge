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
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider();
    }
}
