using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using Pokemanager.App.Resources;
using Pokemanager.Randomizer;

namespace Pokemanager.App.ViewModels;

/// <summary>Editor of the UPR ZX options. The source of truth is the project's preset (.rnqs).</summary>
public partial class UprOptionsViewModel : ObservableObject
{
    private readonly Action markDirty;
    private readonly Dictionary<string, UprOptionViewModel> byName = [];

    public ObservableCollection<UprOptionGroupViewModel> Groups { get; } = [];

    [ObservableProperty]
    public partial UprOptionGroupViewModel? SelectedGroup { get; set; }

    [ObservableProperty]
    public partial bool IsLoaded { get; set; }

    [ObservableProperty]
    public partial string LoadStatus { get; set; } = Strings.Rnd_LoadingOptions;

    /// <summary>There are option changes not yet written to the preset.</summary>
    public bool HasChanges { get; private set; }

    private readonly ShopExtrasViewModel? shops;
    private readonly Func<GameTweaksViewModel?>? game;
    private readonly DifficultyViewModel? difficulty;

    /// <param name="shops">
    /// Our own shop settings, shown on the item options page. Null in the tests, which only exercise the UPR options.
    /// </param>
    /// <param name="game">
    /// Our own game options, put on the group each one is about. A function because they are built after this page.
    /// </param>
    /// <param name="difficulty">The difficulty of the run, shown with the trainer options. Null in the tests.</param>
    public UprOptionsViewModel(Action markDirty, ShopExtrasViewModel? shops = null, Func<GameTweaksViewModel?>? game = null,
        DifficultyViewModel? difficulty = null)
    {
        this.markDirty = markDirty;
        this.shops = shops;
        this.game = game;
        this.difficulty = difficulty;
    }

    public void Load(UprSettingsDescription description, IReadOnlySet<string> availableTweaks, int generation)
    {
        Groups.Clear();
        byName.Clear();
        var groups = UprOptionCatalog.Groups.ToDictionary(g => g, g => new UprOptionGroupViewModel(g));
        groupsById = groups;
        groups[UprOptionCatalog.Items].Shops = shops;
        groups[UprOptionCatalog.Trainers].Difficulty = difficulty;
        if (game?.Invoke() is { } tweaks)
        {
            foreach (var group in groups.Values)
                group.Game = tweaks;
        }

        foreach (var option in description.Options)
        {
            var info = UprOptionCatalog.Describe(option.Name);
            var vm = new UprOptionViewModel(this, option, info);
            byName[option.Name] = vm;
            // A hidden option keeps its value in the preset (and is written back) but has no control of its own.
            if (!UprOptionCatalog.IsHidden(option.Name, generation))
                groups[info.Group].Items.Add(vm);
        }

        foreach (var tweak in description.Tweaks.Where(t => availableTweaks.Contains(t.Name)))
        {
            var vm = UprOptionViewModel.ForTweak(this, tweak);
            byName[vm.Name] = vm;
            groups[UprOptionCatalog.Misc].Items.Add(vm);
        }

        BuildTrainerTable(groups[UprOptionCatalog.Trainers], game?.Invoke());

        // Catalog order within each group (uncatalogued options last).
        var order = UprOptionCatalog.Options.Keys.Select((k, i) => (k, i)).ToDictionary(p => p.k, p => p.i);
        foreach (var g in groups.Values)
        {
            var sorted = g.Items.OrderBy(i => order.GetValueOrDefault(i.Name, int.MaxValue)).ToList();
            g.Items.Clear();
            foreach (var item in sorted)
                g.Items.Add(item);
            g.Sections = Sectioned(g);
            // The Levels page has none of UPR ZX's options (its level modifiers are hidden behind ours) and is all ours.
            if (g.Items.Count > 0 || g.IsLevels)
                Groups.Add(g);
        }

        HasChanges = false;
        RefreshEnabled();
        SelectedGroup = Groups.FirstOrDefault();
        IsLoaded = true;
        OnPropertyChanged(nameof(IsLoaded)); // the groups are new even when they were loaded before
        LoadStatus = "";
    }

