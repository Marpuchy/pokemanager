using CommunityToolkit.Mvvm.Input;
using Pokemanager.App.Resources;
using Pokemanager.App.Services;

namespace Pokemanager.App.ViewModels;

/// <summary>A project in the start screen's list: selecting it shows its team, "Manage" opens the editor.</summary>
public partial class RecentProjectItem(WelcomeViewModel owner, RecentProject recent)
{
    public string Path => recent.Path;
    public string Name => System.IO.Path.GetFileNameWithoutExtension(recent.Path);
    public string Details => string.Format(Strings.Recent_Details, recent.Path, recent.LastOpened);

    [RelayCommand]
    private void Manage() => owner.Manage(this);

    [RelayCommand]
    private void Forget() => owner.Forget(this);
}
