using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pokemanager.App.Services;
using Pokemanager.Bridge;
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

    public IReadOnlyList<EmulatorUserFolder> DetectedEmulators { get; } = EmulatorUserFolders.Detect();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    public partial string DumpDirectory { get; set; }

    [ObservableProperty]
    public partial LanguageOption Language { get; set; } = Languages[0];

    [ObservableProperty]
    public partial string EmulatorDirectory { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    public partial string ProjectPath { get; set; }

    [ObservableProperty]
    public partial string? Error { get; set; }

    public WelcomeViewModel(MainWindowViewModel main, IDialogs dialogs)
    {
        this.main = main;
        this.dialogs = dialogs;
        DumpDirectory = Environment.GetEnvironmentVariable("POKEMANAGER_DUMP") ?? "";
        EmulatorDirectory = DetectedEmulators.FirstOrDefault()?.Path ?? "";
        ProjectPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Pokemanager", "pokemon-x.json");
    }

    partial void OnEmulatorDirectoryChanged(string value) => OnPropertyChanged(nameof(SelectedDetectedEmulator));

    public EmulatorUserFolder? SelectedDetectedEmulator
    {
        get => DetectedEmulators.FirstOrDefault(e => e.Path == EmulatorDirectory);
        set
        {
            if (value is not null)
                EmulatorDirectory = value.Path;
        }
    }

    [RelayCommand]
    private async Task BrowseDump()
    {
        if (await dialogs.PickFolderAsync("Carpeta del volcado (con romfs y exefs)", DumpDirectory) is { } path)
            DumpDirectory = path;
    }

    [RelayCommand]
    private async Task BrowseEmulator()
    {
        if (await dialogs.PickFolderAsync("Carpeta de usuario del emulador (contiene load)", EmulatorDirectory) is { } path)
            EmulatorDirectory = path;
    }

    [RelayCommand]
    private async Task BrowseProject()
    {
        if (await dialogs.PickSaveFileAsync("Guardar proyecto como", Path.GetFileName(ProjectPath)) is { } path)
            ProjectPath = path;
    }

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
            var project = new Project
            {
                DumpDirectory = Path.GetFullPath(DumpDirectory),
                Language = Language.Value,
                EmulatorUserDirectory = string.IsNullOrWhiteSpace(EmulatorDirectory) ? null : Path.GetFullPath(EmulatorDirectory),
            };
            project.Save(ProjectPath);
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
