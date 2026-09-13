using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pokemanager.App.Services;
using Pokemanager.Bridge;
using Pokemanager.Model.Build;
using Pokemanager.Model.Data;
using Pokemanager.Model.Dump;
using Pokemanager.Model.Editing;
using Pokemanager.Model.Edits;

namespace Pokemanager.App.ViewModels;

public partial class EditorViewModel : ObservableObject
{
    private readonly MainWindowViewModel main;
    private readonly IDialogs dialogs;
    private readonly List<ListEntryViewModel> allSpecies;
    private readonly List<ListEntryViewModel> allMoves;

    public string ProjectPath { get; }
    public GameDump Dump { get; }
    public EditorSession Session { get; }
    public GameNames Names { get; }

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

    public string GameText => $"Pokémon {Dump.Title} · {Dump.Title.TitleIdHex()} · {Session.Project.DumpDirectory}";
    public string EditCountText => Session.Project.Edits.Count == 1 ? "1 cambio" : $"{Session.Project.Edits.Count} cambios";
    public string DirtyText => IsDirty ? "sin guardar" : "guardado";
    public string EmulatorText => Session.Project.EmulatorUserDirectory is { } dir
        ? EmulatorUserFolders.ModDirectory(dir, Dump.Title.TitleIdHex())
        : "Sin carpeta de emulador";

    public EditorViewModel(MainWindowViewModel main, IDialogs dialogs, string projectPath, GameDump dump, EditorSession session)
    {
        this.main = main;
        this.dialogs = dialogs;
        ProjectPath = projectPath;
        Dump = dump;
        Session = session;
        Names = new GameNames(dump, session.Original);

        string[] personalTables = [GameTables.Personal, GameTables.Learnsets];
        allSpecies = Enumerable.Range(0, session.Current.Personal.Length)
            .Skip(1) // la entrada 0 es un marcador vacío
            .Select(i => new ListEntryViewModel(session, personalTables, i, Names.PersonalEntries[i]))
            .ToList();
        allMoves = Enumerable.Range(1, session.Current.Moves.Length - 1)
            .Select(i => new ListEntryViewModel(session, [GameTables.Moves], i, Names.Moves[i]))
            .ToList();

        ApplyFilter(Species, allSpecies, "");
        ApplyFilter(Moves, allMoves, "");
        SelectedSpecies = Species.FirstOrDefault();
        SelectedMove = Moves.FirstOrDefault();

        session.Changed += OnSessionChanged;
    }

    private void OnSessionChanged(object? sender, EditKey key)
    {
        IsDirty = true;
        OnPropertyChanged(nameof(EditCountText));
        var list = key.Table == GameTables.Moves ? allMoves : allSpecies;
        list.FirstOrDefault(e => e.Id == key.Id)?.Refresh();
    }

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

    [RelayCommand]
    private async Task BuildMod()
    {
        if (Session.Project.EmulatorUserDirectory is null && !await ChooseEmulatorFolder())
            return;

        Save();
        if (StatusIsError)
            return;

        try
        {
            string modDir = EmulatorUserFolders.ModDirectory(Session.Project.EmulatorUserDirectory!, Dump.Title.TitleIdHex());
            var result = ModBuilder.Build(Session, modDir);
            string written = result.Written.Count == 0 ? "sin cambios que escribir" : string.Join(", ", result.Written);
            string removed = result.Removed.Count == 0 ? "" : $" · eliminados: {string.Join(", ", result.Removed)}";
            SetStatus($"Mod generado en {result.ModDirectory}: {written}{removed}. Reinicia el juego en el emulador para verlo.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            SetStatus($"No se pudo generar el mod: {ex.Message}", error: true);
        }
    }

    [RelayCommand]
    private async Task ChangeEmulatorFolder() => await ChooseEmulatorFolder();

    private async Task<bool> ChooseEmulatorFolder()
    {
        string? start = Session.Project.EmulatorUserDirectory ?? EmulatorUserFolders.Detect().FirstOrDefault()?.Path;
        if (await dialogs.PickFolderAsync("Carpeta de usuario del emulador (contiene load)", start) is not { } path)
            return false;
        Session.Project.EmulatorUserDirectory = path;
        IsDirty = true;
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

    private void SetStatus(string text, bool error = false)
    {
        Status = text;
        StatusIsError = error;
    }
}
