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

    /// <summary>The decrypted ROM the new project is made from.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    public partial string RomPath { get; set; } = "";

    /// <summary>Name of the new project (its file in Documents\Pokemanager\Projects).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    [NotifyPropertyChangedFor(nameof(ProjectFileText))]
    public partial string ProjectName { get; set; } = "";

    /// <summary>"Pokémon Ultra Moon" once the ROM is recognized.</summary>
    [ObservableProperty]
    public partial string? DetectedGame { get; set; }

    /// <summary>The chosen ROM already has a randomizer log next to it.</summary>
    [ObservableProperty]
    public partial bool RomLooksRandomized { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    public partial bool IsCreating { get; set; }

    [ObservableProperty]
    public partial string? CreateStatus { get; set; }

    private GameTitle? detectedTitle;
    private string lastSuggestedName = "";

    public string ProjectFileText => string.Format(Strings.Welcome_ProjectWillBe, ProjectFilePath(ProjectName.Trim()));

    public string GamesFolderText => string.Format(Strings.Welcome_GameDataWillBe, AppSettings.GamesRoot);

    [ObservableProperty]
    public partial string? Error { get; set; }

    /// <summary>The project whose team is shown on the right.</summary>
    [ObservableProperty]
    public partial RecentProjectItem? SelectedProject { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreview))]
    public partial ProjectPreviewViewModel? Preview { get; set; }

    public bool HasPreview => Preview is not null && !ShowNewProject;

    [ObservableProperty]
    public partial bool IsLoadingPreview { get; set; }

    [ObservableProperty]
    public partial string? PreviewError { get; set; }

    /// <summary>The right side shows the new project form instead of a preview.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreview), nameof(ShowPreviewArea))]
    public partial bool ShowNewProject { get; set; }

    public bool ShowPreviewArea => !ShowNewProject;

    private int previewVersion;

    public WelcomeViewModel(MainWindowViewModel main, IDialogs dialogs)
    {
        this.main = main;
        this.dialogs = dialogs;
        LoadRecentProjects();
        ShowNewProject = RecentProjects.Count == 0;
        SelectedProject = RecentProjects.FirstOrDefault(p => string.Equals(p.Path, main.Settings.LastProject, StringComparison.OrdinalIgnoreCase))
                          ?? RecentProjects.FirstOrDefault();
    }

    private void LoadRecentProjects()
    {
        RecentProjects.Clear();
        foreach (var r in main.Settings.ExistingRecentProjects())
            RecentProjects.Add(new RecentProjectItem(this, r));
        OnPropertyChanged(nameof(HasRecentProjects));
    }

    partial void OnSelectedProjectChanged(RecentProjectItem? value)
    {
        if (value is not null)
        {
            ShowNewProject = false;
            _ = LoadPreviewAsync(value);
        }
    }

    /// <summary>Loads the project and its save in the background; only the latest selection is shown.</summary>
    public async Task LoadPreviewAsync(RecentProjectItem item)
    {
        int version = ++previewVersion;
        Preview = null;
        PreviewError = null;
        IsLoadingPreview = true;
        try
        {
            var preview = await Task.Run(() => new ProjectPreviewViewModel(main, ProjectLoader.Load(item.Path, main.Upr), () => LoadPreviewAsync(item)));
            if (version == previewVersion)
                Preview = preview;
        }
        catch (ProjectLoadException ex)
        {
            if (version == previewVersion)
                PreviewError = ex.Message;
        }
        finally
        {
            if (version == previewVersion)
                IsLoadingPreview = false;
        }
    }

    internal void Manage(RecentProjectItem item)
    {
        if (Preview is { } preview && string.Equals(preview.Loaded.Path, item.Path, StringComparison.OrdinalIgnoreCase))
            main.Manage(preview.Loaded);
        else
            Error = main.OpenProject(item.Path);
    }

    [RelayCommand]
    private void NewProject()
    {
        SelectedProject = null;
        ShowNewProject = true;
    }

    private static string ProjectFilePath(string name) => Path.Combine(AppSettings.ProjectsRoot, name + ".json");

    internal void Forget(RecentProjectItem item)
    {
        main.Settings.ForgetProject(item.Path);
        bool wasSelected = SelectedProject == item;
        LoadRecentProjects();
        if (wasSelected)
        {
            Preview = null;
            SelectedProject = RecentProjects.FirstOrDefault();
            ShowNewProject = RecentProjects.Count == 0;
        }
    }

    [RelayCommand]
    private async Task BrowseRom()
    {
        if (await dialogs.PickOpenFileAsync(Strings.Welcome_PickRomTitle, ["*.3ds", "*.cci", "*.cxi"]) is { } path)
            RomPath = path;
    }

    /// <summary>Recognizes the game as soon as a ROM is chosen, and names the project after the ROM.</summary>
    partial void OnRomPathChanged(string value)
    {
        Error = null;
        DetectedGame = null;
        detectedTitle = null;
        RomLooksRandomized = false;
        if (string.IsNullOrWhiteSpace(value) || !File.Exists(value))
            return;
        try
        {
            detectedTitle = GameImporter.Detect(value);
            DetectedGame = string.Format(Strings.Welcome_Detected, detectedTitle.Value.DisplayName());
            RomLooksRandomized = File.Exists(value + ".log");
            string suggested = Path.GetFileNameWithoutExtension(value);
            if (ProjectName.Length == 0 || ProjectName == lastSuggestedName)
                ProjectName = suggested;
            lastSuggestedName = suggested;
        }
        catch (Exception ex) when (ex is RomReadException or IOException or UnauthorizedAccessException)
        {
            Error = ex.Message;
        }
        CreateCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task OpenSettings() =>
        await dialogs.ShowSettingsAsync(new SettingsViewModel(main.Settings, main.Upr, dialogs, null, GameTitle.X));

    private bool CanCreate() => !IsCreating && detectedTitle is not null && !string.IsNullOrWhiteSpace(ProjectName);

    /// <summary>
    /// Imports the ROM's game data into Documents\Pokemanager\Games (reusing it if that ROM was already imported),
    /// creates the project in Documents\Pokemanager\Projects and opens it.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCreate))]
    private async Task Create()
    {
        Error = null;
        string name = ProjectName.Trim();
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            Error = Strings.Welcome_NameInvalid;
            return;
        }
        string projectPath = ProjectFilePath(name);
        if (File.Exists(projectPath))
        {
            Error = string.Format(Strings.Welcome_ProjectExists, projectPath);
            return;
        }

        string rom = Path.GetFullPath(RomPath);
        IsCreating = true;
        try
        {
            var progress = new Progress<string>(text => CreateStatus = text);
            var loaded = await Task.Run(() =>
            {
                Directory.CreateDirectory(AppSettings.GamesRoot);
                var imported = GameImporter.Import(rom, AppSettings.GamesRoot, progress);
                // The base ROM is fixed when the project is created: randomized ROMs may appear in the same folder later.
                new Project { DumpDirectory = imported.Directory, RomFile = rom, Game = imported.Game, Language = GameTextLanguage.Current }
                    .Save(projectPath);
                return ProjectLoader.Load(projectPath, main.Upr);
            });
            main.Manage(loaded);
        }
        catch (Exception ex) when (ex is RomReadException or IOException or UnauthorizedAccessException)
        {
            Error = string.Format(Strings.Welcome_CreateFailed, ex.Message);
        }
        catch (ProjectLoadException ex)
        {
            Error = ex.Message;
        }
        finally
        {
            IsCreating = false;
            CreateStatus = null;
        }
    }

    [RelayCommand]
    private async Task OpenExisting()
    {
        if (await dialogs.PickOpenFileAsync(Strings.Welcome_OpenProjectTitle) is { } path)
            Error = main.OpenProject(path);
    }
}
