using pk3DS.Core.CTR;

namespace Pokemanager.Model.Data;

/// <summary>
/// The levels of what the player meets — trainers, wild Pokémon, and the fixed ones (legendaries, gifts, totems and
/// their allies) — scaled by a percentage, the way UPR ZX's own level modifiers do it (rounded to the nearest level,
/// never below 1 or above 100).
/// </summary>
/// <remarks>
/// **Measured on the user's Ultra Moon ROMs** (2026-09-19), a ROM randomized twice with UPR ZX's +20 %: every wild
/// table, 250 of the 252 static encounters and every totem level of the second ROM is exactly
/// <c>round(first × 1.2)</c>, so the same rounding undoes it: scaling by −16.67 % gives back the first ROM's levels.
/// </remarks>
public static class GameLevels
{
    /// <summary><paramref name="level"/> raised or lowered by <paramref name="percent"/>; 0 leaves every level alone.</summary>
    public static int Scale(int level, double percent)
    {
        if (percent == 0 || level <= 0)
            return level;
        return Math.Clamp((int)Math.Round(level * (100 + percent) / 100, MidpointRounding.AwayFromZero), 1, 100);
    }

    // ------------------------------------------------------------------ wild, Generation 7

    /// <summary>Area files of the encounter archive: every eleventh, starting at 9 (UPR ZX and pk3DS).</summary>
    public const int FirstArea = 9;

    public const int AreaStride = 11;

    /// <summary>
    /// Each table of an area: a 4-byte header, then the day half and the night half, 0x164 bytes each; a half starts with
    /// its lowest and highest level.
    /// </summary>
    private static readonly int[] TableLevelOffsets = [4, 5, 4 + 0x164, 5 + 0x164];

    private const int TableSize = 4 + (2 * 0x164);

    /// <summary>
    /// Scales the level range of every wild table of a Generation 7 encounter archive, in place. The area files are LZ11
    /// packs of <c>EA</c> tables; only the files whose levels change are written back. Anything that is not an area as
    /// described is left alone.
    /// </summary>
    /// <returns>How many level values changed.</returns>
    public static int ScaleWild7(byte[][] files, double percent)
    {
        if (percent == 0)
            return 0;
        int changed = 0;
        for (int i = FirstArea; i < files.Length; i += AreaStride)
        {
            if (files[i].Length == 0 || files[i][0] != 0x11)
                continue;
            byte[] area;
            try
            {
                area = LZSS.Decompress(files[i]);
            }
            catch (Exception ex) when (ex is InvalidDataException or IndexOutOfRangeException or ArgumentException or EndOfStreamException)
            {
                continue;
            }
            if (Mini.UnpackMini(area, "EA") is not { } tables)
                continue;
            int before = changed;
            foreach (byte[] table in tables.Where(t => t.Length >= TableSize))
            {
                foreach (int offset in TableLevelOffsets)
                {
                    int scaled = Scale(table[offset], percent);
                    if (scaled == table[offset])
                        continue;
                    table[offset] = (byte)scaled;
                    changed++;
                }
            }
            if (changed != before)
                files[i] = LZSS.Compress(Mini.PackMini(tables, "EA"));
        }
        return changed;
    }

    // ------------------------------------------------------------------ fixed Pokémon, Generation 7

    /// <summary>File 0 of the static archive: gifts, 20 bytes each (the three starters first).</summary>
    public const int GiftSize = 20;

    /// <summary>File 1 of the static archive: static encounters, 0x38 bytes each — legendaries, totems and their allies.</summary>
    public const int EncounterSize = 0x38;

    /// <summary>The level byte, in both kinds of entry.</summary>
    private const int LevelOffset = 3;

    /// <summary>
    /// Scales the gifts and the static encounters of a Generation 7 static archive, in place. The three starters and the
    /// eggs (level 1) are left as they are, as UPR ZX leaves them: measured, a ROM with UPR's +20 % still gives its
    /// starters at 5 and its egg at 1.
    /// </summary>
    /// <returns>How many levels changed.</returns>
    public static int ScaleStatics7(byte[][] files, double percent)
    {
        if (percent == 0 || files.Length < 2)
            return 0;
        int changed = 0;
        for (int entry = 3; entry < files[0].Length / GiftSize; entry++)
            changed += ScaleAt(files[0], (entry * GiftSize) + LevelOffset, percent);
        for (int entry = 0; entry < files[1].Length / EncounterSize; entry++)
            changed += ScaleAt(files[1], (entry * EncounterSize) + LevelOffset, percent);
        return changed;
    }

    private static int ScaleAt(byte[] file, int offset, double percent)
    {
        int level = file[offset];
        if (level <= 1)
            return 0;
        int scaled = Scale(level, percent);
        if (scaled == level)
            return 0;
        file[offset] = (byte)scaled;
        return 1;
    }
}
