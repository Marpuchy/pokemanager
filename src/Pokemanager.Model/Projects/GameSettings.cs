namespace Pokemanager.Model.Projects;

/// <summary>
/// Changes to how the game itself behaves, applied when the ROM is built. Like the shops they are part of the project
/// and not of the randomization: the seed does not touch them and changing one needs a rebuild, not a new roll.
/// </summary>
public sealed class GameSettings
{
    /// <summary>
    /// Every Pokémon that is not shiny-locked comes out shiny, by taking the condition off the branch the game uses to
    /// decide it (see <see cref="Data.GameCode"/>). The games keep no "shiny rate" number that can be dialled: this is
    /// the change that is actually there to make, so the choice is the game's odds or all of them.
    /// </summary>
    public bool AlwaysShiny { get; set; }

    /// <summary>
    /// The highest level a Pokémon can reach by experience, or null for the game's own 100. Written into the game's
    /// experience table (see <see cref="Data.ExperienceTable.Cap"/>), so it holds inside the game itself.
    /// </summary>
    public int? LevelCap { get; set; }

    /// <summary>
    /// The cap follows the player's progress (see <see cref="Data.LevelCaps"/>): the application sets <see cref="LevelCap"/>
    /// from the save's badges or Z-crystals, and a new milestone asks for a rebuild.
    /// </summary>
    public bool LevelCapByMilestones { get; set; }

    /// <summary>
    /// The odds of a Pokémon being shiny, as "one in N" (the game's own is one in 4096): null leaves the game's, 1 makes
    /// every Pokémon that is not shiny-locked shiny. Anything between is written as the number of times the game rolls
    /// the Pokémon's identity looking for a shiny one (<see cref="Data.GameCode.SetShinyRolls"/>), which is how the
    /// Shiny Charm works too — so N is approximate below 1 in 16, the most rolls the patch writes is 255.
    /// </summary>
    public int? ShinyOdds { get; set; }

    /// <summary>The odds that will be built: <see cref="ShinyOdds"/>, or 1 in 1 for a project made with the old switch.</summary>
    public int? EffectiveShinyOdds => ShinyOdds ?? (AlwaysShiny ? 1 : null);

    /// <summary>Levels the player chose for some steps of the plan (step index → level), over the computed ones.</summary>
    public Dictionary<int, int> LevelCapOverrides { get; set; } = [];

    /// <summary>
    /// Trainers' Pokémon levels raised or lowered by this percentage (0 = as the ROM has them). A trainer level edited
    /// by hand keeps its value. See <see cref="Data.GameLevels"/>.
    /// </summary>
    public double TrainerLevelPercent { get; set; }

    /// <summary>Wild Pokémon levels, as a percentage (Generation 7).</summary>
    public double WildLevelPercent { get; set; }

    /// <summary>The fixed Pokémon — legendaries, gifts, totems and their allies — as a percentage (Generation 7).</summary>
    public double StaticLevelPercent { get; set; }

    /// <summary>
    /// The three starters, as a percentage (Generation 7). Apart from <see cref="StaticLevelPercent"/>, which leaves
    /// them alone as UPR ZX does, so a run can start stronger or weaker without moving every legendary with them.
    /// </summary>
    public double StarterLevelPercent { get; set; }

    /// <summary>
    /// Give the three starters the game ships with (Rowlet, Litten and Popplio in Generation 7) when the project does
    /// not randomize, whatever the base ROM has. A base ROM that was randomized before it was imported carries other
    /// three, and starting over has to start with the game's own (user, 2026-11-03). A randomization decides them
    /// itself, so with one on this does nothing.
    /// </summary>
    public bool OriginalStarters { get; set; }

    public bool HasLevelChanges => TrainerLevelPercent != 0 || WildLevelPercent != 0 || StaticLevelPercent != 0
                                   || StarterLevelPercent != 0;

    /// <summary>
    /// The cap of the ROM that was built last, which is the one the save has been played on. It is a fact of what was
    /// built, like the randomization's installed seed: an undo does not move it, and the save adaptation needs it to
    /// know what a Pokémon banked while that cap held it.
    /// </summary>
    public int? InstalledLevelCap { get; set; }

    /// <summary>
    /// Bring back to the cap whatever got past it, when the emulator is closed. A Rare Candy ignores the cap — it
    /// writes the next level's value straight in — so without this the cap can be walked past by accident. Null means
    /// on, so a project made before this option follows the cap.
    /// </summary>
    public bool? TrimOverCap { get; set; }

    /// <summary>Whether the levels past the cap are brought back (<see cref="TrimOverCap"/>, on unless it is off).</summary>
    public bool TrimsOverCap => TrimOverCap ?? true;

    /// <summary>
    /// The difficulty profile the player chose (<see cref="Difficulty.DifficultyProfiles"/>), null for the game's own.
    /// It is only the label: what a profile does lives in the trainer edits and in <see cref="TrainerLevelPercent"/>,
    /// like anything the player could have written by hand.
    /// </summary>
    public Difficulty.DifficultyLevel? Difficulty { get; set; }

    /// <summary>
    /// From what level a difficulty profile that gives megas puts the Mega Stone on a trainer's last Pokémon, instead of
    /// the profile's own (2026-11-09, issue #2); null keeps the profile's.
    /// </summary>
    public int? DifficultyMegaFromLevel { get; set; }

    /// <summary>Whether anything at all has to be done at build time.</summary>
    public bool IsEmpty => !AlwaysShiny && LevelCap is null && !HasLevelChanges && !OriginalStarters && ShinyOdds is null;

    public GameSettings Clone() => new()
    {
        AlwaysShiny = AlwaysShiny,
        LevelCap = LevelCap,
        InstalledLevelCap = InstalledLevelCap,
        TrimOverCap = TrimOverCap,
        Difficulty = Difficulty,
        DifficultyMegaFromLevel = DifficultyMegaFromLevel,
        LevelCapByMilestones = LevelCapByMilestones,
        LevelCapOverrides = new(LevelCapOverrides),
        TrainerLevelPercent = TrainerLevelPercent,
        WildLevelPercent = WildLevelPercent,
        StaticLevelPercent = StaticLevelPercent,
        StarterLevelPercent = StarterLevelPercent,
        OriginalStarters = OriginalStarters,
        ShinyOdds = ShinyOdds,
    };
}
