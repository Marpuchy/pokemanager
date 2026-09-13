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
using Pokemanager.Save;

namespace Pokemanager.App.ViewModels;

public partial class EditorViewModel : ObservableObject
{
    private readonly MainWindowViewModel main;
    private readonly IDialogs dialogs;
    private readonly UprService upr;
    private readonly AppSettings settings;
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
    [NotifyCanExecuteChangedFor(nameof(BuildRomCommand))]
    public partial bool IsBusy { get; set; }

    public string GameText => $"Pokémon {Dump.Title} · {Dump.Title.TitleIdHex()} · {Session.Project.DumpDirectory}";
    public string EditCountText => Session.Project.Edits.Count == 1 ? "1 retoque avanzado" : $"{Session.Project.Edits.Count} retoques avanzados";
    public string DirtyText => IsDirty ? "sin guardar" : "guardado";
    public string EmulatorText => $"Emulador: {settings.EffectiveEmulatorName}";

    /// <summary>Qué base usa el editor avanzado: el juego original o el random de una semilla.</summary>
    public string BaseText => Session.Layers.Roots.Count > 1
        ? $"Base: randomización con semilla {Session.Project.Randomization.Seed}"
        : "Base: juego original";

    public EditorViewModel(MainWindowViewModel main, IDialogs dialogs, UprService upr, AppSettings settings,
        string projectPath, GameDump dump, EditorSession session)
    {
        this.main = main;
        this.dialogs = dialogs;
        this.upr = upr;
        this.settings = settings;
        ProjectPath = projectPath;
        Dump = dump;
        Session = session;
        Names = new GameNames(dump, session.Original);
        LoadLists();
        Randomizer = new RandomizerViewModel(this, dialogs, upr, settings);
    }

    // ------------------------------------------------------------------ editor avanzado

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

    // ------------------------------------------------------------------ guardar

    [RelayCommand]
    private async Task Save() => await SaveAsync();

    /// <summary>Escribe las opciones del randomizer al preset y guarda el proyecto. Devuelve false si falló.</summary>
    private async Task<bool> SaveAsync()
    {
        try
        {
            await Randomizer.CommitOptionsAsync();
            Session.Project.Save(ProjectPath);
            IsDirty = false;
            SetStatus($"Proyecto guardado en {ProjectPath}");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or UprException)
        {
            SetStatus($"No se pudo guardar: {ex.Message}", error: true);
            return false;
        }
    }

    // ------------------------------------------------------------------ crear la ROM

    private bool CanBuild() => !IsBusy;

    /// <summary>
    /// Crea la ROM (.cxi) junto a la ROM base con el random y los retoques, retira mods antiguos de la app en el
    /// emulador y, si se ha pedido, adapta la partida a la nueva ROM.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanBuild))]
    private async Task BuildRom()
    {
        var project = Session.Project;
        var r = project.Randomization;

        if (upr.Tools is not { } tools) { SetStatus(upr.StatusText, error: true); return; }
        if (project.ResolveRomFile() is not { } baseRom) { SetStatus(Randomizer.BaseRomText, error: true); return; }
        if (RomBuilder.ValidateName(r.OutputName) is { } nameError) { SetStatus(nameError, error: true); return; }
        if (r.Enabled && !r.IsReady) { SetStatus("Falta una semilla válida.", error: true); return; }

        string? savePath = r.UpdateSave ? Randomizer.SavePath : null;
        if (savePath is not null && File.Exists(savePath) && EmulatorUserFolders.RunningEmulators() is { Count: > 0 } running)
        {
            SetStatus($"Cierra {string.Join(", ", running)} antes de crear la ROM: si el emulador está abierto, al salir sobrescribiría la partida actualizada.", error: true);
            return;
        }

        IsBusy = true;
        try
        {
            if (!await SaveAsync())
                return;

            var progress = new Progress<string>(line => SetStatus(line));

            UprResult? random = null;
            if (r.Enabled)
                random = await upr.Cache.GetOrCreateAsync(new UprRunner(tools), tools, baseRom, r.Preset!, r.Seed, progress);

            ReloadBaseIfNeeded(random is null ? null : Path.Combine(random.TitleDirectory, "romfs"));

            string output = RomBuilder.OutputPath(baseRom, r.OutputName!);
            var built = await RomBuilder.BuildAsync(new UprRunner(tools), baseRom, random, ModBuilder.BuildEdits(Session),
                output, r.Seed, upr.Cache.Root, progress);

            // Los mods LayeredFS que instalaban versiones anteriores de la app se aplican a cualquier ROM del juego: fuera.
            int removedMods = 0;
            foreach (var emulator in EmulatorUserFolders.Detect())
                removedMods += await Task.Run(() => ModInstaller.Uninstall(EmulatorUserFolders.ModDirectory(emulator.Path, Dump.Title.TitleIdHex())).Count);

            SaveUpdateResult? saveResult = null;
            if (savePath is not null && File.Exists(savePath))
            {
                SetStatus("Adaptando la partida a la nueva ROM…");
                string backups = Path.Combine(AppSettings.BackupRoot, settings.EffectiveEmulatorName);
                saveResult = await Task.Run(() => SaveUpdater.Apply(savePath, Session.Current, backups));
            }

            r.InstalledSeed = random?.Seed;
            await SaveAsync();
            Randomizer.OnBuilt(random, saveResult);

            string what = random is null ? "sin randomizar" : $"semilla {random.Seed}";
            string mods = removedMods > 0 ? $" Se retiraron {removedMods} archivo(s) de un mod anterior de la carpeta de mods." : "";
            string save = saveResult is null ? "" : saveResult.Changes.Count == 0
                ? " Partida revisada: no hacía falta cambiar nada."
                : $" Partida actualizada ({saveResult.Changes.Count} cambios) y verificada.";
            SetStatus($"ROM creada ({what}): {built.RomPath}.{save}{mods} Ábrela en {settings.EffectiveEmulatorName}.");
        }
        catch (UprException ex)
        {
            SetStatus($"{ex.Message} {LastLines(ex.Output, 3)}", error: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                                       or InvalidDataException or SaveUpdateException)
        {
            SetStatus($"No se pudo crear la ROM: {ex.Message}", error: true);
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

    // ------------------------------------------------------------------ otros

    [RelayCommand]
    private async Task OpenSettings()
    {
        await dialogs.ShowSettingsAsync(new SettingsViewModel(settings, upr, dialogs, Session.Project, Dump.Title));
        OnPropertyChanged(nameof(EmulatorText));
        Randomizer.RefreshSave();
        MarkDirty(); // la ROM base del proyecto puede haber cambiado
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
