using pk3DS.Core.CTR;
using Pokemanager.Model.Dump;

namespace Pokemanager.Model.Data;

/// <summary>
/// The three Pokémon the game offers at the start, and the text that talks about them.
/// </summary>
/// <remarks>
/// **Measured on the user's randomized Ultra Moon**: they are the first three entries of file 0 of the static archive
/// (<c>a/1/5/9</c>; <c>a/1/5/5</c> in Sun/Moon), 20 bytes each with the species in the first two — that ROM reads 498,
/// 602 and 174 at level 5, exactly the three its own English text names. Generation 6 keeps them in a CRO module
/// instead, so it is not offered there.
/// </remarks>
public static class GameStarters
{
    private const int EntrySize = 20;

    /// <summary>The three the game ships with, whatever the ROM now holds.</summary>
    public static int[] Vanilla(GameTitle title) => title.Family() switch
    {
        GameFamily.XY => [650, 653, 656],       // Chespin, Fennekin, Froakie
        GameFamily.ORAS => [252, 255, 258],     // Treecko, Torchic, Mudkip
        _ => [722, 725, 728],                   // Rowlet, Litten, Popplio
    };

    /// <summary>
    /// The three species the ROM gives now, or null when this game keeps them somewhere this application does not read
    /// (Generation 6) or the archive is not in any layer (a game folder imported before this version).
    /// </summary>
    public static int[]? Read(RomFsLayers layers, GameTitle title)
    {
        if (title.Layout().Statics is not { Length: > 0 } path)
            return null;
        try
        {
            var garc = new GARC.MemGARC(File.ReadAllBytes(layers.Resolve(path)));
            byte[] file = garc.GetFile(0);
            if (file.Length < 3 * EntrySize)
                return null;
            int[] starters = [.. Enumerable.Range(0, 3).Select(i => (int)BitConverter.ToUInt16(file, i * EntrySize))];
            return starters.All(s => s > 0) ? starters : null;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or IndexOutOfRangeException
                                       or FileNotFoundException or EndOfStreamException)
        {
            return null;
        }
    }

    /// <summary>
    /// Writes the three starters into file 0 of a static archive (species, form 0), in place.
    /// </summary>
    /// <returns>How many of the three changed.</returns>
    public static int SetStarters(byte[][] files, IReadOnlyList<int> species)
    {
        if (files.Length < 1 || files[0].Length < 3 * EntrySize)
            return 0;
        int changed = 0;
        for (int slot = 0; slot < 3; slot++)
        {
            int at = slot * EntrySize;
            if (BitConverter.ToUInt16(files[0], at) == species[slot] && files[0][at + 2] == 0)
                continue;
            BitConverter.TryWriteBytes(files[0].AsSpan(at, 2), (ushort)species[slot]);
            files[0][at + 2] = 0;
            changed++;
        }
        return changed;
    }

    /// <summary>The three species of an already-read static archive (file 0's first three gifts), or null.</summary>
    public static int[]? Read(GARC.MemGARC garc)
    {
        try
        {
            byte[] file = garc.GetFile(0);
            if (file.Length < 3 * EntrySize)
                return null;
            int[] starters = [.. Enumerable.Range(0, 3).Select(i => (int)BitConverter.ToUInt16(file, i * EntrySize))];
            return starters.All(s => s > 0) ? starters : null;
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or IndexOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>
    /// The three species the starter scene names now, read from the menu at its end (its last three lines are the
    /// three names, measured in Ultra Sun and Ultra Moon in English and Spanish), or null when those lines are not three
    /// species names of this language.
    /// </summary>
    public static int[]? NamedInScene(string[] lines, IReadOnlyList<string> species)
    {
        var menu = lines.Select(l => l.Trim()).Where(l => l.Length > 0).TakeLast(3).ToArray();
        if (menu.Length != 3)
            return null;
        var ids = new int[3];
        for (int slot = 0; slot < 3; slot++)
        {
            int id = -1;
            for (int s = 1; s < species.Count; s++)
            {
                if (string.Equals(species[s], menu[slot], StringComparison.Ordinal))
                {
                    id = s;
                    break;
                }
            }
            if (id < 0)
                return null;
            ids[slot] = id;
        }
        return ids;
    }

    /// <summary>
    /// The type name the scene gives one slot, found in that slot's own lines (the one naming it or the one the game
    /// fills in by slot), the longest that appears; null when none does.
    /// </summary>
    public static string? TypeInScene(string[] lines, int slot, string name, IReadOnlyList<string> types)
    {
        foreach (string line in lines)
        {
            if (!line.Contains(name, StringComparison.Ordinal) && !line.Contains($"[VAR 0101({slot + 1:0000})]", StringComparison.Ordinal))
                continue;
            if (types.Where(t => t.Length > 2 && line.Contains(t, StringComparison.Ordinal)).OrderByDescending(t => t.Length).FirstOrDefault() is { } type)
                return type;
        }
        return null;
    }

    /// <summary>
    /// One of the three the scene talks about: the name it used to say and the one it should say now, and the same
    /// for the type it names ("the Fire-type Pokémon"). A type of null leaves that part alone.
    /// </summary>
    public sealed record StarterSwap(string OldName, string NewName, string? OldType = null, string? NewType = null);

    /// <summary>
    /// The lines of the starter scene with the Pokémon it names put right. Every name is swapped in one pass, so a
    /// name that becomes another slot's old name is not swapped twice; the type is only touched in the line that is
    /// about that slot, which is the one naming it or the one the game fills in by slot ([VAR 0101(0002)]).
    /// </summary>
    /// <returns>How many lines changed.</returns>
    public static int Rewrite(string[] lines, IReadOnlyList<StarterSwap> swaps)
    {
        var byName = swaps.Where(s => s.OldName != s.NewName).ToDictionary(s => s.OldName, s => s.NewName);
        int changed = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            int slot = Slot(lines[i], swaps);
            string line = byName.Count == 0
                ? lines[i]
                : System.Text.RegularExpressions.Regex.Replace(lines[i],
                    string.Join('|', byName.Keys.Select(System.Text.RegularExpressions.Regex.Escape)),
                    m => byName[m.Value]);
            if (slot >= 0 && swaps[slot] is { OldType.Length: > 0, NewType.Length: > 0 } swap && swap.OldType != swap.NewType)
                line = line.Replace(swap.OldType, swap.NewType, StringComparison.Ordinal);
            if (line == lines[i])
                continue;
            lines[i] = line;
            changed++;
        }
        return changed;
    }

    /// <summary>Which of the three a line is about: the one it names, or the slot the game fills in. -1 for none.</summary>
    private static int Slot(string line, IReadOnlyList<StarterSwap> swaps)
    {
        for (int i = 0; i < swaps.Count; i++)
        {
            if (line.Contains(swaps[i].OldName, StringComparison.Ordinal) || line.Contains($"[VAR 0101({i + 1:0000})]", StringComparison.Ordinal))
                return i;
        }
        return -1;
    }
}
