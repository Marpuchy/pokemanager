using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pokemanager.App.Resources;
using Pokemanager.App.Services;
using Pokemanager.Model.Dump;
using Pokemanager.Model.Projects;

namespace Pokemanager.App.ViewModels;

/// <summary>Start screen: recent projects, create a new one or open an existing one.</summary>
public partial class WelcomeViewModel : ObservableObject
{
    private readonly MainWindowViewModel main;
    private readonly IDialogs dialogs;

    public ObservableCollection<RecentProjectItem> RecentProjects { get; } = [];

    public bool HasRecentProjects => RecentProjects.Count > 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    public partial string DumpDirectory { get; set; }

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

    /// <summary>Documents/Pokemanager/pokemon-x.json, or pokemon-x2.json… if it already exists.</summary>
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
        if (await dialogs.PickFolderAsync(Strings.Welcome_PickDumpTitle, DumpDirectory) is { } path)
            DumpDirectory = path;
    }

    [RelayCommand]
    private async Task BrowseProject()
    {
        if (await dialogs.PickSaveFileAsync(Strings.Welcome_SaveProjectTitle, Path.GetFileName(ProjectPath)) is { } path)
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
            Error = Strings.Welcome_DumpInvalid + "\n- " + string.Join("\n- ", inspection.Problems);
            return;
        }

        if (File.Exists(ProjectPath))
        {
            Error = string.Format(Strings.Welcome_ProjectExists, ProjectPath);
            return;
        }

        try
        {
            string dump = Path.GetFullPath(DumpDirectory);
            // The base ROM is fixed when the project is created: randomized ROMs may appear in the same folder later.
            new Project { DumpDirectory = dump, RomFile = Project.FindBaseRom(dump), Language = GameTextLanguage.Current }.Save(ProjectPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Error = string.Format(Strings.Welcome_CreateFailed, ex.Message);
            return;
        }

        Error = main.OpenProject(ProjectPath);
    }

    [RelayCommand]
    private async Task OpenExisting()
    {
        if (await dialogs.PickOpenFileAsync(Strings.Welcome_OpenProjectTitle) is { } path)
            Error = main.OpenProject(path);
    }
}
