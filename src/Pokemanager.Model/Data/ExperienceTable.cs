using System.Buffers.Binary;

namespace Pokemanager.Model.Data;

/// <summary>
/// The game's experience table: one file per growth rate, 101 <c>u32</c> — the total experience a Pokémon needs to be
/// at each level, 0 to 100. **Found by its values in the real games** (Medium Fast is n³: 0, 0, 8, 27, 64, 125 …
/// 1 000 000): X <c>a/2/1/7</c>, Alpha Sapphire <c>a/1/9/4</c>, Ultra Moon <c>a/0/1/6</c> — always the archive just
/// before the personal table. X has eight files: the six growth rates and two copies of Medium Fast.
/// </summary>
public static class ExperienceTable
{
    public const int Levels = 101;

    /// <summary>
    /// Where the levels above the cap start: far beyond anything a Pokémon can earn (the most any growth rate asks for
    /// level 100 is 1 640 000).
    /// </summary>
    /// <remarks>
    /// Level <c>n</c> above the cap needs <c>CappedBase + n × CappedStep</c>. **The gap between two of them has to be
    /// huge**: a Rare Candy ignores the cap — it sets the experience of the next level — and the first prototype (levels
    /// one experience point apart above <c>0x40000000</c>) would then have sent the Pokémon from the cap to 100 in its
    /// next battle (found on the user's Ultra Moon, 2026-09-19: three candies took a level-17 Tepig to 20 with a cap of
    /// 18). With 16.7 million between levels a candy moves one level and battles move none. The value also **says the
    /// level** (<see cref="LevelOfCapped"/>), so a save read with the normal table can still be understood, and the
    /// biggest one (level 100) stays below 2³¹.
    /// </remarks>
    public const uint CappedBase = 0x10000000;

    public const uint CappedStep = 0x01000000;

    /// <summary>
    /// The first prototype's region: <c>0x40000000 + (level - cap)</c>, so its values sit just above the base. **Level 48
    /// of the encoding is exactly <c>0x40000000</c>**, so the two cannot be told apart by "above this base": a coded value
    /// is a whole number of steps above <see cref="CappedBase"/> and a prototype one never is (it is off by 1 to 99).
    /// Reading a coded level 48 or more as a prototype value was what put six of the user's boxed Pokémon at level 100
    /// (2026-09-21): in a box nothing else says the level, so the fallback answered 100 and the next build wrote it.
    /// </summary>
    public const uint PrototypeBase = 0x40000000;

    /// <summary>
    /// The most experience a growth rate asks for level 100, measured in the real X dump (1 640 000, the Fluctuating
    /// rate; the biggest step between two levels is 68 116). A Pokémon the cap is holding **goes on earning experience
    /// in battle**, which the game adds to the coded value — so a coded level has to be read through that much noise.
    /// <see cref="CappedStep"/> is ten times this, so two coded levels can never be confused.
    /// </summary>
    public const uint MostOrdinaryExperience = 1_640_000;

    /// <summary>Whether this is one of the first prototype's values, which do not say their level.</summary>
    public static bool IsPrototype(uint exp) => InPrototypeWindow(exp);

    /// <summary>
    /// The prototype wrote <c>0x40000000 + (level - cap)</c>, which is a coded level 48 plus 1 to 99. Those few values
    /// belong to it, and a real level 48 that has earned only that much experience is not something a battle produces.
    /// </summary>
    private static bool InPrototypeWindow(uint exp) => exp > PrototypeBase && exp <= PrototypeBase + Levels;

    /// <summary>The level an experience value above the cap stands for, or null when it is an ordinary one.</summary>
    /// <remarks>
    /// The value is read **through the experience earned since**: a Pokémon held at the cap keeps winning battles and
    /// the game keeps adding to what it holds, so requiring an exact multiple of <see cref="CappedStep"/> made one
    /// battle turn a capped Pokémon into level 100 — the fallback for a value nothing else explains.
    /// </remarks>
    public static int? LevelOfCapped(uint exp)
    {
        if (exp < CappedBase || InPrototypeWindow(exp))
            return null;
        uint offset = exp - CappedBase;
        uint level = offset / CappedStep, earnedSince = offset % CappedStep;
        return level is >= 1 and <= Levels - 1 && earnedSince <= MostOrdinaryExperience ? (int)level : null;
    }

    /// <summary>The experience that keeps a Pokémon at <paramref name="level"/> above the cap.</summary>
    public static uint CappedExperience(int level) => CappedBase + ((uint)Math.Clamp(level, 1, 100) * CappedStep);

    /// <summary>The total experience of <paramref name="level"/> in one growth rate file.</summary>
    public static uint Read(byte[] file, int level) => BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(level * 4));

    /// <summary>
    /// Makes every level above <paramref name="cap"/> impossible to reach by experience, in every growth rate. The files
    /// are checked first — the right size and going up level by level, as the game ships them — and nothing is changed
    /// unless all of them are, so a wrong archive is left alone instead of broken.
    /// </summary>
    /// <returns>Whether the table was changed.</returns>
    public static bool Cap(byte[][] files, int cap)
    {
        if (cap is < 1 or >= Levels - 1 || files.Length == 0 || !files.All(IsTable))
            return false;
        foreach (byte[] file in files)
        {
            for (int level = cap + 1; level < Levels; level++)
                BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(level * 4), CappedExperience(level));
        }
        return true;
    }

    /// <summary>A growth rate as the game has it: 101 values, 0 at levels 0 and 1, then never going down.</summary>
    public static bool IsTable(byte[] file)
    {
        if (file.Length < Levels * 4 || Read(file, 0) != 0 || Read(file, 1) != 0)
            return false;
        for (int level = 2; level < Levels; level++)
        {
            if (Read(file, level) <= Read(file, level - 1))
                return false;
        }
        return Read(file, Levels - 1) < CappedBase;
    }
}
