using CommunityToolkit.Mvvm.ComponentModel;
using Pokemanager.App.Services;
using Pokemanager.Model.Dump;
using Pokemanager.Model.Editing;
using Pokemanager.Model.Projects;

namespace Pokemanager.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IDialogs dialogs;
    private readonly AppSettings settings;

    [ObservableProperty]
    public partial ObservableObject CurrentPage { get; set; }

    public MainWindowViewModel(IDialogs dialogs, AppSettings settings)
    {
        this.dialogs = dialogs;
        this.settings = settings;

        if (settings.LastProject is { } last && File.Exists(last) && TryOpen(last, out var editor, out _))
            CurrentPage = editor!;
        else
            CurrentPage = new WelcomeViewModel(this, dialogs);
    }

    public string Title => CurrentPage is EditorViewModel e ? $"Pokemanager — {Path.GetFileName(e.ProjectPath)}" : "Pokemanager";

    partial void OnCurrentPageChanged(ObservableObject value) => OnPropertyChanged(nameof(Title));

    /// <summary>Abre un proyecto guardado. Devuelve el error para mostrarlo, o null si se abrió.</summary>
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
            var session = EditorSession.Open(project);
            editor = new EditorViewModel(this, dialogs, path, dump, session);
            settings.LastProject = path;
            settings.Save();
            error = null;
            return true;
        }
        catch (InvalidDumpException ex)
        {
            error = ex.Message;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            error = $"No se pudo abrir {path}: {ex.Message}";
        }
        return false;
    }
}
