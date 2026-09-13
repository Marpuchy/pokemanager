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

            // Si la randomización del proyecto ya está en caché, el editor avanzado trabaja sobre ella.
            string? randomRomFs = Upr.TryGetCached(project, project.Randomization.Preset) is { } cached
                ? Path.Combine(cached.TitleDirectory, "romfs")
                : null;
            var session = EditorSession.Open(project, randomRomFs);

            editor = new EditorViewModel(this, dialogs, Upr, settings, path, dump, session);
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
