using Pokemanager.Randomizer.Resources;

namespace Pokemanager.Randomizer;

/// <summary>How to show a UPR ZX option: its group, which option it depends on and its numeric range.</summary>
/// <param name="DependsOn">Option that must be active (bool true or enum other than its first value) for this one to matter.</param>
public sealed record UprOptionInfo(string Group, string? DependsOn = null, int Min = 0, int Max = 100);

/// <summary>
/// Grouping of the UPR ZX options, in the order of its tabs. Labels come from the resources (<c>Opt_*</c>,
/// <c>Choice_*</c>, <c>Group_*</c>, <c>Tweak_*</c>). Totem, ally and aura Pokémon only exist in Sun/Moon and Ultra
/// Sun/Ultra Moon, so that group is hidden for Generation 6 games.
/// </summary>
public static class UprOptionCatalog
{
    public const string Traits = "Traits";
    public const string Evolutions = "Evolutions";
    public const string Starters = "Starters";
    public const string Moves = "Moves";
    public const string Trainers = "Trainers";
    public const string Wild = "Wild";
    public const string TmsTutors = "TmsTutors";
    public const string Items = "Items";
    public const string Misc = "Misc";
    public const string Totems = "Totems";

    public static IReadOnlyList<string> Groups { get; } = [Traits, Evolutions, Starters, Moves, Trainers, Wild, Totems, TmsTutors, Items, Misc];

    /// <summary>
    /// Options the application does not show. <c>LimitPokemon</c> is not supported. The trainer level percentage is
    /// shown (the user wants it with the other trainer options) even though Advanced: Trainers has one per class too:
    /// UPR ZX's applies to every trainer as part of the randomization, the advanced one on top of it.
    /// </summary>
    public static IReadOnlySet<string> Hidden { get; } = new HashSet<string> { "LimitPokemon" };

    /// <summary>Whether an option is hidden for a game of <paramref name="generation"/> (6 or 7).</summary>
    public static bool IsHidden(string name, int generation) =>
        Hidden.Contains(name) || (generation < 7 && Describe(name).Group == Totems);