    /// <summary>
    /// Groups whose options are shown as titled cards instead of loose columns (2026-11-09, user): the option names of
    /// each card, in order. An option of the group that no card names goes into a last "Others" card.
    /// </summary>
    private static readonly Dictionary<string, (string Title, string[] Names)[]> SectionsByGroup = new()
    {
        [UprOptionCatalog.Trainers] =
        [
            (Strings.Section_TrainerTeams, ["TrainersMod", "RivalCarriesStarterThroughout", "TrainersUsePokemonOfSimilarStrength",
                "TrainersMatchTypingDistribution", "TrainersBlockLegendaries", "TrainersBlockEarlyWonderGuard",
                "TrainersEnforceDistribution", "TrainersEnforceMainPlaythrough", "TrainersForceFullyEvolved",
                "TrainersForceFullyEvolvedLevel", "EliteFourUniquePokemonNumber", "AllowTrainerAlternateFormes", "SwapTrainerMegaEvos"]),
            (Strings.Section_TrainerItems, ["ConsumableItemsOnlyForTrainers", "SensibleItemsOnlyForTrainers", "HighestLevelGetsItemsForTrainers"]),
            (Strings.Section_TrainerBattle, ["DoubleBattleMode", "BetterTrainerMovesets", "ShinyChance", "RandomizeTrainerNames", "RandomizeTrainerClassNames"]),
        ],
        [UprOptionCatalog.Items] =
        [
            (Strings.Section_FieldItems, ["FieldItemsMod", "BanBadRandomFieldItems"]),
            (Strings.Section_Shops, ["ShopItemsMod", "BanBadRandomShopItems", "BanRegularShopItems", "BanOPShopItems", "BalanceShopPrices",
                "GuaranteeEvolutionItems", "GuaranteeXItems"]),
            (Strings.Section_Pickup, ["PickupItemsMod", "BanBadRandomPickupItems"]),
        ],
        [UprOptionCatalog.Misc] =
        [
            (Strings.Section_General, ["BanIrregularAltFormes", "RaceMode", "BlockBrokenMoves"]),
        ],
        [UprOptionCatalog.Traits] =
        [
            (Strings.Section_Stats, ["BaseStatisticsMod", "BaseStatsFollowEvolutions", "BaseStatsFollowMegaEvolutions",
                "AssignEvoStatsRandomly", "UpdateBaseStats", "UpdateBaseStatsToGeneration"]),
            (Strings.Section_Types, ["TypesMod", "TypesFollowMegaEvolutions", "DualTypeOnly"]),
            (Strings.Section_Abilities, ["AbilitiesMod", "AbilitiesFollowEvolutions", "AbilitiesFollowMegaEvolutions", "AllowWonderGuard",
                "BanTrappingAbilities", "BanNegativeAbilities", "BanBadAbilities", "WeighDuplicateAbilitiesTogether", "EnsureTwoAbilities"]),
            (Strings.Section_Experience, ["StandardizeEXPCurves", "ExpCurveMod"]),
        ],
        [UprOptionCatalog.Evolutions] =
        [
            (Strings.Section_Evolutions, ["EvolutionsMod", "EvosSimilarStrength", "EvosSameTyping", "EvosMaxThreeStages", "EvosForceChange",
                "EvosAllowAltFormes", "ChangeImpossibleEvolutions", "MakeEvolutionsEasier", "RemoveTimeBasedEvolutions"]),
        ],
        [UprOptionCatalog.Moves] =
        [
            (Strings.Section_MoveData, ["RandomizeMovePowers", "RandomizeMoveAccuracies", "RandomizeMovePPs", "RandomizeMoveTypes",
                "RandomizeMoveCategory", "UpdateMoves", "UpdateMovesToGeneration"]),
            (Strings.Section_Movesets, ["MovesetsMod", "StartWithGuaranteedMoves", "GuaranteedMoveCount", "ReorderDamagingMoves",
                "MovesetsForceGoodDamaging", "MovesetsGoodDamagingPercent", "BlockBrokenMovesetMoves", "EvolutionMovesForAll"]),
        ],
        [UprOptionCatalog.TmsTutors] =
        [
            (Strings.Section_Tms, ["TmsMod", "KeepFieldMoveTMs", "TmsForceGoodDamaging", "TmsGoodDamagingPercent", "BlockBrokenTMMoves",
                "TmsHmsCompatibilityMod", "TmLevelUpMoveSanity", "TmsFollowEvolutions", "FullHMCompat"]),
            (Strings.Section_Tutors, ["MoveTutorMovesMod", "KeepFieldMoveTutors", "TutorsForceGoodDamaging", "TutorsGoodDamagingPercent",
                "BlockBrokenTutorMoves", "MoveTutorsCompatibilityMod", "TutorLevelUpMoveSanity", "TutorFollowEvolutions"]),
        ],
    };

