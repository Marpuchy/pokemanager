namespace Pokemanager.Randomizer;

/// <summary>Cómo mostrar una opción de UPR ZX: grupo, texto en español y de qué opción depende.</summary>
/// <param name="DependsOn">Opción que debe estar activa (bool true o enum distinto del primer valor) para que esta tenga efecto.</param>
public sealed record UprOptionInfo(
    string Group, string Label, string? Hint = null, string? DependsOn = null,
    int Min = 0, int Max = 100, IReadOnlyDictionary<string, string>? ChoiceLabels = null);

/// <summary>
/// Textos en español y agrupación de las opciones de UPR ZX, en el orden de sus pestañas. Las opciones
/// que no aplican a X/Y (Tótem, aliados y auras son de Sol/Luna) no aparecen.
/// </summary>
public static class UprOptionCatalog
{
    public const string Traits = "Pokémon: stats, tipos y habilidades";
    public const string Evolutions = "Evoluciones";
    public const string Starters = "Iniciales, estáticos e intercambios";
    public const string Moves = "Movimientos";
    public const string Trainers = "Entrenadores";
    public const string Wild = "Pokémon salvajes";
    public const string TmsTutors = "MT y tutores";
    public const string Items = "Objetos";
    public const string Misc = "General y ajustes varios";

    public static IReadOnlyList<string> Groups { get; } = [Traits, Evolutions, Starters, Moves, Trainers, Wild, TmsTutors, Items, Misc];

    /// <summary>Opciones que no se muestran: de otros juegos o que la app no puede editar con sentido.</summary>
    public static IReadOnlySet<string> Hidden { get; } = new HashSet<string>
    {
        "TotemPokemonMod", "AllyPokemonMod", "AuraMod", "RandomizeTotemHeldItems", "TotemLevelsModified",
        "TotemLevelModifier", "AllowTotemAltFormes", "LimitPokemon",
    };

    private static Dictionary<string, string> C(params (string Key, string Label)[] pairs) => pairs.ToDictionary(p => p.Key, p => p.Label);

    private static readonly Dictionary<string, string> UnchangedRandom = C(("UNCHANGED", "Sin cambios"), ("RANDOM", "Aleatorio"));

