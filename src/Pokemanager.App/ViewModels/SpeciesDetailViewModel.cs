using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pokemanager.App.Resources;
using Pokemanager.App.Services;
using Pokemanager.Model.Data;
using Pokemanager.Model.Dump;
using Pokemanager.Model.Editing;
using Pokemanager.Model.Edits;

namespace Pokemanager.App.ViewModels;

/// <summary>Detail of a personal entry (species or form) with its learnset.</summary>
public partial class SpeciesDetailViewModel : ObservableObject
{
    private const string P = GameTables.Personal;
    private readonly EditorSession session;
    private readonly GameNames names;

    public int Id { get; }
    public string Title { get; }

    /// <summary>Sprite from the dump, if any.</summary>
    public Avalonia.Media.Imaging.Bitmap? Icon { get; }

    public IReadOnlyList<StatFieldViewModel> Stats { get; }
    public IReadOnlyList<ChoiceFieldViewModel> Types { get; }
    public IReadOnlyList<ChoiceFieldViewModel> Abilities { get; }
    public IReadOnlyList<FieldViewModel> Breeding { get; }
    public IReadOnlyList<ChoiceFieldViewModel> Items { get; }
    public IReadOnlyList<IntFieldViewModel> EvYield { get; }
    public IReadOnlyList<IntFieldViewModel> Other { get; }

    /// <summary>Generation 7 only: crystal, the move it needs and the Z-move it becomes.</summary>
    public IReadOnlyList<ChoiceFieldViewModel> ZMove { get; }

    public bool HasZMove { get; }

    public ObservableCollection<LearnsetRowViewModel> Learnset { get; } = [];
    public IReadOnlyList<string> MoveNames => names.Moves;

    /// <summary>Type chip of a move as the project has it now (the learnset rows show it).</summary>
    internal TypeChip MoveTypeChip(int move)
    {
        int type = move > 0 && move < session.Current.Moves.Length ? session.Current.Moves[move].Type : -1;
        return new TypeChip(type >= 0 && type < names.Types.Count ? names.Types[type] : "—", TypeColors.Background(type), TypeColors.Foreground(type));
    }

    public int BaseStatTotal => Stats.Sum(s => (int)(s.Value ?? 0));

    /// <summary>
    /// The base stats the ROM has before this project's edits, next to the current ones (2026-11-09, user: "un botón al
    /// lado del BST que te muestre los stats originales"). Rebuilt on every read, so it follows the edits.
    /// </summary>
    public IReadOnlyList<OriginalStatRow> OriginalStats =>
        [.. Stats.Select(s => new OriginalStatRow(s.Label, s.OriginalInt, (int)(s.Value ?? 0))),
         new OriginalStatRow(Strings.Badge_Total, Stats.Sum(s => s.OriginalInt), BaseStatTotal)];
    public bool IsLearnsetModified => session.IsModified(GameTables.Learnsets, Id, GameTables.LevelUp);
    public bool IsModified => session.IsModified(P, Id) || session.IsModified(GameTables.Learnsets, Id) || IsEvolutionModified;

    // ------------------------------------------------------------------ evolution

    /// <summary>The evolutions in use of this row, one editable row each (the game has eight slots).</summary>
    public ObservableCollection<EvolutionRowViewModel> Evolutions { get; } = [];

    /// <summary>False when the game folder has no evolution archive or this row's record is not one the app reads.</summary>
    public bool HasEvolutionData => Id < session.Current.Evolutions.Length && session.Current.Evolutions[Id].IsReadable;

    public bool IsEvolutionModified => Id < session.Current.Evolutions.Length && session.IsModified(GameTables.Evolutions, Id);

    public bool CanAddEvolution => HasEvolutionData && Evolutions.Count < EvolutionSlots.Count;

    public bool HasNoEvolution => HasEvolutionData && Evolutions.Count == 0;

    private EvolutionChainViewModel? chain;

    /// <summary>The whole line this Pokémon belongs to; a click on another of its Pokémon opens that one.</summary>
    public EvolutionChainViewModel Chain => chain ??= BuildChain();

    private EvolutionChainViewModel BuildChain()
    {
        var index = PersonalIndex.Of(session.Current);
        var (species, form) = index.Pokemon(Id) ?? (Id, 0);
        return EvolutionChainViewModel.Build(session.Current, names, species, form,
            (s, f) => sprites?.For(s, f), openRow is null ? null : (s, f) =>
            {
                int row = index.Row(s, f);
                openRow(row >= 0 ? row : s);
            });
    }

