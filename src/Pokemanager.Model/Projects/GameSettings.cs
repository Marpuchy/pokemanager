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

    /// <summary>Whether anything at all has to be done at build time.</summary>
    public bool IsEmpty => !AlwaysShiny && LevelCap is null;

    public GameSettings Clone() => new() { AlwaysShiny = AlwaysShiny, LevelCap = LevelCap };
}