    public static IReadOnlyDictionary<string, UprOptionInfo> Options { get; } = new Dictionary<string, UprOptionInfo>
    {
        // ---- Pokémon traits
        ["BaseStatisticsMod"] = new(Traits),
        ["BaseStatsFollowEvolutions"] = new(Traits, "BaseStatisticsMod"),
        ["BaseStatsFollowMegaEvolutions"] = new(Traits, "BaseStatisticsMod"),
        ["AssignEvoStatsRandomly"] = new(Traits, "BaseStatsFollowEvolutions"),
        ["UpdateBaseStats"] = new(Traits),
        ["UpdateBaseStatsToGeneration"] = new(Traits, "UpdateBaseStats", 6, 9),
        ["StandardizeEXPCurves"] = new(Traits),
        ["ExpCurveMod"] = new(Traits, "StandardizeEXPCurves"),
        ["TypesMod"] = new(Traits),
        ["TypesFollowMegaEvolutions"] = new(Traits, "TypesMod"),
        ["DualTypeOnly"] = new(Traits, "TypesMod"),
        ["AbilitiesMod"] = new(Traits),
        ["AbilitiesFollowEvolutions"] = new(Traits, "AbilitiesMod"),
        ["AbilitiesFollowMegaEvolutions"] = new(Traits, "AbilitiesMod"),
        ["AllowWonderGuard"] = new(Traits, "AbilitiesMod"),
        ["BanTrappingAbilities"] = new(Traits, "AbilitiesMod"),
        ["BanNegativeAbilities"] = new(Traits, "AbilitiesMod"),
        ["BanBadAbilities"] = new(Traits, "AbilitiesMod"),
        ["WeighDuplicateAbilitiesTogether"] = new(Traits, "AbilitiesMod"),
        ["EnsureTwoAbilities"] = new(Traits, "AbilitiesMod"),

        // ---- Evolutions
        ["EvolutionsMod"] = new(Evolutions),
        ["EvosSimilarStrength"] = new(Evolutions, "EvolutionsMod"),
        ["EvosSameTyping"] = new(Evolutions, "EvolutionsMod"),
        ["EvosMaxThreeStages"] = new(Evolutions, "EvolutionsMod"),
        ["EvosForceChange"] = new(Evolutions, "EvolutionsMod"),
        ["EvosAllowAltFormes"] = new(Evolutions, "EvolutionsMod"),
        ["ChangeImpossibleEvolutions"] = new(Evolutions),
        ["MakeEvolutionsEasier"] = new(Evolutions),
        ["RemoveTimeBasedEvolutions"] = new(Evolutions),

        // ---- Starters, statics and trades
        ["StartersMod"] = new(Starters),
        ["AllowStarterAltFormes"] = new(Starters, "StartersMod"),
        ["RandomizeStartersHeldItems"] = new(Starters),
        ["BanBadRandomStarterHeldItems"] = new(Starters, "RandomizeStartersHeldItems"),
        ["StaticPokemonMod"] = new(Starters),
        ["AllowStaticAltFormes"] = new(Starters, "StaticPokemonMod"),
        ["SwapStaticMegaEvos"] = new(Starters, "StaticPokemonMod"),
        ["LimitMainGameLegendaries"] = new(Starters, "StaticPokemonMod"),
        ["Limit600"] = new(Starters, "StaticPokemonMod"),
        ["StaticLevelModified"] = new(Starters),
        ["StaticLevelModifier"] = new(Starters, "StaticLevelModified", -50, 50),
        ["CorrectStaticMusic"] = new(Starters),
        ["InGameTradesMod"] = new(Starters),
        ["RandomizeInGameTradesNicknames"] = new(Starters, "InGameTradesMod"),
        ["RandomizeInGameTradesOTs"] = new(Starters, "InGameTradesMod"),
        ["RandomizeInGameTradesIVs"] = new(Starters, "InGameTradesMod"),
        ["RandomizeInGameTradesItems"] = new(Starters, "InGameTradesMod"),

        // ---- Moves
        ["RandomizeMovePowers"] = new(Moves),
        ["RandomizeMoveAccuracies"] = new(Moves),
        ["RandomizeMovePPs"] = new(Moves),
        ["RandomizeMoveTypes"] = new(Moves),
        ["RandomizeMoveCategory"] = new(Moves),
        ["UpdateMoves"] = new(Moves),
        ["UpdateMovesToGeneration"] = new(Moves, "UpdateMoves", 6, 9),
        ["MovesetsMod"] = new(Moves),
        ["StartWithGuaranteedMoves"] = new(Moves, "MovesetsMod"),
        ["GuaranteedMoveCount"] = new(Moves, "StartWithGuaranteedMoves", 2, 4),
        ["ReorderDamagingMoves"] = new(Moves, "MovesetsMod"),
        ["MovesetsForceGoodDamaging"] = new(Moves, "MovesetsMod"),
        ["MovesetsGoodDamagingPercent"] = new(Moves, "MovesetsForceGoodDamaging"),
        ["BlockBrokenMovesetMoves"] = new(Moves, "MovesetsMod"),
        ["EvolutionMovesForAll"] = new(Moves, "MovesetsMod"),

        // ---- Trainers
        ["TrainersMod"] = new(Trainers),
        ["RivalCarriesStarterThroughout"] = new(Trainers, "TrainersMod"),
        ["TrainersUsePokemonOfSimilarStrength"] = new(Trainers, "TrainersMod"),
        ["TrainersMatchTypingDistribution"] = new(Trainers, "TrainersMod"),
        ["TrainersBlockLegendaries"] = new(Trainers, "TrainersMod"),
        ["TrainersBlockEarlyWonderGuard"] = new(Trainers, "TrainersMod"),
        ["TrainersEnforceDistribution"] = new(Trainers, "TrainersMod"),
        ["TrainersEnforceMainPlaythrough"] = new(Trainers, "TrainersMod"),
        ["TrainersForceFullyEvolved"] = new(Trainers),
        ["TrainersForceFullyEvolvedLevel"] = new(Trainers, "TrainersForceFullyEvolved", 30, 65),
        ["TrainersLevelModified"] = new(Trainers),
        ["TrainersLevelModifier"] = new(Trainers, "TrainersLevelModified", -50, 50),
        ["EliteFourUniquePokemonNumber"] = new(Trainers, Min: 0, Max: 2),
        ["AllowTrainerAlternateFormes"] = new(Trainers),
        ["SwapTrainerMegaEvos"] = new(Trainers),
        ["DoubleBattleMode"] = new(Trainers),
        ["AdditionalBossTrainerPokemon"] = new(Trainers, Max: 5),
        ["AdditionalImportantTrainerPokemon"] = new(Trainers, Max: 5),
        ["AdditionalRegularTrainerPokemon"] = new(Trainers, Max: 5),
        ["RandomizeHeldItemsForBossTrainerPokemon"] = new(Trainers),
        ["RandomizeHeldItemsForImportantTrainerPokemon"] = new(Trainers),
        ["RandomizeHeldItemsForRegularTrainerPokemon"] = new(Trainers),
        ["ConsumableItemsOnlyForTrainers"] = new(Trainers),
        ["SensibleItemsOnlyForTrainers"] = new(Trainers),
        ["HighestLevelGetsItemsForTrainers"] = new(Trainers),
        ["BetterTrainerMovesets"] = new(Trainers),
        ["RandomizeTrainerNames"] = new(Trainers),
        ["RandomizeTrainerClassNames"] = new(Trainers),

        // ---- Wild Pokémon
        ["WildPokemonMod"] = new(Wild),
        ["WildPokemonRestrictionMod"] = new(Wild, "WildPokemonMod"),
        ["UseTimeBasedEncounters"] = new(Wild, "WildPokemonMod"),
        ["BlockWildLegendaries"] = new(Wild, "WildPokemonMod"),
        ["AllowWildAltFormes"] = new(Wild, "WildPokemonMod"),
        ["BalanceShakingGrass"] = new(Wild, "WildPokemonMod"),
        ["UseMinimumCatchRate"] = new(Wild),
        ["MinimumCatchRateLevel"] = new(Wild, "UseMinimumCatchRate", 1, 5),
        ["RandomizeWildPokemonHeldItems"] = new(Wild),
        ["BanBadRandomWildPokemonHeldItems"] = new(Wild, "RandomizeWildPokemonHeldItems"),
        ["WildLevelsModified"] = new(Wild),
        ["WildLevelModifier"] = new(Wild, "WildLevelsModified", -50, 50),

        // ---- TMs and tutors
        ["TmsMod"] = new(TmsTutors),
        ["KeepFieldMoveTMs"] = new(TmsTutors, "TmsMod"),
        ["TmsForceGoodDamaging"] = new(TmsTutors, "TmsMod"),
        ["TmsGoodDamagingPercent"] = new(TmsTutors, "TmsForceGoodDamaging"),
        ["BlockBrokenTMMoves"] = new(TmsTutors, "TmsMod"),
        ["TmsHmsCompatibilityMod"] = new(TmsTutors),
        ["TmLevelUpMoveSanity"] = new(TmsTutors, "TmsHmsCompatibilityMod"),
        ["TmsFollowEvolutions"] = new(TmsTutors, "TmsHmsCompatibilityMod"),
        ["FullHMCompat"] = new(TmsTutors),
        ["MoveTutorMovesMod"] = new(TmsTutors),
        ["KeepFieldMoveTutors"] = new(TmsTutors, "MoveTutorMovesMod"),
        ["TutorsForceGoodDamaging"] = new(TmsTutors, "MoveTutorMovesMod"),
        ["TutorsGoodDamagingPercent"] = new(TmsTutors, "TutorsForceGoodDamaging"),
        ["BlockBrokenTutorMoves"] = new(TmsTutors, "MoveTutorMovesMod"),
        ["MoveTutorsCompatibilityMod"] = new(TmsTutors),
        ["TutorLevelUpMoveSanity"] = new(TmsTutors, "MoveTutorsCompatibilityMod"),
        ["TutorFollowEvolutions"] = new(TmsTutors, "MoveTutorsCompatibilityMod"),

        // ---- Items
        ["FieldItemsMod"] = new(Items),
        ["BanBadRandomFieldItems"] = new(Items, "FieldItemsMod"),
        ["ShopItemsMod"] = new(Items),
        ["BanBadRandomShopItems"] = new(Items, "ShopItemsMod"),
        ["BanRegularShopItems"] = new(Items, "ShopItemsMod"),
        ["BanOPShopItems"] = new(Items, "ShopItemsMod"),
        ["BalanceShopPrices"] = new(Items),
        ["GuaranteeEvolutionItems"] = new(Items, "ShopItemsMod"),
        ["GuaranteeXItems"] = new(Items, "ShopItemsMod"),
        ["PickupItemsMod"] = new(Items),
        ["BanBadRandomPickupItems"] = new(Items, "PickupItemsMod"),

        // ---- Totem, ally and aura Pokémon (Generation 7)
        ["TotemPokemonMod"] = new(Totems),
        ["AllyPokemonMod"] = new(Totems),
        ["AuraMod"] = new(Totems),
        ["RandomizeTotemHeldItems"] = new(Totems),
        ["AllowTotemAltFormes"] = new(Totems, "TotemPokemonMod"),
        ["TotemLevelsModified"] = new(Totems),
        ["TotemLevelModifier"] = new(Totems, "TotemLevelsModified", -50, 50),

        // ---- General
        ["BanIrregularAltFormes"] = new(Misc),
        ["RaceMode"] = new(Misc),
        ["BlockBrokenMoves"] = new(Misc),
        ["ShinyChance"] = new(Misc),
    };

