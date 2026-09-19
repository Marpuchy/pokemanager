using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pokemanager.App.Resources;
using Pokemanager.App.Services;
using Pokemanager.Bridge;
using Pokemanager.Model.Build;
using Pokemanager.Model.Data;
using Pokemanager.Model.Dump;
using Pokemanager.Model.Editing;
using Pokemanager.Model.Edits;
using Pokemanager.Model.Projects;
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
    private List<ListEntryViewModel> allTrainers = [];
    private List<TrainerCategoryViewModel> allCategories = [];

    public string ProjectPath { get; }
    public GameDump Dump { get; }
    public RandomizerViewModel Randomizer { get; }
    public HistoryViewModel History { get; }
    public SaveEditorViewModel SaveEditor { get; }

    /// <summary>The player's profile picture (multiplayer profile), shown on the save's trainer card.</summary>
    public AvatarViewModel ProfileAvatar => main.Room.Avatar;
    public LockeViewModel Locke { get; }
    public IDialogs Dialogs => dialogs;

    /// <summary>Pokémon icons from the dump.</summary>
    public PokemonSprites Sprites { get; }

    /// <summary>Pokémon of the save to show when the view appears (opened from the project preview).</summary>
    public SaveSlot? PendingSaveSlot { get; set; }

    public void ShowSavePokemon(SaveSlot slot) => PendingSaveSlot = slot;

    [ObservableProperty]
    public partial EditorSession Session { get; private set; }

    [ObservableProperty]
    public partial GameNames Names { get; private set; }

    public ObservableCollection<ListEntryViewModel> Species { get; } = [];
    public ObservableCollection<ListEntryViewModel> Moves { get; } = [];
    public ObservableCollection<ListEntryViewModel> Trainers { get; } = [];

    /// <summary>The game's trainer classes, plus "every trainer": the groups the by-category mode edits.</summary>
    public ObservableCollection<TrainerCategoryViewModel> TrainerCategories { get; } = [];

    /// <summary>
    /// The tab opens on the categories: changing the AI or the IVs of a whole class at once is what a run needs, and
    /// the list of 785 trainers one by one is the other mode.
    /// </summary>
    [ObservableProperty]
    public partial bool TrainersByCategory { get; set; } = true;

    /// <summary>
    /// Teams start hidden (like the abilities of the Pokémon tab): someone looking at what a trainer's AI does should
    /// not have the whole team spoiled at them. Not persisted: off again on every open.
    /// </summary>
    [ObservableProperty]
    public partial bool ShowTrainerTeam { get; set; }

    public TrainerBulkViewModel TrainerBulk { get; private set; } = null!;

    /// <summary>Advanced: Game — what the built ROM does differently, beyond a single Pokémon or trainer.</summary>
    public GameTweaksViewModel GameTweaks { get; }

    [ObservableProperty]
    public partial string SpeciesFilter { get; set; } = "";

    /// <summary>
    /// Advanced: Pokémon shows the abilities only on request, off every time a project opens: players checking the
    /// changed stats of a randomized ROM may not want to see them (spoilers).
    /// </summary>
    [ObservableProperty]
    public partial bool ShowAbilities { get; set; }

    [RelayCommand]
    private void RevealAbilities() => ShowAbilities = true;

    [RelayCommand]
    private void RevealTrainerTeam() => ShowTrainerTeam = true;

    [RelayCommand]
    private void HideAbilities() => ShowAbilities = false;

    [ObservableProperty]
    public partial string MoveFilter { get; set; } = "";

    [ObservableProperty]
    public partial string TrainerFilter { get; set; } = "";

    [ObservableProperty]
    public partial ListEntryViewModel? SelectedSpecies { get; set; }

    [ObservableProperty]
    public partial ListEntryViewModel? SelectedMove { get; set; }

    [ObservableProperty]
    public partial ListEntryViewModel? SelectedTrainer { get; set; }

    [ObservableProperty]
    public partial TrainerCategoryViewModel? SelectedTrainerCategory { get; set; }

    [ObservableProperty]
    public partial SpeciesDetailViewModel? SpeciesDetail { get; set; }

    [ObservableProperty]
    public partial MoveDetailViewModel? MoveDetail { get; set; }

    [ObservableProperty]
    public partial TrainerDetailViewModel? TrainerDetail { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DirtyText))]
    public partial bool IsDirty { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; } = "";

    [ObservableProperty]
    public partial bool StatusIsError { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BuildRomCommand), nameof(AdaptSaveCommand), nameof(PlayCommand))]
    public partial bool IsBusy { get; set; }

    public string GameText => string.Format(Strings.Editor_GameText, Dump.Title.DisplayName(), Dump.Title.TitleIdHex(), Session.Project.DumpDirectory);
    public string EditCountText => Session.Project.Edits.Count == 1 ? Strings.Editor_EditCountOne : string.Format(Strings.Editor_EditCount, Session.Project.Edits.Count);
    public string DirtyText => IsDirty ? Strings.Editor_Unsaved : Strings.Editor_Saved;
    /// <summary>The application settings, for the pages that need the emulator folder or the backups.</summary>
    public AppSettings Settings => settings;

    public string EmulatorText => string.Format(Strings.Editor_Emulator, settings.EffectiveEmulatorName);

    /// <summary>Which base the advanced editor uses: the original game or a seed's randomization.</summary>
    public string BaseText => Session.Layers.Roots.Count > 1
        ? string.Format(Strings.Editor_BaseRandom, Session.Project.Randomization.Seed)
        : Strings.Editor_BaseOriginal;

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
        Sprites = PokemonSprites.Load(session.Project, session.Original);
        LoadLists();

        // Projects without a fixed base ROM get it now, before a built ROM can be mistaken for it.
        if (session.Project.RomFile is null && Project.FindBaseRom(session.Project.DumpDirectory) is { } baseRom)
        {
            session.Project.RomFile = baseRom;
            try { session.Project.Save(projectPath); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { IsDirty = true; }
        }

        Randomizer = new RandomizerViewModel(this, dialogs, upr, settings);
        History = new HistoryViewModel(this, dialogs, projectPath);
        SaveEditor = new SaveEditorViewModel(this, settings);
        Locke = new LockeViewModel(this);
        GameTweaks = new GameTweaksViewModel(this, session);

        ProjectUndo = new SnapshotHistory(() => Session.Project.CaptureState(), RestoreProjectState);
        ProjectUndo.Changed += NotifyUndo;
        ProjectUndo.Reset();
        SaveEditor.Undo.Changed += NotifyUndo;

        // Projects built before the history existed: keep what is being played as the first version, so there is
        // something to go back to before the next build.
        var r = session.Project.Randomization;
        if (History.IsEmpty && r.LastBuiltRom is { } rom && File.Exists(rom) && (!r.Enabled || r.InstalledSeed == r.Seed))
            RecordVersion(VersionKind.Built, rom);

        ready = true;
        RefreshRomNotice();
    }

    /// <summary>
    /// The project no longer matches the last built ROM (unsaved or unbuilt changes), so data taken from the project may
    /// not be what the game uses.
    /// </summary>
    public bool ProjectDiffersFromBuiltRom
    {
        get
        {
            var r = Session.Project.Randomization;
            if (romDirty || Randomizer.Options.HasChanges || (r.Enabled && r.InstalledSeed != r.Seed))
                return true;
            return History.History.List().FirstOrDefault(v => v.Kind == VersionKind.Built) is { } built
                   && built.Fingerprint != ProjectHistory.Fingerprint(Session.Project);
        }
    }

    // ------------------------------------------------------------------ advanced editor

    private void LoadLists()
    {
        string[] personalTables = [GameTables.Personal, GameTables.Learnsets];
        allSpecies = Enumerable.Range(1, Session.Current.Personal.Length - 1) // entry 0 is an empty placeholder
            .Select(i => new ListEntryViewModel(Session, personalTables, i, Names.PersonalEntries[i], () => Sprites.ForPersonalEntry(i),
                () => (Session.Current.Personal[i].Types[0], Session.Current.Personal[i].Types[1])))
            .ToList();
        string[] moveTables = [GameTables.Moves, GameTables.MoveTexts];
        allMoves = Enumerable.Range(1, Session.Current.Moves.Length - 1)
            .Select(i => new ListEntryViewModel(Session, moveTables, i, Names.Moves[i], types: () => (Session.Current.Moves[i].Type, Session.Current.Moves[i].Type), moveCategory: () => Session.Current.Moves[i].Category))
            .ToList();

        string[] trainerTables = [GameTables.Trainers];
        allTrainers = Enumerable.Range(0, Session.Current.Trainers.Length)
            .Select(i => new ListEntryViewModel(Session, trainerTables, i, "",
                // The part they play goes in the name: a ROM with randomized names is a list of strangers otherwise,
                // and this is what lets "rival" or "leader" be typed in the search box.
                liveName: () => TrainerName(i),
                strip: () => TrainerDetailViewModel.AiColor(Session.GetInt(GameTables.Trainers, i, "ai"))))
            .ToList();

        TrainerBulk ??= new TrainerBulkViewModel(this, Session);
        allCategories =
        [
            new TrainerCategoryViewModel(Session, Strings.Trainer_CategoryAll, [.. Enumerable.Range(0, Session.Current.Trainers.Length)]),
            // By how hard they are meant to be, which is what a difficulty change is usually about.
            .. Enum.GetValues<TrainerDifficulty>()
                .OrderByDescending(d => d)
                .Select(d => new TrainerCategoryViewModel(Session, TrainerRoleNames.Of(d),
                    [.. Enumerable.Range(0, Session.Current.Trainers.Length).Where(i => TrainerRoles.DifficultyOf(Session.Current.Title, i) == d)],
                    Tip(d)))
                .Where(c => c.Trainers.Count > 0),
            .. Enumerable.Range(0, Session.Current.Trainers.Length)
                .GroupBy(i => Session.GetInt(GameTables.Trainers, i, "class"))
                .Select(g => new TrainerCategoryViewModel(Session, ClassLabel(g.Key), [.. g]))
                .OrderByDescending(c => c.Trainers.Count)
                .ThenBy(c => c.Name, StringComparer.CurrentCulture),
        ];

        int? species = SelectedSpecies?.Id, move = SelectedMove?.Id, trainer = SelectedTrainer?.Id;
        SpeciesDetail = null;
        MoveDetail = null;
        TrainerDetail = null;
        ApplyFilter(Species, allSpecies, SpeciesFilter);
        ApplyFilter(Moves, allMoves, MoveFilter);
        ApplyFilter(Trainers, allTrainers, TrainerFilter);
        SelectedSpecies = Species.FirstOrDefault(s => s.Id == species) ?? Species.FirstOrDefault();
        SelectedMove = Moves.FirstOrDefault(m => m.Id == move) ?? Moves.FirstOrDefault();
        SelectedTrainer = Trainers.FirstOrDefault(t => t.Id == trainer) ?? Trainers.FirstOrDefault();
        string? category = SelectedTrainerCategory?.Name;
        TrainerCategories.Clear();
        foreach (var c in allCategories.Where(c => c.Matches(TrainerFilter)))
            TrainerCategories.Add(c);
        SelectedTrainerCategory = TrainerCategories.FirstOrDefault(c => c.Name == category) ?? TrainerCategories.FirstOrDefault();

        Session.Changed += OnSessionChanged;
        OnPropertyChanged(nameof(EditCountText));
        OnPropertyChanged(nameof(BaseText));
        RefreshRomNotice();
    }

    private void OnSessionChanged(object? sender, EditKey key)
    {
        bool move = key.Table is GameTables.Moves or GameTables.MoveTexts;
        bool trainer = key.Table is GameTables.Trainers;
        string name = trainer
            ? Names.TrainerLabel(key.Id, Session.GetInt(GameTables.Trainers, key.Id, "class"))
            : move
                ? key.Id < Names.Moves.Count ? Names.Moves[key.Id] : $"#{key.Id}"
                : key.Id < Names.PersonalEntries.Count ? Names.PersonalEntries[key.Id] : $"#{key.Id}";
        MarkDirty($"{name} · {key.Field}");
        OnPropertyChanged(nameof(EditCountText));
        var list = trainer ? allTrainers : move ? allMoves : allSpecies;
        list.FirstOrDefault(e => e.Id == key.Id)?.Refresh();
        if (trainer && !inBatch)
            SelectedTrainerCategory?.Refresh();
    }

    /// <summary>The project changed: unsaved, and a step for undo (the label says what, for the Undo button).</summary>
    public void MarkDirty() => MarkDirty(Strings.Undo_ProjectChange);

    /// <param name="affectsRom">
    /// False for what the game never sees (the Locke rules: lives, roulette prizes and spins). Those are saved with the
    /// project but do not ask for a new ROM.
    /// </param>
    public void MarkDirty(string label, bool affectsRom = true)
    {
        IsDirty = true;
        romDirty |= affectsRom;
        if (!restoringProject && !inBatch)
            ProjectUndo?.Record(label);
        RefreshRomNotice();
    }

    private bool inBatch;

    /// <summary>
    /// Groups everything done inside it into a single undo step: applying a change to a whole category of trainers is
    /// one thing the user did, not two hundred.
    /// </summary>
    public IDisposable BeginBatch(string label) => new Batch(this, label);

    private sealed class Batch : IDisposable
    {
        private readonly EditorViewModel editor;
        private readonly string label;
        private readonly bool outer;

        public Batch(EditorViewModel editor, string label)
        {
            this.editor = editor;
            this.label = label;
            outer = !editor.inBatch;
            editor.inBatch = true;
        }

        public void Dispose()
        {
            if (!outer)
                return;
            editor.inBatch = false;
            if (!editor.restoringProject)
                editor.ProjectUndo?.Record(label);
            editor.RefreshRomNotice();
        }
    }

    // ------------------------------------------------------------------ "the ROM does not have these changes yet"

    /// <summary>
    /// Stats, moves, learnsets and randomizer options only reach the game when the ROM is built, so the editor says so
    /// as soon as something changes and until "Build ROM" runs.
    /// </summary>
    public bool NeedsRomBuild => ProjectDiffersFromBuiltRom;

    public string NeedsRomBuildText => Session.Project.Randomization.LastBuiltRom is { } rom && File.Exists(rom)
        ? string.Format(Strings.Editor_RomOutdated, Path.GetFileName(rom))
        : Strings.Editor_RomNeverBuilt;

    /// <summary>After anything that can change whether the built ROM matches the project (not while still being built).</summary>
    public void RefreshRomNotice()
    {
        if (!ready)
            return;
        OnPropertyChanged(nameof(NeedsRomBuild));
        OnPropertyChanged(nameof(NeedsRomBuildText));
        SaveEditor.RefreshRomWarning();
    }

    /// <summary>False while the constructor runs: the pages it notifies do not exist yet.</summary>
    private bool ready;

    /// <summary>Something the ROM carries (stats, moves, learnsets, randomization) changed since it was built.</summary>
    private bool romDirty;

    // ------------------------------------------------------------------ undo / redo

    private bool restoringProject;

    /// <summary>Undo of the project: advanced edits, imports, randomizer settings and Locke rules.</summary>
    public SnapshotHistory ProjectUndo { get; private set; } = null!;

    /// <summary>Index of the main tab shown: the Save tab (1) undoes save edits, the others the project.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UndoText), nameof(RedoText), nameof(UndoTip), nameof(RedoTip))]
    [NotifyCanExecuteChangedFor(nameof(UndoCommand), nameof(RedoCommand))]
    public partial int SelectedTab { get; set; }

    private const int SaveTabIndex = 1;

    private SnapshotHistory ActiveUndo => SelectedTab == SaveTabIndex ? SaveEditor.Undo : ProjectUndo;

    public string UndoText => ActiveUndo.UndoCount > 0 ? string.Format(Strings.Undo_Button, ActiveUndo.UndoCount) : Strings.Undo_ButtonNone;
    public string RedoText => ActiveUndo.RedoCount > 0 ? string.Format(Strings.Redo_Button, ActiveUndo.RedoCount) : Strings.Redo_ButtonNone;
    public string UndoTip => ActiveUndo.UndoLabel is { } label ? string.Format(Strings.Undo_Tip, label) : Strings.Undo_Nothing;
    public string RedoTip => ActiveUndo.RedoLabel is { } label ? string.Format(Strings.Redo_Tip, label) : Strings.Redo_Nothing;

    private void NotifyUndo()
    {
        OnPropertyChanged(nameof(UndoText));
        OnPropertyChanged(nameof(RedoText));
        OnPropertyChanged(nameof(UndoTip));
        OnPropertyChanged(nameof(RedoTip));
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    private bool CanUndo() => ActiveUndo.CanUndo;
    private bool CanRedo() => ActiveUndo.CanRedo;

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        if (ActiveUndo.Undo() is { } label)
            SetStatus(string.Format(Strings.Undo_Done, label));
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        if (ActiveUndo.Redo() is { } label)
            SetStatus(string.Format(Strings.Redo_Done, label));
    }

    /// <summary>Puts a captured project state back: edits through the session (so every view follows), then the settings.</summary>
    private void RestoreProjectState(byte[] state)
    {
        var (edits, randomization, locke, shops, tweaks) = Project.ReadState(state);
        restoringProject = true;
        Session.Changed -= OnSessionChanged;
        try
        {
            var target = edits.ToDictionary(e => e.Key);
            foreach (var edit in Session.Project.Edits.All.ToList())
                if (!target.ContainsKey(edit.Key))
                    Session.Set(edit.Table, edit.Id, edit.Field, Session.GetOriginal(edit.Table, edit.Id, edit.Field));
            foreach (var edit in edits)
                if (!System.Text.Json.Nodes.JsonNode.DeepEquals(Session.Get(edit.Table, edit.Id, edit.Field), edit.Value))
                    Session.Set(edit.Table, edit.Id, edit.Field, edit.Value);

            // What a build recorded (the ROM written, its seed) is a fact, not something to undo.
            var r = Session.Project.Randomization;
            (string? lastBuilt, long? installed) = (r.LastBuiltRom, r.InstalledSeed);
            r.CopyFrom(randomization);
            (r.LastBuiltRom, r.InstalledSeed) = (lastBuilt, installed);
            Session.Project.Locke = locke;
            Session.Project.Shops = shops;
            Session.Project.Tweaks = tweaks;
            Randomizer?.Shops.Refresh();
            GameTweaks?.Refresh();

            foreach (var entry in allSpecies.Concat(allMoves).Concat(allTrainers))
                entry.Refresh();
            if (SpeciesDetail is { } species)
                SpeciesDetail = new SpeciesDetailViewModel(Session, Names, species.Id, Sprites.ForPersonalEntry(species.Id));
            if (MoveDetail is { } move)
                MoveDetail = new MoveDetailViewModel(Session, Names, move.Id);
            if (TrainerDetail is { } trainer)
                TrainerDetail = new TrainerDetailViewModel(this, Session, Names, Sprites, trainer.Id);
            Randomizer.ReloadFromProject();
            Locke.Refresh();
            IsDirty = true;
            OnPropertyChanged(nameof(EditCountText));
        }
        finally
        {
            Session.Changed += OnSessionChanged;
            restoringProject = false;
        }
    }

    // Filtering rebuilds the list and the ListBox loses its selection: keep the open detail and reselect its entry
    // if it is still visible.
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

    partial void OnTrainerFilterChanged(string value)
    {
        var keep = SelectedTrainer;
        ApplyFilter(Trainers, allTrainers, value);
        if (keep is not null && Trainers.Contains(keep))
            SelectedTrainer = keep;

        var keepCategory = SelectedTrainerCategory;
        TrainerCategories.Clear();
        foreach (var c in allCategories.Where(c => c.Matches(value)))
            TrainerCategories.Add(c);
        SelectedTrainerCategory = keepCategory is not null && TrainerCategories.Contains(keepCategory)
            ? keepCategory
            : TrainerCategories.FirstOrDefault();
    }

    partial void OnSelectedTrainerCategoryChanged(TrainerCategoryViewModel? value) => TrainerBulk.Category = value;

    /// <summary>After a bulk change: every row and the open trainer show the new values.</summary>
    /// <summary>"Gym leader 3 - Korrina", or just the name when the trainer has no part of their own.</summary>
    private string TrainerName(int trainer)
    {
        string label = Names.TrainerLabel(trainer, Session.GetInt(GameTables.Trainers, trainer, "class"));
        return TrainerRoleNames.Of(TrainerRoles.TagOf(Session.Current.Title, trainer), Session.Current.Title.Generation() == 7) is { } role
            ? string.Format(Strings.Trainer_WithRole, role, label)
            : label;
    }

    /// <summary>What a difficulty group means, on hover.</summary>
    private static string Tip(TrainerDifficulty difficulty) => difficulty switch
    {
        TrainerDifficulty.Boss => Strings.Role_BossesTip,
        TrainerDifficulty.Important => Strings.Role_ImportantTip,
        _ => Strings.Role_OrdinaryTip,
    };

    /// <summary>
    /// The name of a class, with its id when another class shares the name: the games do that on purpose (two "Pokémon
    /// Trainer" classes in X), and a ROM with randomized names does it by chance.
    /// </summary>
    private string ClassLabel(int id)
    {
        string name = Names.TrainerClassName(id);
        if (string.IsNullOrWhiteSpace(name))
            return string.Format(Strings.Trainer_ClassWithId, Strings.Trainer_CategoryNoClass, id);
        bool shared = Names.TrainerClasses.Count(n => n == name) > 1;
        return shared ? string.Format(Strings.Trainer_ClassWithId, name, id) : name;
    }

    /// <summary>The class name of a trainer, which is what groups the categories.</summary>
    private string ClassName(int trainer)
    {
        int id = Session.GetInt(GameTables.Trainers, trainer, "class");
        return id >= 0 && id < Names.TrainerClasses.Count && !string.IsNullOrWhiteSpace(Names.TrainerClasses[id])
            ? Names.TrainerClasses[id]
            : $"#{id}";
    }

    public void RefreshTrainerLists()
    {
        foreach (var entry in allTrainers)
            entry.Refresh();
        foreach (var category in allCategories)
            category.Refresh();
        if (TrainerDetail is { } detail)
            TrainerDetail = new TrainerDetailViewModel(this, Session, Names, Sprites, detail.Id);
    }

    partial void OnSelectedTrainerChanged(ListEntryViewModel? value)
    {
        if (value is not null && value.Id != TrainerDetail?.Id)
            TrainerDetail = new TrainerDetailViewModel(this, Session, Names, Sprites, value.Id);
    }

    partial void OnSelectedSpeciesChanged(ListEntryViewModel? value)
    {
        if (value is not null && value.Id != SpeciesDetail?.Id)
            SpeciesDetail = new SpeciesDetailViewModel(Session, Names, value.Id, Sprites.ForPersonalEntry(value.Id));
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

    // ------------------------------------------------------------------ save

    [RelayCommand]
    private async Task Save() => await SaveAsync();

    /// <summary>Writes the randomizer options to the preset and saves the project. Returns false on failure.</summary>
    private async Task<bool> SaveAsync()
    {
        try
        {
            await Randomizer.CommitOptionsAsync();
            Session.Project.Save(ProjectPath);
            IsDirty = false;
            SetStatus(string.Format(Strings.Status_Saved, ProjectPath));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or UprException)
        {
            SetStatus(string.Format(Strings.Status_SaveFailed, ex.Message), error: true);
            return false;
        }
    }

    // ------------------------------------------------------------------ build the ROM

    private bool CanBuild() => !IsBusy;

    /// <summary>
    /// Builds the ROM (.cxi) next to the base ROM with the randomization and the edits, removes old app mods from the
    /// emulators and, when requested, adapts the save to the new ROM.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanBuild))]
    private Task BuildRom() => BuildRomAsync(confirmRandomizationChange: true);

    /// <param name="confirmRandomizationChange">Ask before changing the randomization of a game in progress.</param>
    private async Task BuildRomAsync(bool confirmRandomizationChange)
    {
        var project = Session.Project;
        var r = project.Randomization;

        if (upr.Tools is not { } tools) { SetStatus(upr.StatusText, error: true); return; }
        if (project.ResolveRomFile() is not { } baseRom) { SetStatus(Randomizer.BaseRomText, error: true); return; }
        if (Randomizer.OutputError is { } nameError) { SetStatus(nameError, error: true); return; }
        if (Randomizer.OutputPath is not { } output) { SetStatus(Strings.Status_NoOutput, error: true); return; }
        if (r.Enabled && !r.IsReady) { SetStatus(Strings.Status_NoSeed, error: true); return; }
        if (SaveEditor.IsDirty) { SetStatus(Strings.Save_UnwrittenChanges, error: true); return; }

        string? savePath = r.UpdateSave ? Randomizer.SavePath : null;
        if (savePath is not null && File.Exists(savePath) && EmulatorBlocking() is { } blocking)
        {
            SetStatus(blocking, error: true);
            return;
        }

        if (confirmRandomizationChange && !await ConfirmRandomizationChangeAsync())
            return;

        IsBusy = true;
        try
        {
            if (!await SaveAsync())
                return;

            var progress = new Progress<string>(line => SetStatus(line));

            UprResult? random = null;
            if (r.Enabled)
                random = await upr.Cache.GetOrCreateAsync(new UprRunner(tools), tools, baseRom, r.Preset!, r.Seed, progress,
                    allItems: r.AllowAllItems, goodItems: r.MegaStonesInPool ? Session.Current.MegaStones : null);

            ReloadBaseIfNeeded(random is null ? null : Path.Combine(random.TitleDirectory, "romfs"));

            var built = await RomBuilder.BuildAsync(new UprRunner(tools), baseRom, random, ModBuilder.BuildEdits(Session, random?.TitleDirectory),
                output, r.Seed, upr.Cache.Root, progress);
            r.LastBuiltRom = built.RomPath;
            if (random is null && File.Exists(built.RomPath + ".log"))
                File.Delete(built.RomPath + ".log"); // log of an earlier randomization that no longer applies

            // Do not pile things up: keep the latest cached randomizations and save backups only.
            upr.Cache.Prune(keep: 3, keepTitleDirectory: random?.TitleDirectory);

            // LayeredFS mods installed by earlier app versions apply to every ROM of the game: remove them.
            int removedMods = 0;
            foreach (var emulator in EmulatorUserFolders.Detect())
                removedMods += await Task.Run(() => ModInstaller.Uninstall(EmulatorUserFolders.ModDirectory(emulator.Path, Dump.Title.TitleIdHex())).Count);

            SaveUpdateResult? saveResult = null;
            if (savePath is not null && File.Exists(savePath))
                saveResult = await ApplySaveUpdateAsync(savePath);

            r.InstalledSeed = random?.Seed;
            await SaveAsync();
            Randomizer.OnBuilt(random, saveResult);
            RecordVersion(VersionKind.Built, built.RomPath);
            romDirty = false;
            RefreshRomNotice();
            SaveEditor.OnRomChanged();

            string what = random is null ? Strings.Status_NotRandomized : string.Format(Strings.Status_Seed, random.Seed);
            string mods = removedMods > 0 ? string.Format(Strings.Status_ModsRemoved, removedMods) : "";
            string save = saveResult is null ? "" : saveResult.Changes.Count == 0
                ? Strings.Status_SaveUnchanged
                : string.Format(Strings.Status_SaveUpdated, saveResult.Changes.Count);
            SetStatus(string.Format(Strings.Status_RomBuilt, what, built.RomPath, save, mods, settings.EffectiveEmulatorName));
        }
        catch (UprException ex)
        {
            SetStatus($"{ex.Message} {LastLines(ex.Output, 3)}", error: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                                       or InvalidDataException or SaveUpdateException)
        {
            SetStatus(string.Format(Strings.Status_BuildFailed, ex.Message), error: true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// When a save exists and the randomization (enabled, seed or preset) differs from the last built ROM, the build
    /// would change the Pokémon of a game in progress: ask first, and remind that History can bring it back.
    /// </summary>
    private async Task<bool> ConfirmRandomizationChangeAsync()
    {
        var r = Session.Project.Randomization;
        if (!Randomizer.HasSave || r.LastBuiltRom is null)
            return true;

        await Randomizer.CommitOptionsAsync();
        bool changed;
        if (History.History.List().FirstOrDefault(v => v.Kind == VersionKind.Built) is { } built)
        {
            try
            {
                var previous = History.History.LoadProject(built).Randomization;
                changed = previous.Enabled != r.Enabled
                          || (r.Enabled && (previous.Seed != r.Seed || !(previous.Preset ?? []).AsSpan().SequenceEqual(r.Preset ?? [])));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException)
            {
                changed = r.Enabled && r.InstalledSeed != r.Seed;
            }
        }
        else
        {
            changed = r.Enabled && r.InstalledSeed is not null && r.InstalledSeed != r.Seed;
        }
        if (!changed)
            return true;

        var question = new QuestionViewModel(Strings.Guard_Title, Strings.Guard_Message, Strings.Guard_Confirm, Strings.Common_Cancel, []);
        return await dialogs.AskAsync(question);
    }

    private void RecordVersion(VersionKind kind, string? romPath)
    {
        try
        {
            History.History.Record(Session.Project, kind, romPath: romPath, savePath: Randomizer.SavePath);
            History.History.Prune();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus(string.Format(Strings.History_Failed, ex.Message), error: true);
        }
        History.Refresh();
    }

    /// <summary>Puts back a version of the history: its randomization and edits, optionally its save, and rebuilds.</summary>
    public async Task RestoreVersionAsync(ProjectVersion version, bool restoreSave, bool rebuild)
    {
        if (IsBusy)
            return;
        if (SaveEditor.IsDirty) { SetStatus(Strings.Save_UnwrittenChanges, error: true); return; }

        Project snapshot;
        try
        {
            snapshot = History.History.LoadProject(version);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException)
        {
            SetStatus(string.Format(Strings.History_Unreadable, ex.Message), error: true);
            return;
        }

        string? savePath = Randomizer.SavePath;
        string? saveCopy = restoreSave ? History.History.SaveFile(version) : null;
        if (restoreSave)
        {
            if (saveCopy is null || savePath is null) { SetStatus(Strings.History_RestoreSaveUnavailable, error: true); return; }
            if (EmulatorBlocking() is { } blocking) { SetStatus(blocking, error: true); return; }
        }

        try
        {
            await Randomizer.CommitOptionsAsync();
            History.History.Record(Session.Project, VersionKind.BeforeRestore, romPath: Session.Project.Randomization.LastBuiltRom, savePath: savePath);

            ProjectHistory.RestoreInto(Session.Project, snapshot);
            ReopenSession();
            await Randomizer.OnProjectReplacedAsync();
            MarkDirty();
            if (!await SaveAsync())
                return;

            string saveNote = "";
            if (saveCopy is not null && savePath is not null)
            {
                string backups = Path.Combine(AppSettings.BackupRoot, settings.EffectiveEmulatorName);
                string backup = await Task.Run(() => SaveDocument.ReplaceFile(savePath, saveCopy, backups));
                saveNote = string.Format(Strings.History_SaveRestored, backup);
            }

            Randomizer.RefreshSave();
            SaveEditor.OnRomChanged();
            History.Refresh();
            SetStatus(string.Format(Strings.History_Restored, version.CreatedAt.ToString("g"), saveNote));

            if (rebuild)
                await BuildRomAsync(confirmRandomizationChange: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SaveUpdateException or UprException or InvalidDataException)
        {
            SetStatus(string.Format(Strings.History_Failed, ex.Message), error: true);
        }
    }

    /// <summary>Reopens the session after the project's edits or randomization were replaced wholesale.</summary>
    private void ReopenSession()
    {
        var project = Session.Project;
        string? randomRomFs = project.Randomization.Enabled && upr.TryGetCached(project, project.Randomization.Preset) is { } cached
            ? Path.Combine(cached.TitleDirectory, "romfs")
            : null;
        Session.Changed -= OnSessionChanged;
        Session = EditorSession.Open(project, randomRomFs);
        Names = new GameNames(Dump, Session.Original);
        LoadLists();
        OnPropertyChanged(nameof(EditCountText));
        RefreshRomNotice();
    }

    // ------------------------------------------------------------------ Pokémon data files

    private string DataDescription()
    {
        var r = Session.Project.Randomization;
        string random = r.Enabled
            ? string.Format(Strings.History_Random, r.Seed, r.PresetName ?? Strings.Rnd_PresetDefaults)
            : Strings.History_NotRandom;
        return $"{Dump.Title.DisplayName()} · {random} · {EditCountText}";
    }

    [RelayCommand]
    private Task ExportPokemonData() => ExportDataAsync(PokemonDataKind.Pokemon);

    [RelayCommand]
    private Task ExportMoveData() => ExportDataAsync(PokemonDataKind.Moves);

    /// <summary>Exports the Pokémon (.pkdata) or the moves (.mvdata): the changes, or every value.</summary>
    private async Task ExportDataAsync(PokemonDataKind kind)
    {
        bool moves = kind == PokemonDataKind.Moves;
        string title = moves ? Strings.Data_ExportMovesTitle : Strings.Data_ExportTitle;
        var all = new DialogCheck(Strings.Data_ExportAll);
        var question = new QuestionViewModel(title, moves ? Strings.Data_ExportMovesMessage : Strings.Data_ExportMessage,
            Strings.Data_ExportConfirm, Strings.Common_Cancel, [all]);
        if (!await dialogs.AskAsync(question))
            return;

        string extension = PokemonDataFile.ExtensionOf(kind);
        string suggested = Path.GetFileNameWithoutExtension(ProjectPath) + (all.IsChecked ? " - all" : " - changes") + "." + extension;
        if (await dialogs.PickSaveFileAsync(title, suggested, extension) is not { } path)
            return;
        try
        {
            var file = all.IsChecked
                ? PokemonDataFile.FromCurrent(Session, Dump.Title, DataDescription(), kind)
                : PokemonDataFile.FromEdits(Session, Dump.Title, DataDescription(), kind);
            await Task.Run(() => file.Save(path));
            SetStatus(string.Format(Strings.Data_Exported, file.ValueCount, path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus(string.Format(Strings.Data_Failed, ex.Message), error: true);
        }
    }

    [RelayCommand]
    private async Task ImportPokemonData()
    {
        if (await dialogs.PickOpenFileAsync(Strings.Data_ImportTitle, ["*." + PokemonDataFile.Extension, "*." + PokemonDataFile.MovesExtension]) is not { } path)
            return;
        PokemonDataFile file;
        try
        {
            file = await Task.Run(() => PokemonDataFile.Load(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            SetStatus(string.Format(Strings.Data_Failed, ex.Message), error: true);
            return;
        }

        string contents = file.Kind switch
        {
            PokemonDataKind.Pokemon => Strings.Data_KindPokemon,
            PokemonDataKind.Moves => Strings.Data_KindMoves,
            _ => Strings.Data_KindAll,
        };
        string scope = contents + ", " + (file.Scope == PokemonDataScope.All ? Strings.Data_ScopeAll : Strings.Data_ScopeEdits);
        string game = file.Game is { } g && g != Dump.Title ? string.Format(Strings.Data_OtherGame, g.DisplayName(), Dump.Title.DisplayName()) : "";
        var replace = new DialogCheck(Strings.Data_Replace, isChecked: false, isEnabled: Session.Project.Edits.Count > 0);
        var question = new QuestionViewModel(Strings.Data_ImportTitle,
            string.Format(Strings.Data_ImportMessage, Path.GetFileName(path), file.Description ?? "—", scope, file.ValueCount, game),
            Strings.Data_ImportConfirm, Strings.Common_Cancel, [replace]);
        if (!await dialogs.AskAsync(question))
            return;

        try
        {
            History.History.Record(Session.Project, VersionKind.BeforeImport, romPath: Session.Project.Randomization.LastBuiltRom);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus(string.Format(Strings.History_Failed, ex.Message), error: true);
            return;
        }

        Session.Changed -= OnSessionChanged;
        var result = file.ApplyTo(Session, Dump.Title, replace.IsChecked);
        LoadLists(); // subscribes again and refreshes every list entry
        MarkDirty();
        History.Refresh();
        SetStatus(string.Format(Strings.Data_Imported, result.Applied, result.SameAsBase, result.Skipped.Count)
                  + (result.Skipped.Count > 0 ? " " + result.Skipped[0] : ""), error: result.Skipped.Count > 0 && result.Applied == 0);
    }

    // ------------------------------------------------------------------ play

    private bool CanPlay() => !IsBusy;

    /// <summary>
    /// Opens the emulator with the built ROM. Warns first when the project has changes the ROM does not include, and
    /// refuses while the save editor has unwritten changes (the game would overwrite them, or they would overwrite the game).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanPlay))]
    private async Task Play()
    {
        var r = Session.Project.Randomization;
        string? rom = r.LastBuiltRom is { } built && File.Exists(built)
            ? built
            : !r.Enabled && Session.Project.Edits.Count == 0 ? Session.Project.ResolveRomFile() : null;
        if (rom is null) { SetStatus(Strings.Play_BuildFirst, error: true); return; }
        if (SaveEditor.IsDirty) { SetStatus(Strings.Save_UnwrittenChanges, error: true); return; }
        if (EmulatorUserFolders.RunningEmulators(settings.EffectiveEmulatorName) is { Count: > 0 } running)
        {
            SetStatus(string.Format(Strings.Play_AlreadyRunning, string.Join(", ", running)), error: true);
            return;
        }

        string? exe = await Task.Run(() => settings.EffectiveEmulatorExecutable(rom, Session.Project.DumpDirectory));
        if (exe is null) { SetStatus(Strings.Play_NoProgram, error: true); return; }

        if (ProjectDiffersFromBuiltRom && r.LastBuiltRom is not null)
        {
            var question = new QuestionViewModel(Strings.Play_OutdatedTitle, Strings.Play_OutdatedMessage, Strings.Play_Anyway, Strings.Common_Cancel, []);
            if (!await dialogs.AskAsync(question))
                return;
        }

        try
        {
            EmulatorExecutables.Launch(exe, rom);
            SetStatus(string.Format(Strings.Play_Started, EmulatorExecutables.NameOf(exe), Path.GetFileName(rom)));
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            SetStatus(string.Format(Strings.Play_Failed, exe, ex.Message), error: true);
        }
    }

    private string? EmulatorBlocking() =>
        EmulatorUserFolders.RunningEmulators(settings.EffectiveEmulatorName) is { Count: > 0 } running
            ? string.Format(Strings.Status_CloseEmulator, string.Join(", ", running))
            : null;

    private async Task<SaveUpdateResult> ApplySaveUpdateAsync(string savePath)
    {
        SetStatus(string.Format(Strings.Status_AdaptingSave, savePath));
        string backups = Path.Combine(AppSettings.BackupRoot, settings.EffectiveEmulatorName);
        var result = await Task.Run(() => SaveUpdater.Apply(savePath, Session.Current, backups));
        SaveUpdater.PruneBackups(backups, keep: 10);
        return result;
    }

    private bool CanAdaptSave() => !IsBusy;

    /// <summary>
    /// Adapts the save to the already built ROM without rebuilding it. Only when the current configuration is the one of
    /// that ROM (same seed, randomization cached), so the save is never adapted to something other than what is played.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAdaptSave))]
    private async Task AdaptSave()
    {
        var r = Session.Project.Randomization;
        if (Randomizer.SavePath is not { } savePath || !File.Exists(savePath)) { SetStatus(Randomizer.SaveText, error: true); return; }
        if (r.LastBuiltRom is null || !File.Exists(r.LastBuiltRom)) { SetStatus(Strings.Status_BuildFirst, error: true); return; }
        if (IsDirty || (r.Enabled && r.InstalledSeed != r.Seed) || Randomizer.Options.HasChanges)
        {
            SetStatus(Strings.Status_ChangedSinceBuild, error: true);
            return;
        }
        if (EmulatorBlocking() is { } blocking) { SetStatus(blocking, error: true); return; }
        if (SaveEditor.IsDirty) { SetStatus(Strings.Save_UnwrittenChanges, error: true); return; }

        UprResult? random = null;
        if (r.Enabled && (random = upr.TryGetCached(Session.Project, r.Preset)) is null)
        {
            SetStatus(Strings.Status_NotCached, error: true);
            return;
        }

        IsBusy = true;
        try
        {
            ReloadBaseIfNeeded(random is null ? null : Path.Combine(random.TitleDirectory, "romfs"));
            var result = await ApplySaveUpdateAsync(savePath);
            Randomizer.OnBuilt(null, result);
            SaveEditor.OnRomChanged();
            SetStatus(result.Changes.Count == 0
                ? string.Format(Strings.Status_SaveAlreadyAdapted, result.PokemonChecked)
                : string.Format(Strings.Status_SaveAdapted, Path.GetFileName(r.LastBuiltRom), result.Changes.Count, result.BackupPath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SaveUpdateException or InvalidDataException)
        {
            SetStatus(string.Format(Strings.Status_AdaptFailed, ex.Message), error: true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>If the base (randomized or not, and which seed) no longer matches, reopens the session keeping the edits.</summary>
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

    // ------------------------------------------------------------------ other

    [RelayCommand]
    private async Task OpenSettings()
    {
        await dialogs.ShowSettingsAsync(new SettingsViewModel(settings, upr, dialogs, Session.Project, Dump.Title, History));
        main.Room.ProfileChanged();
        OnPropertyChanged(nameof(EmulatorText));
        Randomizer.RefreshSave();
        MarkDirty(); // the project's base ROM may have changed
    }

    [RelayCommand]
    private async Task OpenOtherProject()
    {
        if (await dialogs.PickOpenFileAsync(Strings.Welcome_OpenProjectTitle) is { } path && main.OpenProject(path) is { } error)
            SetStatus(error, error: true);
    }

    [RelayCommand]
    private void NewProject() => main.ShowWelcome();

    /// <summary>
    /// Back to the ROM preview of this project (the start screen selects the last opened project). The project is saved
    /// first so the preview shows what was edited; unwritten save edits must be written or reloaded before leaving.
    /// </summary>
    [RelayCommand]
    private async Task ViewRom()
    {
        if (SaveEditor.IsDirty) { SetStatus(Strings.Save_UnwrittenChanges, error: true); return; }
        if (IsDirty && !await SaveAsync())
            return;
        settings.TouchProject(ProjectPath);
        main.ShowWelcome();
    }

    public void SetStatus(string text, bool error = false)
    {
        Status = text;
        StatusIsError = error;
    }
}
