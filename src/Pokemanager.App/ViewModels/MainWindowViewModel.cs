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

    /// <summary>Opens a saved project. Returns the error to show, or null when it opened.</summary>
    public string? OpenProject(string path)
    {
        if (!TryOpen(path, out var editor, out string? error))
            return error;
        CurrentPage = editor!;
        return null;
    }

    public void ShowWelcome() => CurrentPage = new WelcomeViewModel(this, dialogs);

    private bool TryOpen(string path, out EditorViewModel? editor, out string? error)
    {
        editor = null;
        try
        {
            var project = Project.Load(path);
            var dump = GameDump.Open(project.RomFsPath, project.ExeFsPath, project.Language);

            // If the project's randomization is cached, the advanced editor works on top of it.
            string? randomRomFs = Upr.TryGetCached(project, project.Randomization.Preset) is { } cached
                ? Path.Combine(cached.TitleDirectory, "romfs")
                : null;
            var session = EditorSession.Open(project, randomRomFs);

            editor = new EditorViewModel(this, dialogs, Upr, settings, path, dump, session);
            settings.TouchProject(path);
            error = null;
            return true;
        }
        catch (InvalidDumpException ex)
        {
            error = ex.Message;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            error = string.Format(Strings.Main_OpenFailed, path, ex.Message);
        }
        return false;
    }
}