    private static IReadOnlyList<UprOptionSectionViewModel> Sectioned(UprOptionGroupViewModel group)
    {
        if (!SectionsByGroup.TryGetValue(group.Id, out var sections) || group.Items.Count == 0)
            return [];
        var byName = group.Items.ToDictionary(i => i.Name);
        var used = new HashSet<string>();
        var result = new List<UprOptionSectionViewModel>();
        foreach (var (title, names) in sections)
        {
            var items = names.Where(byName.ContainsKey).Select(n => { used.Add(n); return byName[n]; }).ToList();
            if (items.Count > 0)
                result.Add(new(title, items));
        }
        var rest = group.Items.Where(i => !used.Contains(i.Name)).ToList();
        if (rest.Count > 0)
            result.Add(new(Strings.Section_Others, rest));
        return result;
    }

    /// <summary>The options that come once per kind of trainer, taken out of the columns into one table.</summary>
    private void BuildTrainerTable(UprOptionGroupViewModel group, GameTweaksViewModel? tweaks)
    {
        var rows = new List<TrainerKindRowViewModel>();
        void Upr(string label, string regular, string important, string boss)
        {
            if (!group.Items.Contains(byName.GetValueOrDefault(boss)!) || !byName.TryGetValue(regular, out var r)
                || !byName.TryGetValue(important, out var i) || !byName.TryGetValue(boss, out var b)
                || !group.Items.Contains(r) || !group.Items.Contains(i))
                return;
            group.Items.Remove(r);
            group.Items.Remove(i);
            group.Items.Remove(b);
            rows.Add(new(label, b.Tip, TrainerKindCellViewModel.For(r), TrainerKindCellViewModel.For(i), TrainerKindCellViewModel.For(b), false));
        }
        Upr(Strings.TrainerKinds_Extra, "AdditionalRegularTrainerPokemon", "AdditionalImportantTrainerPokemon", "AdditionalBossTrainerPokemon");
        Upr(Strings.TrainerKinds_HeldItems, "RandomizeHeldItemsForRegularTrainerPokemon",
            "RandomizeHeldItemsForImportantTrainerPokemon", "RandomizeHeldItemsForBossTrainerPokemon");
        if (tweaks is not null)
        {
            var items = tweaks.TrainerItems; // boss, important, ordinary
            rows.Add(new(Strings.TrainerItems_Mega, Strings.TrainerItems_MegaTip, TrainerKindCellViewModel.Mega(items[2]),
                TrainerKindCellViewModel.Mega(items[1]), TrainerKindCellViewModel.Mega(items[0]), true));
            if (tweaks.HasZCrystals)
                rows.Add(new(Strings.TrainerItems_Z, Strings.TrainerItems_ZTip, TrainerKindCellViewModel.Z(items[2]),
                    TrainerKindCellViewModel.Z(items[1]), TrainerKindCellViewModel.Z(items[0]), true));
        }
        group.KindTable = rows.Count > 0 ? new TrainerKindTableViewModel(rows) : null;
    }

    private Dictionary<string, UprOptionGroupViewModel> groupsById = [];

    /// <summary>The loaded options with these names that have a control (hidden ones are left out), in that order.</summary>
    public IReadOnlyList<UprOptionViewModel> Shown(IEnumerable<string> names) => !IsLoaded
        ? []
        : [.. names.Select(n => byName.GetValueOrDefault(n))
            .OfType<UprOptionViewModel>().Where(o => groupsById.Values.Any(g => g.Items.Contains(o)))];

