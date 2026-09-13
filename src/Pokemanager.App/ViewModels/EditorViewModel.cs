using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pokemanager.App.Services;
using Pokemanager.Bridge;
using Pokemanager.Model.Build;
using Pokemanager.Model.Dump;
using Pokemanager.Model.Editing;
using Pokemanager.Model.Edits;
using Pokemanager.Randomizer;

namespace Pokemanager.App.ViewModels;

public partial class EditorViewModel : ObservableObject
{
    private readonly MainWindowViewModel main;
    private readonly IDialogs dialogs;
    private readonly UprService upr;
    private List<ListEntryViewModel> allSpecies = [];
    private List<ListEntryViewModel> allMoves = [];

    public string ProjectPath { get; }
    public GameDump Dump { get; }
    public RandomizerViewModel Randomizer { get; }

    [ObservableProperty]
    public partial EditorSession Session { get; private set; }

    [ObservableProperty]
    public partial GameNames Names { get; private set; }

    public ObservableCollection<ListEntryViewModel> Species { get; } = [];
    public ObservableCollection<ListEntryViewModel> Moves { get; } = [];

    [ObservableProperty]
    public partial string SpeciesFilter { get; set; } = "";

    [ObservableProperty]
    public partial string MoveFilter { get; set; } = "";

    [ObservableProperty]
    public partial ListEntryViewModel? SelectedSpecies { get; set; }

    [ObservableProperty]
    public partial ListEntryViewModel? SelectedMove { get; set; }

    [ObservableProperty]
    public partial SpeciesDetailViewModel? SpeciesDetail { get; set; }

