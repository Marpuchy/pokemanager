using PKHeX.Core;
using Pokemanager.Model.Data;

namespace Pokemanager.Save;

/// <summary>
/// Levels from experience when the ROM has a level cap (<see cref="ExperienceTable"/>): above the cap the experience is a
/// code for the level, not an amount, and the normal table would read any of those values as level 100.
/// </summary>
public static class ExperienceLevels
{
    /// <summary>The level the game shows for this Pokémon.</summary>
    public static int LevelOf(PKM pk, byte growth)
    {
        if (ExperienceTable.LevelOfCapped(pk.EXP) is { } coded)
            return coded;
        if (ExperienceTable.IsPrototype(pk.EXP))
            return pk.Stat_Level is > 0 and <= 100 ? pk.Stat_Level : 100; // the first prototype: only the party level says it
        return Experience.GetLevel(pk.EXP, growth);
    }

    /// <summary>
    /// Makes the experience right for a ROM with <paramref name="cap"/> (null: no cap). A Pokémon above the cap — a Rare
    /// Candy ignores it — keeps its level coded, so battles do not move it and the game still shows its level; one at or
    /// under the cap gets the ordinary experience of its level back (the progress within that level is lost).
    /// </summary>
    /// <returns>The level, when the experience had to change.</returns>
    public static int? Fit(PKM pk, byte growth, int? cap)
    {
        int level = LevelOf(pk, growth);
        uint wanted = cap is { } c && level > c ? ExperienceTable.CappedExperience(level) : Experience.GetEXP((byte)level, growth);
        bool coded = pk.EXP >= ExperienceTable.CappedBase;
        if (pk.EXP == wanted || (!coded && wanted == Experience.GetEXP((byte)level, growth)))
            return null; // an ordinary value under the cap is left as the game has it
        pk.EXP = wanted;
        return level;
    }
}