    /// <summary>Info of an option; uncatalogued ones go to the general group.</summary>
    public static UprOptionInfo Describe(string name) => Options.TryGetValue(name, out var info) ? info : new UprOptionInfo(Misc);

    public static string GroupLabel(string group) => Text("Group_" + group) ?? group;

    /// <summary>Localized label of an option; uncatalogued ones show their name with spaces.</summary>
    public static string Label(string name) => Text("Opt_" + name) ?? Humanize(name);

    /// <summary>What an option does, one line, shown when the pointer rests on it; null when there is none written.</summary>
    public static string? Description(string name) => Text("Tip_" + name);

    /// <summary>Label of an enum value: specific to the option, then generic, then the raw constant.</summary>
    public static string ChoiceLabel(string option, string choice) =>
        Text($"Choice_{option}_{choice}") ?? Text("Choice_" + choice) ?? choice;

    /// <summary>Label of a misc tweak, falling back to UPR's own (English) name.</summary>
    public static string TweakLabel(string tweak, string fallback) => Text("Tweak_" + tweak) ?? fallback;

    private static string? Text(string key) => Strings.ResourceManager.GetString(key, Strings.Culture);

    private static string Humanize(string name) =>
        string.Concat(name.Select((c, i) => i > 0 && char.IsUpper(c) && !char.IsUpper(name[i - 1]) ? " " + c : c.ToString()));
}