    private void LoadEvolutions()
    {
        Evolutions.Clear();
        if (HasEvolutionData)
        {
            bool gen7 = session.Current.Evolutions[Id].Generation7;
            foreach (var (slot, _) in session.Current.Evolutions[Id].InUse())
                Evolutions.Add(new EvolutionRowViewModel(session, names, Id, slot, gen7, RemoveEvolution, EvolutionChanged, sprites));
        }
        EvolutionChanged();
    }

    private void EvolutionChanged()
    {
        chain = null;
        OnPropertyChanged(nameof(Chain));
        OnPropertyChanged(nameof(IsEvolutionModified));
        OnPropertyChanged(nameof(IsModified));
        OnPropertyChanged(nameof(CanAddEvolution));
        OnPropertyChanged(nameof(HasNoEvolution));
        foreach (var row in Evolutions)
            row.Refresh();
    }

    [RelayCommand]
    private void AddEvolution()
    {
        if (!HasEvolutionData)
            return;
        var record = session.Current.Evolutions[Id];
        int free = Enumerable.Range(0, EvolutionSlots.Count).FirstOrDefault(i => record.Slot(i)!.Method == 0, -1);
        if (free < 0)
            return;
        bool gen7 = record.Generation7;
        int species = PersonalIndex.Of(session.Current).Pokemon(Id)?.Species ?? 0;
        int into = Math.Min(species + 1, session.Current.LastSpecies);
        session.SetInt(GameTables.Evolutions, Id, GameTables.EvolutionField(free, "method"), 4);
        session.SetInt(GameTables.Evolutions, Id, GameTables.EvolutionField(free, "species"), into);
        session.SetInt(GameTables.Evolutions, Id, GameTables.EvolutionField(free, gen7 ? "level" : "argument"), 16);
        if (gen7)
            session.SetInt(GameTables.Evolutions, Id, GameTables.EvolutionField(free, "form"), -1);
        LoadEvolutions();
    }

    private void RemoveEvolution(EvolutionRowViewModel row)
    {
        row.Clear();
        LoadEvolutions();
    }

    private readonly PokemonSprites? sprites;
    private readonly Action<int>? openRow;

