using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pokemanager.App.Services;
using Pokemanager.Model.Dump;
using Pokemanager.Model.Projects;

namespace Pokemanager.App.ViewModels;

public sealed record LanguageOption(GameLanguage Value, string Name);

/// <summary>Pantalla inicial: crear un proyecto nuevo o abrir uno existente.</summary>
public partial class WelcomeViewModel : ObservableObject
{
    private readonly MainWindowViewModel main;
    private readonly IDialogs dialogs;

    public static IReadOnlyList<LanguageOption> Languages { get; } =
    [
        new(GameLanguage.Spanish, "Español"),
        new(GameLanguage.English, "English"),
        new(GameLanguage.French, "Français"),
        new(GameLanguage.Italian, "Italiano"),
        new(GameLanguage.German, "Deutsch"),
        new(GameLanguage.JapaneseKana, "日本語 (カナ)"),
        new(GameLanguage.JapaneseKanji, "日本語 (漢字)"),
        new(GameLanguage.Korean, "한국어"),
    ];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    public partial string DumpDirectory { get; set; }

    [ObservableProperty]
    public partial LanguageOption Language { get; set; } = Languages[0];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    public partial string ProjectPath { get; set; }

    [ObservableProperty]
    public partial string? Error { get; set; }

    public System.Collections.ObjectModel.ObservableCollection<RecentProjectItem> RecentProjects { get; } = [];

    public bool HasRecentProjects => RecentProjects.Count > 0;

    public WelcomeViewModel(MainWindowViewModel main, IDialogs dialogs)
    {
        this.main = main;
        this.dialogs = dialogs;
        DumpDirectory = Environment.GetEnvironmentVariable("POKEMANAGER_DUMP") ?? "";
        ProjectPath = SuggestProjectPath();
        LoadRecentProjects();
    }

    private void LoadRecentProjects()
    {
        RecentProjects.Clear();
        foreach (var r in main.Settings.ExistingRecentProjects())
            RecentProjects.Add(new RecentProjectItem(this, r));
        OnPropertyChanged(nameof(HasRecentProjects));
    }

    /// <summary>Documentos/Pokemanager/pokemon-x.json, o pokemon-x2.json… si ya existe.</summary>
    private static string SuggestProjectPath()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Pokemanager");
        string path = Path.Combine(dir, "pokemon-x.json");
        for (int i = 2; File.Exists(path); i++)
            path = Path.Combine(dir, $"pokemon-x{i}.json");
        return path;
    }

    internal void Open(RecentProjectItem item) => Error = main.OpenProject(item.Path);

    internal void Forget(RecentProjectItem item)
    {
        main.Settings.ForgetProject(item.Path);
        LoadRecentProjects();
    }

    [RelayCommand]
    private async Task BrowseDump()
    {
        if (await dialogs.PickFolderAsync("Carpeta del volcado (con el .3ds, romfs y exefs)", DumpDirectory) is { } path)
            DumpDirectory = path;
    }

    [RelayCommand]
    private async Task BrowseProject()
    {
        if (await dialogs.PickSaveFileAsync("Guardar proyecto como", Path.GetFileName(ProjectPath)) is { } path)
            ProjectPath = path;
    }

    [RelayCommand]
    private async Task OpenSettings() =>
        await dialogs.ShowSettingsAsync(new SettingsViewModel(main.Settings, main.Upr, dialogs, null, GameTitle.X));

    private bool CanCreate() => !string.IsNullOrWhiteSpace(DumpDirectory) && !string.IsNullOrWhiteSpace(ProjectPath);

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private void Create()
    {
        Error = null;
        var inspection = DumpInspector.Inspect(Path.Combine(DumpDirectory, "romfs"), Path.Combine(DumpDirectory, "exefs"));
        if (!inspection.IsValid)
        {
            Error = "El volcado no es utilizable:\n- " + string.Join("\n- ", inspection.Problems);
            return;
        }

        if (File.Exists(ProjectPath))
        {
            Error = $"Ya existe un proyecto en {ProjectPath}. Ábrelo o elige otro nombre.";
            return;
        }

        try
        {
            string dump = Path.GetFullPath(DumpDirectory);
            // La ROM base se fija al crear el proyecto: después pueden aparecer ROM randomizadas en la misma carpeta.
            new Project { DumpDirectory = dump, RomFile = Project.FindBaseRom(dump), Language = Language.Value }.Save(ProjectPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Error = $"No se pudo crear el proyecto: {ex.Message}";
            return;
        }

        Error = main.OpenProject(ProjectPath);
    }

    [RelayCommand]
    private async Task OpenExisting()
    {
        if (await dialogs.PickOpenFileAsync("Abrir proyecto") is { } path)
            Error = main.OpenProject(path);
    }
}