    /// <summary>
    /// The groups with these ids, in that order, once the options are loaded (none before). With
    /// <paramref name="withOptions"/>, only the ones that have options of the randomizer's to show.
    /// </summary>
    public IReadOnlyList<UprOptionGroupViewModel> GroupsFor(IEnumerable<string> ids, bool withOptions) => !IsLoaded
        ? []
        : [.. ids.Select(id => groupsById.GetValueOrDefault(id)).OfType<UprOptionGroupViewModel>()
            .Where(g => !withOptions || g.Items.Count > 0)];

    public void Fail(string message)
    {
        IsLoaded = false;
        LoadStatus = message;
    }

    internal void OnChanged()
    {
        HasChanges = true;
        RefreshEnabled();
        markDirty();
    }

    private void RefreshEnabled()
    {
        foreach (var vm in byName.Values)
            vm.IsEnabled = vm.Info.DependsOn is not { } parent || !byName.TryGetValue(parent, out var p) || p.IsActive;
    }

    /// <summary>Every option as name=value lines for <c>write-settings</c>.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Assignments() =>
        byName.Values.Select(v => new KeyValuePair<string, string>(v.Name, v.SerializedValue)).ToList();

    public void MarkWritten() => HasChanges = false;

    /// <summary>
    /// What the preset asks the randomizer to add to a kind of level, when this application hides that option because it
    /// has its own. 0 when the preset does not touch it.
    /// </summary>
    public int PresetLevelModifier(string toggle, string percent) =>
        byName.TryGetValue(toggle, out var on) && on.BoolValue && byName.TryGetValue(percent, out var value)
            ? (int)(value.IntValue ?? 0)
            : 0;

    /// <summary>Puts that modifier back to nothing, so only this application's percentage acts.</summary>
    public void ClearLevelModifier(string toggle, string percent)
    {
        if (byName.TryGetValue(toggle, out var on))
            on.BoolValue = false;
        if (byName.TryGetValue(percent, out var value))
            value.IntValue = 0;
    }
}

/// <summary>
/// One group of options: UPR ZX's own and **this application's for the same subject**, on the same page. They are not
/// the same kind of thing — the randomizer's are rolled when the ROM is randomized, ours are written when it is built —
/// so each card says so; but a player looking for "trainer levels" finds everything about it in one place.
/// </summary>
public sealed class UprOptionGroupViewModel(string id)
{
    public string Id { get; } = id;
    public string Name { get; } = UprOptionCatalog.GroupLabel(id);
    public ObservableCollection<UprOptionViewModel> Items { get; } = [];

    /// <summary>Our own item and shop settings, on the group that holds the item options.</summary>
    public ShopExtrasViewModel? Shops { get; set; }

    public bool HasShops => Shops is not null;

    /// <summary>The difficulty of the run, on the group that holds the trainer options.</summary>
    public DifficultyViewModel? Difficulty { get; set; }

    public bool HasDifficulty => Difficulty is not null;

    /// <summary>The group's options as titled cards, when the group has them (empty otherwise: loose columns).</summary>
    public IReadOnlyList<UprOptionSectionViewModel> Sections { get; set; } = [];

    public bool HasSections => Sections.Count > 0;

    /// <summary>The trainer options that come once per kind of trainer, as one table (trainers group only).</summary>
    public TrainerKindTableViewModel? KindTable { get; set; }

    public bool HasKindTable => KindTable is not null;

    /// <summary>Our own game options; each group shows the ones about its subject.</summary>
    public GameTweaksViewModel? Game { get; set; }

    public bool HasGame => Game is not null;

    /// <summary>Base stats and the rest of the personal table: the sweeps over every Pokémon of the game.</summary>
    public bool IsPokemon => HasGame && Id == UprOptionCatalog.Traits;

    public bool IsTrainers => HasGame && Id == UprOptionCatalog.Trainers;

    /// <summary>
    /// Every level option of the application in one page (user, 2026-11-03): one value for the whole game, then
    /// trainers, wild, fixed and starters apart, and the level cap.
    /// </summary>
    public bool IsLevels => HasGame && Id == UprOptionCatalog.Levels;

    /// <summary>Everything else about the whole game: shiny.</summary>
    public bool IsMisc => HasGame && Id == UprOptionCatalog.Misc;
}

