using Avalonia.Threading;
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
        Room = new RoomViewModel(this);

        // Start on the project list: with several projects, the user picks which one to open.
        CurrentPage = new WelcomeViewModel(this, dialogs);

        capTimer = new DispatcherTimer(TimeSpan.FromSeconds(3), DispatcherPriority.Background, OnCapTick);
        capTimer.Start();
    }

    // ----- Level cap: whatever got past it is brought back when the emulator closes -----
    // It lived in the editor and stopped with it, so a game played from the start page was never trimmed
    // (2026-11-04, user: a Pokémon candied to 25 over a cap of 20). It belongs to the application now: on the edge
    // emulator running → closed, and whenever a project is shown (start, page change, another project picked) with the
    // emulator already closed, the project on screen gets the pass.
    private readonly DispatcherTimer capTimer;
    private bool emulatorWasRunning, checkingEmulator, capCheckPending = true;

    /// <summary>Asks for a level cap pass at the next tick in which the emulator is closed.</summary>
    public void RequestLevelCapCheck() => capCheckPending = true;

    private async void OnCapTick(object? sender, EventArgs e)
    {
        if (checkingEmulator)
            return;
        checkingEmulator = true;
        try
        {
            // Enumerating every process is not free, so it happens off the UI thread.
            string name = settings.EffectiveEmulatorName;
            bool running = await Task.Run(() => Pokemanager.Bridge.EmulatorUserFolders.RunningEmulators(name).Count > 0);
            if (!running && (emulatorWasRunning || capCheckPending))
                capCheckPending = !TrimShownProject();
            emulatorWasRunning = running;
        }
        finally
        {
            checkingEmulator = false;
        }
    }

    /// <summary>The pass on the project on screen; false when there is none yet.</summary>
    private bool TrimShownProject()
    {
        switch (CurrentPage)
        {
            case EditorViewModel editor:
                editor.LevelCap.TrimNow();
                return true;
            case WelcomeViewModel welcome:
                return welcome.TrimShownProject();
            default:
                return true;
        }
    }

    /// <summary>Version of the application (from the build: <c>Directory.Build.props</c> or the release tag).</summary>
    public static string Version { get; } = typeof(MainWindowViewModel).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "";

    public string Title => CurrentPage is EditorViewModel e
        ? string.Format(Strings.Main_TitleWithProject, Path.GetFileName(e.ProjectPath))
        : $"Pokemanager {Version}";

    partial void OnCurrentPageChanged(ObservableObject value)
    {
        OnPropertyChanged(nameof(Title));
        // The accent and the glow follow the game being edited (the start page sets its own as projects are picked).
        Services.AppTheme.UseGame(value switch
        {
            EditorViewModel editor => editor.Session.Project.Game,
            WelcomeViewModel welcome => welcome.SelectedProject?.Game,
            _ => null,
        });
        RequestLevelCapCheck();
    }

    /// <summary>A closed editor stops watching the save for the level cap.</summary>
    partial void OnCurrentPageChanging(ObservableObject? oldValue, ObservableObject newValue)
    {
        if (oldValue is EditorViewModel editor && !ReferenceEquals(oldValue, newValue))
            editor.LevelCap.Dispose();
    }

    /// <summary>The multiplayer room: it lives with the app, whichever page is shown.</summary>
    public RoomViewModel Room { get; }

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
    public void Manage(LoadedProject loaded, Pokemanager.Save.SaveSlot? saveSlot = null, bool pokedex = false)
    {
        var editor = new EditorViewModel(this, dialogs, Upr, settings, loaded.Path, loaded.Dump, loaded.Session);
        if (saveSlot is { } slot)
            editor.ShowSavePokemon(slot);
        else if (pokedex)
            editor.ShowSaveDex();
        settings.TouchProject(loaded.Path);
        CurrentPage = editor;
    }

    public void ShowWelcome() => CurrentPage = new WelcomeViewModel(this, dialogs);
}