    /// <param name="batch">Groups the earlier stages' changes into one undo step (the editor's <c>BeginBatch</c>).</param>
    public SpeciesDetailViewModel(EditorSession session, GameNames names, int id, Avalonia.Media.Imaging.Bitmap? icon = null,
        PokemonSprites? sprites = null, Action<int>? openRow = null, Func<string, IDisposable>? batch = null)
    {
        this.batch = batch;
        this.session = session;
        this.names = names;
        this.sprites = sprites;
        this.openRow = openRow;
        Id = id;
        Title = $"#{id:000} {names.PersonalEntries[id]}";
        Icon = icon;

        IntFieldViewModel Int(string field, string label, int max = 255) => new(session, P, id, field, label, 0, max);
        ChoiceFieldViewModel Choice(string field, string label, IReadOnlyList<string> options) => new(session, P, id, field, label, options);

        StatFieldViewModel Stat(string field, string label) => new(session, P, id, field, label);
        Stats =
        [
            Stat("hp", Strings.Stat_HP), Stat("atk", Strings.Stat_Atk), Stat("def", Strings.Stat_Def),
            Stat("spa", Strings.Stat_SpA), Stat("spd", Strings.Stat_SpD), Stat("spe", Strings.Stat_Spe),
        ];
        Types = [Choice("type1", Strings.Field_Type1, names.Types), Choice("type2", Strings.Field_Type2, names.Types)];
        Abilities =
        [
            Choice("ability1", Strings.Field_Ability1, names.Abilities),
            Choice("ability2", Strings.Field_Ability2, names.Abilities),
            Choice("abilityHidden", Strings.Field_AbilityHidden, names.Abilities),
        ];
        Breeding =
        [
            Int("catchRate", Strings.Field_CatchRate),
            Int("baseExp", Strings.Field_BaseExp, 65535),
            Choice("expGrowth", Strings.Field_ExpGrowth, GameNames.ExpGrowth),
            Int("gender", Strings.Field_Gender),
            Int("hatchCycles", Strings.Field_HatchCycles),
            Int("baseFriendship", Strings.Field_BaseFriendship),
            Choice("eggGroup1", Strings.Field_EggGroup1, GameNames.EggGroups),
            Choice("eggGroup2", Strings.Field_EggGroup2, GameNames.EggGroups),
        ];
        Items =
        [
            Choice("item1", Strings.Field_Item1, names.Items),
            Choice("item2", Strings.Field_Item2, names.Items),
            Choice("item3", Strings.Field_Item3, names.Items),
        ];
        EvYield =
        [
            Int("evHp", Strings.Ev_HP, 3), Int("evAtk", Strings.Ev_Atk, 3), Int("evDef", Strings.Ev_Def, 3),
            Int("evSpa", Strings.Ev_SpA, 3), Int("evSpd", Strings.Ev_SpD, 3), Int("evSpe", Strings.Ev_Spe, 3),
        ];
        Other = [Int("height", Strings.Field_Height, 65535), Int("weight", Strings.Field_Weight, 65535), Int("escapeRate", Strings.Field_EscapeRate)];

        // Generation 7: the species' own Z-move. Changing the move it needs is what makes a crystal usable in a run
        // (Decidueye's Decidium Z asks for Spirit Shackle, which a randomized Decidueye may never learn).
        HasZMove = session.Current.Personal[id] is pk3DS.Core.Structures.PersonalInfo.PersonalInfoSM;
        ZMove = HasZMove
            ?
            [
                Choice(GameTables.ZCrystal, Strings.Field_ZCrystal, names.Items),
                Choice(GameTables.ZBaseMove, Strings.Field_ZBaseMove, names.Moves),
                Choice(GameTables.ZMove, Strings.Field_ZMove, names.Moves),
            ]
            : [];

        foreach (var stat in Stats)
            stat.PropertyChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(BaseStatTotal));
                OnPropertyChanged(nameof(OriginalStats));
                FollowUp();
            };
        // The colored header follows the types as they are edited.
        foreach (var type in Types)
            type.PropertyChanged += (_, _) =>
            {
                NotifyHeader();
                FollowUp();
            };
        Breeding[2].PropertyChanged += (_, _) => FollowUp();
        followed = Signature();
        foreach (var field in Abilities)
            field.PropertyChanged += (_, _) => OnPropertyChanged(nameof(AbilitiesText));

        LoadLearnset();
        LoadEvolutions();
    }

    // ------------------------------------------------------------------ earlier stages

    /// <summary>What the earlier stages follow, as it was when they last followed it.</summary>
    private string followed = "";
    private readonly Func<string, IDisposable>? batch;
    private bool following;

    private string Signature() => string.Join(",", new[] { "hp", "atk", "def", "spa", "spd", "spe", "type1", "type2", "expGrowth" }
        .Select(f => session.GetInt(P, Id, f)));

    /// <summary>
    /// The earlier stages follow this Pokémon's stats, types and experience group as they are edited (2026-11-10, user),
    /// unless the project turned it off.
    /// </summary>
    public bool FollowPreevolutions
    {
        get => !session.Project.Tweaks.KeepPreevolutions;
        set
        {
            if (value == FollowPreevolutions)
                return;
            session.Project.Tweaks.KeepPreevolutions = !value;
            OnPropertyChanged();
            if (value)
            {
                followed = "";
                FollowUp();
            }
        }
    }

    [ObservableProperty]
    private string preevolutionNote = "";

    private void FollowUp()
    {
        if (following || !FollowPreevolutions)
            return;
        string now = Signature();
        if (now == followed)
            return;
        following = true;
        try
        {
            followed = now;
            using var step = batch?.Invoke(string.Format(Strings.Undo_Preevolutions, names.PersonalEntries[Id]));
            int changed = new PreevolutionSync(session).SyncFrom(Id);
            if (changed > 0)
                PreevolutionNote = string.Format(Strings.Detail_PreevolutionsAdjusted, changed);
        }
        finally
        {
            following = false;
        }
    }

    /// <summary>Every earlier stage of every edited Pokémon whose stats were not edited itself.</summary>
    [RelayCommand]
    private void SyncAllPreevolutions()
    {
        int changed;
        using (batch?.Invoke(Strings.Detail_SyncAllPreevolutions))
            changed = new PreevolutionSync(session).SyncAll();
        PreevolutionNote = changed > 0 ? string.Format(Strings.Detail_PreevolutionsAdjusted, changed) : Strings.Detail_PreevolutionsNone;
    }

    // ------------------------------------------------------------------ header

    private int Type1 => session.GetInt(P, Id, "type1");
    private int Type2 => session.GetInt(P, Id, "type2");

    public IBrush TypeBackground => TypeColors.Background(Type1, Type2);
    public IBrush TypeForeground => TypeColors.Foreground(Type1, Type2);

    public IReadOnlyList<TypeChip> TypeChips => new[] { Type1, Type2 }.Distinct()
        .Select(t => new TypeChip(t < names.Types.Count ? names.Types[t] : "?", TypeColors.Background(t), TypeColors.Foreground(t)))
        .ToList();

    public string AbilitiesText => string.Join(" · ", new[] { "ability1", "ability2", "abilityHidden" }
        .Select(f => session.GetInt(P, Id, f)).Distinct()
        .Select(a => a < names.Abilities.Count ? names.Abilities[a] : "?"));

    private void NotifyHeader()
    {
        OnPropertyChanged(nameof(TypeBackground));
        OnPropertyChanged(nameof(TypeForeground));
        OnPropertyChanged(nameof(TypeChips));
        OnPropertyChanged(nameof(IsModified));
    }

    private void LoadLearnset()
    {
        Learnset.Clear();
        foreach (var pair in session.Get(GameTables.Learnsets, Id, GameTables.LevelUp).AsArray())
            Learnset.Add(new LearnsetRowViewModel(this, pair![0]!.GetValue<int>(), pair[1]!.GetValue<int>()));
        OnPropertyChanged(nameof(IsLearnsetModified));
    }

    /// <summary>Stores the whole list as a single edit and optionally reloads it sorted by level.</summary>
    internal void CommitLearnset(bool reload)
    {
        var value = new JsonArray(Learnset.Select(r => (JsonNode)new JsonArray(r.LevelValue, r.MoveIndex)).ToArray());
        session.Set(GameTables.Learnsets, Id, GameTables.LevelUp, value);
        if (reload)
            LoadLearnset();
        OnPropertyChanged(nameof(IsLearnsetModified));
        OnPropertyChanged(nameof(IsModified));
    }

    [RelayCommand]
    private void AddMove()
    {
        int level = Learnset.Count > 0 ? Learnset[^1].LevelValue : 1;
        Learnset.Add(new LearnsetRowViewModel(this, level, 1));
        CommitLearnset(reload: false);
    }

    internal void RemoveRow(LearnsetRowViewModel row)
    {
        Learnset.Remove(row);
        CommitLearnset(reload: false);
    }

    [RelayCommand]
    private void SortLearnset() => CommitLearnset(reload: true);

    [RelayCommand]
    private void Revert()
    {
        session.Revert(P, Id);
        session.Revert(GameTables.Learnsets, Id);
        if (Id < session.Current.Evolutions.Length)
            session.Revert(GameTables.Evolutions, Id);
        LoadEvolutions();
        foreach (var f in Stats.Concat<FieldViewModel>(Types).Concat(Abilities).Concat(Breeding).Concat(Items).Concat(EvYield).Concat(Other))
            f.Refresh();
        LoadLearnset();
        OnPropertyChanged(string.Empty);
    }
}

public partial class LearnsetRowViewModel(SpeciesDetailViewModel owner, int level, int move) : ObservableObject
{
    public int LevelValue { get; private set; } = level;
    public int MoveIndex { get; private set; } = move;
    public IReadOnlyList<string> MoveNames => owner.MoveNames;

    public decimal? Level
    {
        get => LevelValue;
        set
        {
            if (value is null || (int)value == LevelValue)
                return;
            LevelValue = Math.Clamp((int)value, 1, 100);
            OnPropertyChanged();
            owner.CommitLearnset(reload: false);
        }
    }

    public TypeChip MoveType => owner.MoveTypeChip(MoveIndex);

    public int SelectedMove
    {
        get => MoveIndex;
        set
        {
            if (value < 0 || value == MoveIndex)
                return;
            MoveIndex = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MoveType));
            owner.CommitLearnset(reload: false);
        }
    }

    [RelayCommand]
    private void Remove() => owner.RemoveRow(this);
}

/// <summary>One base stat as the ROM has it and as the project leaves it.</summary>
public sealed record OriginalStatRow(string Label, int Original, int Current)
{
    public string Difference => Current == Original ? "" : (Current - Original).ToString("+0;-0");
    public bool Changed => Current != Original;
}