    [ObservableProperty]
    public partial MoveDetailViewModel? MoveDetail { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DirtyText))]
    public partial bool IsDirty { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; } = "";

    [ObservableProperty]
    public partial bool StatusIsError { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    public partial bool IsBusy { get; set; }

    public string GameText => $"Pokémon {Dump.Title} · {Dump.Title.TitleIdHex()} · {Session.Project.DumpDirectory}";
    public string EditCountText => Session.Project.Edits.Count == 1 ? "1 edición manual" : $"{Session.Project.Edits.Count} ediciones manuales";
    public string DirtyText => IsDirty ? "sin guardar" : "guardado";
    public string EmulatorText => Session.Project.EmulatorUserDirectory is { } dir
        ? EmulatorUserFolders.ModDirectory(dir, Dump.Title.TitleIdHex())
        : "Sin carpeta de emulador";

    /// <summary>Texto de qué base usa el editor avanzado: el volcado o el random de una semilla.</summary>
    public string BaseText => Session.Layers.Roots.Count > 1
        ? $"Base: randomización con semilla {Session.Project.Randomization.Seed}"
        : "Base: juego original";

    public EditorViewModel(MainWindowViewModel main, IDialogs dialogs, UprService upr, string projectPath, GameDump dump, EditorSession session)
    {
        this.main = main;
        this.dialogs = dialogs;
        this.upr = upr;
        ProjectPath = projectPath;
        Dump = dump;
        Session = session;
        Names = new GameNames(dump, session.Original);
        LoadLists();
        Randomizer = new RandomizerViewModel(this, dialogs, upr);
    }

    /// <summary>Rellena las listas del editor avanzado a partir de la sesión actual.</summary>
    private void LoadLists()
    {
        string[] personalTables = [GameTables.Personal, GameTables.Learnsets];
        allSpecies = Enumerable.Range(1, Session.Current.Personal.Length - 1) // la entrada 0 es un marcador vacío
            .Select(i => new ListEntryViewModel(Session, personalTables, i, Names.PersonalEntries[i]))
            .ToList();
        allMoves = Enumerable.Range(1, Session.Current.Moves.Length - 1)
            .Select(i => new ListEntryViewModel(Session, [GameTables.Moves], i, Names.Moves[i]))
            .ToList();

        int? species = SelectedSpecies?.Id, move = SelectedMove?.Id;
        SpeciesDetail = null;
        MoveDetail = null;
        ApplyFilter(Species, allSpecies, SpeciesFilter);
        ApplyFilter(Moves, allMoves, MoveFilter);
        SelectedSpecies = Species.FirstOrDefault(s => s.Id == species) ?? Species.FirstOrDefault();
        SelectedMove = Moves.FirstOrDefault(m => m.Id == move) ?? Moves.FirstOrDefault();

        Session.Changed += OnSessionChanged;
        OnPropertyChanged(nameof(EditCountText));
        OnPropertyChanged(nameof(BaseText));
    }

    private void OnSessionChanged(object? sender, EditKey key)
    {
        MarkDirty();
        OnPropertyChanged(nameof(EditCountText));
        var list = key.Table == GameTables.Moves ? allMoves : allSpecies;
        list.FirstOrDefault(e => e.Id == key.Id)?.Refresh();
    }

    public void MarkDirty() => IsDirty = true;

    // Filtrar regenera la lista y el ListBox pierde la selección: se conserva la ficha abierta
    // y se vuelve a seleccionar su entrada si sigue visible.
    partial void OnSpeciesFilterChanged(string value)
    {
        var keep = SelectedSpecies;
        ApplyFilter(Species, allSpecies, value);
        if (keep is not null && Species.Contains(keep))
            SelectedSpecies = keep;
    }

    partial void OnMoveFilterChanged(string value)
    {
        var keep = SelectedMove;
        ApplyFilter(Moves, allMoves, value);
        if (keep is not null && Moves.Contains(keep))
            SelectedMove = keep;
    }

    partial void OnSelectedSpeciesChanged(ListEntryViewModel? value)
    {
        if (value is not null && value.Id != SpeciesDetail?.Id)
            SpeciesDetail = new SpeciesDetailViewModel(Session, Names, value.Id);
    }

    partial void OnSelectedMoveChanged(ListEntryViewModel? value)
    {
        if (value is not null && value.Id != MoveDetail?.Id)
            MoveDetail = new MoveDetailViewModel(Session, Names, value.Id);
    }

    private static void ApplyFilter(ObservableCollection<ListEntryViewModel> target, List<ListEntryViewModel> all, string filter)
    {
        filter = filter.Trim();
        target.Clear();
        foreach (var entry in all.Where(e => e.Matches(filter)))
            target.Add(entry);
    }

    [RelayCommand]
    private void Save()
    {
        try
        {
            Session.Project.Save(ProjectPath);
            IsDirty = false;
            SetStatus($"Proyecto guardado en {ProjectPath}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus($"No se pudo guardar: {ex.Message}", error: true);
        }
    }

    private bool CanInstall() => !IsBusy;

    /// <summary>
    /// Guarda, randomiza si hace falta (o usa la caché), recarga la base del editor si ha cambiado y
    /// escribe en el emulador la salida del randomizer con las ediciones manuales encima.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task Install()
    {
        if (Session.Project.EmulatorUserDirectory is null && !await ChooseEmulatorFolder())
            return;

        IsBusy = true;
        try
        {
            Save();
            if (StatusIsError)
                return;

            UprResult? random = null;
            var settings = Session.Project.Randomization;
            if (settings.Enabled)
            {
                if (!settings.IsReady)
                {
                    SetStatus("Para randomizar hace falta importar un preset y tener una semilla.", error: true);
                    return;
                }
                if (upr.Tools is not { } tools)
                {
                    SetStatus($"No se puede ejecutar UPR ZX: {upr.StatusText}.", error: true);
                    return;
                }
                if (Session.Project.ResolveRomFile() is not { } rom)
                {
                    SetStatus("No se encuentra la ROM (.3ds) para randomizar. Elígela en la pestaña Randomizer.", error: true);
                    return;
                }

                var progress = new Progress<string>(line => SetStatus(line));
                random = await upr.Cache.GetOrCreateAsync(new UprRunner(tools), tools, rom, settings.Preset!, settings.Seed, progress);
            }

            ReloadBaseIfNeeded(random is null ? null : Path.Combine(random.TitleDirectory, "romfs"));

            string modDir = EmulatorUserFolders.ModDirectory(Session.Project.EmulatorUserDirectory!, Dump.Title.TitleIdHex());
            var result = await Task.Run(() => ModInstaller.Install(modDir, Session.Project.DumpDirectory, random?.TitleDirectory, ModBuilder.BuildEdits(Session)));

            settings.InstalledSeed = random?.Seed;
            Save();
            Randomizer.OnInstalled(random);

            string what = random is null
                ? $"{result.Written.Count} archivo(s) de ediciones manuales"
                : $"randomización (semilla {random.Seed}) + {Session.Project.Edits.Count} edición(es) manual(es), {result.Written.Count} archivos";
            string removed = result.Removed.Count == 0 ? "" : $" · {result.Removed.Count} archivo(s) antiguo(s) eliminado(s)";
            string warnings = random is { Warnings.Count: > 0 } ? " Avisos de UPR: " + string.Join(" ", random.Warnings) : "";
            SetStatus($"Instalado en {result.ModDirectory}: {what}{removed}. Reinicia el juego en el emulador.{warnings}");
        }
        catch (UprException ex)
        {
            SetStatus($"{ex.Message} {LastLines(ex.Output, 3)}", error: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException)
        {
            SetStatus($"No se pudo instalar: {ex.Message}", error: true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Si la base (random o no, y qué semilla) ya no coincide, reabre la sesión conservando las ediciones.</summary>
    private void ReloadBaseIfNeeded(string? randomizedRomFs)
    {
        string? current = Session.Layers.Roots.Count > 1 ? Session.Layers.Roots[0] : null;
        string? wanted = randomizedRomFs is null ? null : Path.GetFullPath(randomizedRomFs);
        if (string.Equals(current, wanted, StringComparison.OrdinalIgnoreCase))
            return;

        Session.Changed -= OnSessionChanged;
        Session = EditorSession.Open(Session.Project, wanted);
        Names = new GameNames(Dump, Session.Original);
        LoadLists();
    }

    private static string LastLines(string text, int count) =>
        string.Join(" ", text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).TakeLast(count));

    [RelayCommand]
    private async Task ChangeEmulatorFolder() => await ChooseEmulatorFolder();

    private async Task<bool> ChooseEmulatorFolder()
    {
        string? start = Session.Project.EmulatorUserDirectory ?? EmulatorUserFolders.Detect().FirstOrDefault()?.Path;
        if (await dialogs.PickFolderAsync("Carpeta de usuario del emulador (contiene load)", start) is not { } path)
            return false;
        Session.Project.EmulatorUserDirectory = path;
        MarkDirty();
        OnPropertyChanged(nameof(EmulatorText));
        return true;
    }

    [RelayCommand]
    private async Task OpenOtherProject()
    {
        if (await dialogs.PickOpenFileAsync("Abrir proyecto") is { } path && main.OpenProject(path) is { } error)
            SetStatus(error, error: true);
    }

    [RelayCommand]
    private void NewProject() => main.ShowWelcome();

    public void SetStatus(string text, bool error = false)
    {
        Status = text;
        StatusIsError = error;
    }
}
