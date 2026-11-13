using CommunityToolkit.Mvvm.ComponentModel;
using Pokemanager.App.Resources;

namespace Pokemanager.App.ViewModels;

/// <summary>
/// The page shown while a project opens: the editor's shape in placeholders, so the window answers at once instead of
/// freezing on the start page (2026-11-13, user: "it takes long to load, at least a skeleton").
/// </summary>
public sealed class LoadingPageViewModel(string name) : ObservableObject
{
    public string Text { get; } = string.Format(Strings.Loading_Project, name);
}
