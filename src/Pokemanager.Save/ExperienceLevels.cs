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
    /// Brings back to the cap anything that got past it. Two ways lead there and the cap is meant to hold both:
    /// a Pokémon held at the cap **goes on earning experience** in battle (the cap is a table, not a rule: the game
    /// adds what each battle gives and simply never finds the next level), so it can be carrying several levels that
    /// would all be cashed in the moment the cap lifts; and a **Rare Candy ignores the cap** altogether, because it
    /// writes the next level's value straight in.
    /// </summary>
    /// <param name="playedCap">The cap of the ROM the save has been played on; null when it had none.</param>
    /// <returns>The level it keeps and the level it was at, when it had to change.</returns>
    /// <remarks>
    /// The floor is the cap **or the level the Pokémon was obtained at**, whichever is higher: the cap holds back
    /// training, it does not shrink what the game handed the player. A wild Pokémon caught at 30 under a cap of 18
    /// stays at 30 (and a candy on it is still undone, 31 → 30); without that floor the pass would destroy it.
    /// </remarks>
    public static (int Level, int Was)? Trim(PKM pk, byte growth, int? playedCap)
    {
        if (playedCap is not { } cap)
            return null;
        if (ExperienceTable.IsPrototype(pk.EXP))
            return null; // the first prototype's values do not say their level: nothing can be decided here

        int level = LevelOf(pk, growth);
        int floor = Math.Max(cap, pk.MetLevel);
        if (level <= floor)
            return null; // under the cap the progress within the level is the player's, and it is kept

        // Above the cap the game's own table holds coded values, so that is what a level above it has to be written as.
        pk.EXP = floor > cap ? ExperienceTable.CappedExperience(floor) : Experience.GetEXP((byte)floor, growth);
        pk.Stat_Level = (byte)floor; // the party shows this one, and the stats are recalculated from the level
        return (floor, level);
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