public partial class UprOptionViewModel : ObservableObject
{
    private readonly UprOptionsViewModel owner;
    private bool boolValue;
    private int intValue;
    private int choiceIndex;

    public string Name { get; }
    public UprOptionInfo Info { get; }
    public string Label { get; }
    public bool IsBool { get; }
    public bool IsInt { get; }
    public bool IsChoice { get; }
    public IReadOnlyList<string> Choices { get; } = [];
    public IReadOnlyList<string> ChoiceLabels { get; } = [];

    /// <summary>The choices with what each does, for the list's entries (built once: an ItemsSource next to a two-way SelectedIndex).</summary>
    public IReadOnlyList<UprChoiceItem> ChoiceItems { get; } = [];
    public string? Hint { get; }

    /// <summary>What the option does, on hover: UPR ZX's own text for a misc tweak, ours for everything else.</summary>
    public string? Tip => Hint ?? UprOptionCatalog.Description(Name);

    [ObservableProperty]
    public partial bool IsEnabled { get; set; } = true;

    internal UprOptionViewModel(UprOptionsViewModel owner, UprOptionValue option, UprOptionInfo info)
    {
        this.owner = owner;
        Name = option.Name;
        Info = info;
        Label = UprOptionCatalog.Label(option.Name);
        switch (option.Type)
        {
            case "bool":
                IsBool = true;
                boolValue = option.Value.ValueKind == JsonValueKind.True;
                break;
            case "int":
                IsInt = true;
                intValue = option.Value.GetInt32();
                break;
            default:
                IsChoice = true;
                Choices = option.Choices ?? [];
                ChoiceLabels = Choices.Select(c => UprOptionCatalog.ChoiceLabel(option.Name, c)).ToList();
                ChoiceItems = Choices.Select(c => new UprChoiceItem(UprOptionCatalog.ChoiceLabel(option.Name, c), UprOptionCatalog.ChoiceTip(option.Name, c))).ToList();
                choiceIndex = option.Value.ValueKind == JsonValueKind.String ? Math.Max(0, Choices.ToList().IndexOf(option.Value.GetString()!)) : 0;
                break;
        }
    }

    private UprOptionViewModel(UprOptionsViewModel owner, string name, string label, string? hint, bool value)
    {
        this.owner = owner;
        Name = name;
        Info = new UprOptionInfo(UprOptionCatalog.Misc);
        Label = label;
        Hint = hint;
        IsBool = true;
        boolValue = value;
    }

    internal static UprOptionViewModel ForTweak(UprOptionsViewModel owner, UprTweakValue tweak) =>
        new(owner, "tweak:" + tweak.Name, UprOptionCatalog.TweakLabel(tweak.Name, tweak.Label), tweak.Tooltip, tweak.Value);

    public bool BoolValue
    {
        get => boolValue;
        set { if (SetProperty(ref boolValue, value)) owner.OnChanged(); }
    }

    public decimal? IntValue
    {
        get => intValue;
        set
        {
            if (value is null)
                return;
            int v = Math.Clamp((int)value.Value, Info.Min, Info.Max);
            if (SetProperty(ref intValue, v))
                owner.OnChanged();
        }
    }

    public int ChoiceIndex
    {
        get => choiceIndex;
        set
        {
            if (value < 0 || value >= Choices.Count)
                return;
            if (SetProperty(ref choiceIndex, value))
                owner.OnChanged();
        }
    }

    public int Min => Info.Min;
    public int Max => Info.Max;

    /// <summary>Active for dependencies: bool checked, or enum other than its first value ("unchanged").</summary>
    public bool IsActive => IsBool ? boolValue : IsChoice ? choiceIndex > 0 : intValue != 0;

    public string SerializedValue => IsBool ? (boolValue ? "true" : "false") : IsInt ? intValue.ToString() : Choices[choiceIndex];
}

/// <summary>A titled card of a group's randomizer options.</summary>
public sealed record UprOptionSectionViewModel(string Title, IReadOnlyList<UprOptionViewModel> Items);

/// <summary>An entry of a randomizer option's list: its name and what it does.</summary>
public sealed record UprChoiceItem(string Label, string? Tip)
{
    public override string ToString() => Label;
}
