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
    /// What every level above the cap needs: far beyond anything a Pokémon can earn (the most any growth rate asks for
    /// level 100 is 1 640 000), and still growing by one per level, so the table keeps going up — code that measures
    /// the gap between two levels never divides by zero.
    /// </summary>
    public const uint Unreachable = 0x40000000;

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
                BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(level * 4), Unreachable + (uint)(level - cap));
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
        return Read(file, Levels - 1) < Unreachable;
    }
}
