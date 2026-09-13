using CommunityToolkit.Mvvm.ComponentModel;
using Pokemanager.App.Resources;
using Pokemanager.App.Services;
using Pokemanager.Model.Dump;
using Pokemanager.Model.Editing;
using Pokemanager.Model.Projects;

namespace Pokemanager.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IDialogs dialogs;
    private readonly AppSettings settings;

    public UprService Upr { get; }
    public AppSettings Settings => settings;
    public IDialogs Dialogs => dialogs;

    [ObservableProperty]
    public partial ObservableObject CurrentPage { get; set; }

    public MainWindowViewModel(IDialogs dialogs, AppSettings settings)
    {
        this.dialogs = dialogs;
        this.settings = settings;
        Upr = new UprService();

        // Start on the project list: with several projects, the user picks which one to open.
        CurrentPage = new WelcomeViewModel(this, dialogs);
    }

    public string Title => CurrentPage is EditorViewModel e ? string.Format(Strings.Main_TitleWithProject, Path.GetFileName(e.ProjectPath)) : "Pokemanager";

    partial void OnCurrentPageChanged(ObservableObject value) => OnPropertyChanged(nameof(Title));

    /// <summary>Opens a saved project in the editor. Returns the error to show, or null when it opened.</summary>
    public string? OpenProject(string path)
    {
        try
        {
            Manage(ProjectLoader.Load(path, Upr));
            return null;
        }
        catch (ProjectLoadException ex)
        {
            return ex.Message;
        }
    }

    /// <summary>Opens an already loaded project in the editor, optionally on a Pokémon of the save.</summary>
    public void Manage(LoadedProject loaded, Pokemanager.Save.SaveSlot? saveSlot = null)
    {
        var editor = new EditorViewModel(this, dialogs, Upr, settings, loaded.Path, loaded.Dump, loaded.Session);
        if (saveSlot is { } slot)
            editor.ShowSavePokemon(slot);
        settings.TouchProject(loaded.Path);
        CurrentPage = editor;
    }

    public void ShowWelcome() => CurrentPage = new WelcomeViewModel(this, dialogs);
}
