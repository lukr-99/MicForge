using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MicForge.ViewModels;

/// <summary>
/// Base view-model. Derives CommunityToolkit.Mvvm's <see cref="ObservableObject"/> for change
/// notification and to enable the <c>[ObservableProperty]</c> / <c>[RelayCommand]</c> source
/// generators in derived VMs. <see cref="Set"/> is a thin alias over <c>SetProperty</c> kept
/// for existing call sites.
/// </summary>
public abstract class ViewModelBase : ObservableObject
{
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string name = null)
        => SetProperty(ref field, value, name);
}