    public static IReadOnlyDictionary<string, UprOptionInfo> Options { get; } = new Dictionary<string, UprOptionInfo>
    {
        // ---- Pokémon: stats, tipos y habilidades
        ["BaseStatisticsMod"] = new(Traits, "Estadísticas base", ChoiceLabels: C(("UNCHANGED", "Sin cambios"), ("SHUFFLE", "Barajar las de cada Pokémon"), ("RANDOM", "Aleatorias (mismo total)"))),
        ["BaseStatsFollowEvolutions"] = new(Traits, "Las evoluciones siguen el reparto de su preevolución", DependsOn: "BaseStatisticsMod"),
        ["BaseStatsFollowMegaEvolutions"] = new(Traits, "Las megaevoluciones siguen a su forma base", DependsOn: "BaseStatisticsMod"),
        ["AssignEvoStatsRandomly"] = new(Traits, "Repartir al azar las stats ganadas al evolucionar", DependsOn: "BaseStatsFollowEvolutions"),
        ["UpdateBaseStats"] = new(Traits, "Actualizar stats base a una generación posterior"),
        ["UpdateBaseStatsToGeneration"] = new(Traits, "Generación de las stats", DependsOn: "UpdateBaseStats", Min: 6, Max: 9),
        ["StandardizeEXPCurves"] = new(Traits, "Unificar curvas de experiencia"),
        ["ExpCurveMod"] = new(Traits, "A qué Pokémon afecta la curva unificada", DependsOn: "StandardizeEXPCurves",
            ChoiceLabels: C(("LEGENDARIES", "Todos salvo legendarios"), ("STRONG_LEGENDARIES", "Todos salvo legendarios fuertes"), ("ALL", "Todos"))),
        ["TypesMod"] = new(Traits, "Tipos", ChoiceLabels: C(("UNCHANGED", "Sin cambios"), ("RANDOM_FOLLOW_EVOLUTIONS", "Aleatorios (respetan evoluciones)"), ("COMPLETELY_RANDOM", "Completamente aleatorios"))),
        ["TypesFollowMegaEvolutions"] = new(Traits, "Las megaevoluciones siguen el tipo de su forma base", DependsOn: "TypesMod"),
        ["DualTypeOnly"] = new(Traits, "Todos con doble tipo", DependsOn: "TypesMod"),
        ["AbilitiesMod"] = new(Traits, "Habilidades", ChoiceLabels: C(("UNCHANGED", "Sin cambios"), ("RANDOMIZE", "Aleatorias"))),
        ["AbilitiesFollowEvolutions"] = new(Traits, "Las evoluciones conservan las habilidades", DependsOn: "AbilitiesMod"),
        ["AbilitiesFollowMegaEvolutions"] = new(Traits, "Las megaevoluciones siguen a su forma base", DependsOn: "AbilitiesMod"),
        ["AllowWonderGuard"] = new(Traits, "Permitir Superguarda", DependsOn: "AbilitiesMod"),
        ["BanTrappingAbilities"] = new(Traits, "Prohibir habilidades que atrapan", DependsOn: "AbilitiesMod"),
        ["BanNegativeAbilities"] = new(Traits, "Prohibir habilidades negativas", DependsOn: "AbilitiesMod"),
        ["BanBadAbilities"] = new(Traits, "Prohibir habilidades malas", DependsOn: "AbilitiesMod"),
        ["WeighDuplicateAbilitiesTogether"] = new(Traits, "Contar juntas las habilidades repetidas", DependsOn: "AbilitiesMod"),
        ["EnsureTwoAbilities"] = new(Traits, "Asegurar dos habilidades", DependsOn: "AbilitiesMod"),

        // ---- Evoluciones
        ["EvolutionsMod"] = new(Evolutions, "Evoluciones", ChoiceLabels: C(("UNCHANGED", "Sin cambios"), ("RANDOM", "Aleatorias"), ("RANDOM_EVERY_LEVEL", "Aleatoria en cada nivel"))),
        ["EvosSimilarStrength"] = new(Evolutions, "Fuerza similar", DependsOn: "EvolutionsMod"),
        ["EvosSameTyping"] = new(Evolutions, "Mismo tipo", DependsOn: "EvolutionsMod"),
        ["EvosMaxThreeStages"] = new(Evolutions, "Máximo tres fases", DependsOn: "EvolutionsMod"),
        ["EvosForceChange"] = new(Evolutions, "Forzar cambio", DependsOn: "EvolutionsMod"),
        ["EvosAllowAltFormes"] = new(Evolutions, "Permitir formas alternativas", DependsOn: "EvolutionsMod"),
        ["ChangeImpossibleEvolutions"] = new(Evolutions, "Cambiar evoluciones imposibles (por intercambio…)"),
        ["MakeEvolutionsEasier"] = new(Evolutions, "Evoluciones más fáciles"),
        ["RemoveTimeBasedEvolutions"] = new(Evolutions, "Quitar evoluciones que dependen de la hora"),

        // ---- Iniciales, estáticos e intercambios
        ["StartersMod"] = new(Starters, "Iniciales", ChoiceLabels: C(("UNCHANGED", "Sin cambios"), ("CUSTOM", "Personalizados (del preset)"), ("COMPLETELY_RANDOM", "Completamente aleatorios"), ("RANDOM_WITH_TWO_EVOLUTIONS", "Aleatorios con dos evoluciones"))),
        ["AllowStarterAltFormes"] = new(Starters, "Iniciales: permitir formas alternativas", DependsOn: "StartersMod"),
        ["RandomizeStartersHeldItems"] = new(Starters, "Iniciales: objeto equipado aleatorio"),
        ["BanBadRandomStarterHeldItems"] = new(Starters, "Iniciales: prohibir objetos malos", DependsOn: "RandomizeStartersHeldItems"),
        ["StaticPokemonMod"] = new(Starters, "Pokémon estáticos (legendarios, regalos…)", ChoiceLabels: C(("UNCHANGED", "Sin cambios"), ("RANDOM_MATCHING", "Aleatorios (legendario por legendario)"), ("COMPLETELY_RANDOM", "Completamente aleatorios"), ("SIMILAR_STRENGTH", "Fuerza similar"))),
        ["AllowStaticAltFormes"] = new(Starters, "Estáticos: permitir formas alternativas", DependsOn: "StaticPokemonMod"),
        ["SwapStaticMegaEvos"] = new(Starters, "Estáticos: cambiar megaevoluciones", DependsOn: "StaticPokemonMod"),
        ["LimitMainGameLegendaries"] = new(Starters, "Estáticos: limitar legendarios de la historia", DependsOn: "StaticPokemonMod"),
        ["Limit600"] = new(Starters, "Estáticos: limitar a 600 de total", DependsOn: "StaticPokemonMod"),
        ["StaticLevelModified"] = new(Starters, "Estáticos: modificar nivel"),
        ["StaticLevelModifier"] = new(Starters, "Estáticos: % de nivel", DependsOn: "StaticLevelModified", Min: -50, Max: 50),
        ["CorrectStaticMusic"] = new(Starters, "Estáticos: música acorde al nuevo Pokémon"),
        ["InGameTradesMod"] = new(Starters, "Intercambios del juego", ChoiceLabels: C(("UNCHANGED", "Sin cambios"), ("RANDOMIZE_GIVEN", "Aleatorizar el que dan"), ("RANDOMIZE_GIVEN_AND_REQUESTED", "Aleatorizar el que dan y el que piden"))),
        ["RandomizeInGameTradesNicknames"] = new(Starters, "Intercambios: motes aleatorios", DependsOn: "InGameTradesMod"),
        ["RandomizeInGameTradesOTs"] = new(Starters, "Intercambios: entrenador original aleatorio", DependsOn: "InGameTradesMod"),
        ["RandomizeInGameTradesIVs"] = new(Starters, "Intercambios: IV aleatorios", DependsOn: "InGameTradesMod"),
        ["RandomizeInGameTradesItems"] = new(Starters, "Intercambios: objeto aleatorio", DependsOn: "InGameTradesMod"),

        // ---- Movimientos
        ["RandomizeMovePowers"] = new(Moves, "Potencia aleatoria"),
        ["RandomizeMoveAccuracies"] = new(Moves, "Precisión aleatoria"),
        ["RandomizeMovePPs"] = new(Moves, "PP aleatorios"),
        ["RandomizeMoveTypes"] = new(Moves, "Tipo aleatorio"),
        ["RandomizeMoveCategory"] = new(Moves, "Categoría aleatoria (físico/especial)"),
        ["UpdateMoves"] = new(Moves, "Actualizar movimientos a una generación posterior"),
        ["UpdateMovesToGeneration"] = new(Moves, "Generación de los movimientos", DependsOn: "UpdateMoves", Min: 6, Max: 9),
        ["MovesetsMod"] = new(Moves, "Movimientos que aprenden", ChoiceLabels: C(("UNCHANGED", "Sin cambios"), ("RANDOM_PREFER_SAME_TYPE", "Aleatorios (preferir su tipo)"), ("COMPLETELY_RANDOM", "Completamente aleatorios"), ("METRONOME_ONLY", "Solo Metrónomo"))),
        ["StartWithGuaranteedMoves"] = new(Moves, "Empezar con movimientos garantizados", DependsOn: "MovesetsMod"),
        ["GuaranteedMoveCount"] = new(Moves, "Número de movimientos garantizados", DependsOn: "StartWithGuaranteedMoves", Min: 2, Max: 4),
        ["ReorderDamagingMoves"] = new(Moves, "Ordenar movimientos de daño por potencia", DependsOn: "MovesetsMod"),
        ["MovesetsForceGoodDamaging"] = new(Moves, "Forzar buenos movimientos de daño", DependsOn: "MovesetsMod"),
        ["MovesetsGoodDamagingPercent"] = new(Moves, "% de buenos movimientos de daño", DependsOn: "MovesetsForceGoodDamaging"),
        ["BlockBrokenMovesetMoves"] = new(Moves, "Bloquear movimientos rotos", DependsOn: "MovesetsMod"),
        ["EvolutionMovesForAll"] = new(Moves, "Movimientos al evolucionar para todos", DependsOn: "MovesetsMod"),

        // ---- Entrenadores
        ["TrainersMod"] = new(Trainers, "Pokémon de los entrenadores", ChoiceLabels: C(("UNCHANGED", "Sin cambios"), ("RANDOM", "Aleatorios"), ("DISTRIBUTED", "Aleatorios repartidos"), ("MAINPLAYTHROUGH", "Repartidos en la historia principal"), ("TYPE_THEMED", "Con tipo temático"), ("TYPE_THEMED_ELITE4_GYMS", "Tipo temático solo en gimnasios y Alto Mando"))),
        ["RivalCarriesStarterThroughout"] = new(Trainers, "El rival lleva su inicial toda la partida", DependsOn: "TrainersMod"),
        ["TrainersUsePokemonOfSimilarStrength"] = new(Trainers, "Fuerza similar al original", DependsOn: "TrainersMod"),
        ["TrainersMatchTypingDistribution"] = new(Trainers, "Mantener la proporción de tipos", DependsOn: "TrainersMod"),
        ["TrainersBlockLegendaries"] = new(Trainers, "Sin legendarios", DependsOn: "TrainersMod"),
        ["TrainersBlockEarlyWonderGuard"] = new(Trainers, "Sin Superguarda al principio", DependsOn: "TrainersMod"),
        ["TrainersEnforceDistribution"] = new(Trainers, "Forzar reparto", DependsOn: "TrainersMod"),
        ["TrainersEnforceMainPlaythrough"] = new(Trainers, "Forzar reparto en la historia", DependsOn: "TrainersMod"),
        ["TrainersForceFullyEvolved"] = new(Trainers, "Pokémon totalmente evolucionados a partir de un nivel"),
        ["TrainersForceFullyEvolvedLevel"] = new(Trainers, "Nivel a partir del cual", DependsOn: "TrainersForceFullyEvolved", Min: 30, Max: 65),
        ["TrainersLevelModified"] = new(Trainers, "Modificar nivel de los entrenadores"),
        ["TrainersLevelModifier"] = new(Trainers, "% de nivel", DependsOn: "TrainersLevelModified", Min: -50, Max: 50),
        ["EliteFourUniquePokemonNumber"] = new(Trainers, "Pokémon únicos en el Alto Mando", Min: 0, Max: 2),
        ["AllowTrainerAlternateFormes"] = new(Trainers, "Permitir formas alternativas"),
        ["SwapTrainerMegaEvos"] = new(Trainers, "Cambiar megaevoluciones de entrenadores"),
        ["DoubleBattleMode"] = new(Trainers, "Todos los combates dobles"),
        ["AdditionalBossTrainerPokemon"] = new(Trainers, "Pokémon extra: jefes", Max: 5),
        ["AdditionalImportantTrainerPokemon"] = new(Trainers, "Pokémon extra: importantes", Max: 5),
        ["AdditionalRegularTrainerPokemon"] = new(Trainers, "Pokémon extra: normales", Max: 5),
        ["RandomizeHeldItemsForBossTrainerPokemon"] = new(Trainers, "Objetos aleatorios: jefes"),
        ["RandomizeHeldItemsForImportantTrainerPokemon"] = new(Trainers, "Objetos aleatorios: importantes"),
        ["RandomizeHeldItemsForRegularTrainerPokemon"] = new(Trainers, "Objetos aleatorios: normales"),
        ["ConsumableItemsOnlyForTrainers"] = new(Trainers, "Solo objetos consumibles"),
        ["SensibleItemsOnlyForTrainers"] = new(Trainers, "Solo objetos con sentido"),
        ["HighestLevelGetsItemsForTrainers"] = new(Trainers, "Solo el de mayor nivel lleva objeto"),
        ["BetterTrainerMovesets"] = new(Trainers, "Mejores movimientos para los entrenadores"),
        ["RandomizeTrainerNames"] = new(Trainers, "Nombres de entrenador aleatorios"),
        ["RandomizeTrainerClassNames"] = new(Trainers, "Clases de entrenador aleatorias"),

        // ---- Pokémon salvajes
        ["WildPokemonMod"] = new(Wild, "Pokémon salvajes", ChoiceLabels: C(("UNCHANGED", "Sin cambios"), ("RANDOM", "Aleatorios"), ("AREA_MAPPING", "Uno por uno en cada zona"), ("GLOBAL_MAPPING", "Uno por uno en todo el juego"))),
        ["WildPokemonRestrictionMod"] = new(Wild, "Restricción", DependsOn: "WildPokemonMod", ChoiceLabels: C(("NONE", "Ninguna"), ("SIMILAR_STRENGTH", "Fuerza similar"), ("CATCH_EM_ALL", "Hazte con todos"), ("TYPE_THEME_AREAS", "Zonas con tipo temático"))),
        ["UseTimeBasedEncounters"] = new(Wild, "Usar encuentros según la hora", DependsOn: "WildPokemonMod"),
        ["BlockWildLegendaries"] = new(Wild, "Sin legendarios salvajes", DependsOn: "WildPokemonMod"),
        ["AllowWildAltFormes"] = new(Wild, "Permitir formas alternativas", DependsOn: "WildPokemonMod"),
        ["BalanceShakingGrass"] = new(Wild, "Equilibrar la hierba que se mueve", DependsOn: "WildPokemonMod"),
        ["UseMinimumCatchRate"] = new(Wild, "Ratio de captura mínimo"),
        ["MinimumCatchRateLevel"] = new(Wild, "Nivel del ratio mínimo (1–5)", DependsOn: "UseMinimumCatchRate", Min: 1, Max: 5),
        ["RandomizeWildPokemonHeldItems"] = new(Wild, "Objetos equipados aleatorios"),
        ["BanBadRandomWildPokemonHeldItems"] = new(Wild, "Prohibir objetos malos", DependsOn: "RandomizeWildPokemonHeldItems"),
        ["WildLevelsModified"] = new(Wild, "Modificar nivel de los salvajes"),
        ["WildLevelModifier"] = new(Wild, "% de nivel", DependsOn: "WildLevelsModified", Min: -50, Max: 50),

        // ---- MT y tutores
        ["TmsMod"] = new(TmsTutors, "Movimientos de las MT", ChoiceLabels: UnchangedRandom),
        ["KeepFieldMoveTMs"] = new(TmsTutors, "MT: conservar movimientos de campo", DependsOn: "TmsMod"),
        ["TmsForceGoodDamaging"] = new(TmsTutors, "MT: forzar buenos movimientos de daño", DependsOn: "TmsMod"),
        ["TmsGoodDamagingPercent"] = new(TmsTutors, "MT: % de buenos movimientos", DependsOn: "TmsForceGoodDamaging"),
        ["BlockBrokenTMMoves"] = new(TmsTutors, "MT: bloquear movimientos rotos", DependsOn: "TmsMod"),
        ["TmsHmsCompatibilityMod"] = new(TmsTutors, "Compatibilidad con MT/MO", ChoiceLabels: C(("UNCHANGED", "Sin cambios"), ("RANDOM_PREFER_TYPE", "Aleatoria (preferir su tipo)"), ("COMPLETELY_RANDOM", "Completamente aleatoria"), ("FULL", "Todos aprenden todo"))),
        ["TmLevelUpMoveSanity"] = new(TmsTutors, "MT: coherente con lo que aprenden por nivel", DependsOn: "TmsHmsCompatibilityMod"),
        ["TmsFollowEvolutions"] = new(TmsTutors, "MT: las evoluciones heredan compatibilidad", DependsOn: "TmsHmsCompatibilityMod"),
        ["FullHMCompat"] = new(TmsTutors, "Todos compatibles con las MO"),
        ["MoveTutorMovesMod"] = new(TmsTutors, "Movimientos de los tutores", ChoiceLabels: UnchangedRandom),
        ["KeepFieldMoveTutors"] = new(TmsTutors, "Tutores: conservar movimientos de campo", DependsOn: "MoveTutorMovesMod"),
        ["TutorsForceGoodDamaging"] = new(TmsTutors, "Tutores: forzar buenos movimientos de daño", DependsOn: "MoveTutorMovesMod"),
        ["TutorsGoodDamagingPercent"] = new(TmsTutors, "Tutores: % de buenos movimientos", DependsOn: "TutorsForceGoodDamaging"),
        ["BlockBrokenTutorMoves"] = new(TmsTutors, "Tutores: bloquear movimientos rotos", DependsOn: "MoveTutorMovesMod"),
        ["MoveTutorsCompatibilityMod"] = new(TmsTutors, "Compatibilidad con tutores", ChoiceLabels: C(("UNCHANGED", "Sin cambios"), ("RANDOM_PREFER_TYPE", "Aleatoria (preferir su tipo)"), ("COMPLETELY_RANDOM", "Completamente aleatoria"), ("FULL", "Todos aprenden todo"))),
        ["TutorLevelUpMoveSanity"] = new(TmsTutors, "Tutores: coherente con lo que aprenden por nivel", DependsOn: "MoveTutorsCompatibilityMod"),
        ["TutorFollowEvolutions"] = new(TmsTutors, "Tutores: las evoluciones heredan compatibilidad", DependsOn: "MoveTutorsCompatibilityMod"),

        // ---- Objetos
        ["FieldItemsMod"] = new(Items, "Objetos del suelo", ChoiceLabels: C(("UNCHANGED", "Sin cambios"), ("SHUFFLE", "Barajar"), ("RANDOM", "Aleatorios"), ("RANDOM_EVEN", "Aleatorios equilibrados"))),
        ["BanBadRandomFieldItems"] = new(Items, "Suelo: prohibir objetos malos", DependsOn: "FieldItemsMod"),
        ["ShopItemsMod"] = new(Items, "Objetos de las tiendas", ChoiceLabels: C(("UNCHANGED", "Sin cambios"), ("SHUFFLE", "Barajar"), ("RANDOM", "Aleatorios"))),
        ["BanBadRandomShopItems"] = new(Items, "Tiendas: prohibir objetos malos", DependsOn: "ShopItemsMod"),
        ["BanRegularShopItems"] = new(Items, "Tiendas: prohibir objetos normales", DependsOn: "ShopItemsMod"),
        ["BanOPShopItems"] = new(Items, "Tiendas: prohibir objetos demasiado buenos", DependsOn: "ShopItemsMod"),
        ["BalanceShopPrices"] = new(Items, "Equilibrar precios"),
        ["GuaranteeEvolutionItems"] = new(Items, "Garantizar objetos de evolución", DependsOn: "ShopItemsMod"),
        ["GuaranteeXItems"] = new(Items, "Garantizar objetos X", DependsOn: "ShopItemsMod"),
        ["PickupItemsMod"] = new(Items, "Objetos de Recogida", ChoiceLabels: UnchangedRandom),
        ["BanBadRandomPickupItems"] = new(Items, "Recogida: prohibir objetos malos", DependsOn: "PickupItemsMod"),

        // ---- General
        ["BanIrregularAltFormes"] = new(Misc, "Prohibir formas alternativas irregulares"),
        ["RaceMode"] = new(Misc, "Modo carrera (verificación de semilla en el log)"),
        ["BlockBrokenMoves"] = new(Misc, "Bloquear movimientos rotos"),
        ["ShinyChance"] = new(Misc, "Aumentar probabilidad de shiny"),
    };

    /// <summary>Etiquetas de los ajustes varios que admite X/Y.</summary>
    public static IReadOnlyDictionary<string, string> TweakLabels { get; } = new Dictionary<string, string>
    {
        ["FASTEST_TEXT"] = "Texto a máxima velocidad",
        ["NATIONAL_DEX_AT_START"] = "Pokédex Nacional desde el principio",
        ["BAN_LUCKY_EGG"] = "Prohibir Huevo Suerte",
        ["RETAIN_ALT_FORMES"] = "No revertir formas temporales",
    };

    /// <summary>Información de una opción; las no catalogadas van a «General» con su nombre legible.</summary>
    public static UprOptionInfo Describe(string name) =>
        Options.TryGetValue(name, out var info) ? info : new UprOptionInfo(Misc, Humanize(name));

    private static string Humanize(string name) =>
        string.Concat(name.Select((c, i) => i > 0 && char.IsUpper(c) && !char.IsUpper(name[i - 1]) ? " " + c : c.ToString()));
}
