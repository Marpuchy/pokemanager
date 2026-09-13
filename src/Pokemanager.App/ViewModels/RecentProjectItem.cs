using CommunityToolkit.Mvvm.Input;
using Pokemanager.App.Services;

namespace Pokemanager.App.ViewModels;

/// <summary>Proyecto de la lista de recientes de la pantalla inicial.</summary>
public partial class RecentProjectItem(WelcomeViewModel owner, RecentProject recent)
{
    public string Path => recent.Path;
    public string Name => System.IO.Path.GetFileNameWithoutExtension(recent.Path);
    public string Details => $"{recent.Path} · abierto el {recent.LastOpened:g}";

    [RelayCommand]
    private void Open() => owner.Open(this);

    [RelayCommand]
    private void Forget() => owner.Forget(this);
}
