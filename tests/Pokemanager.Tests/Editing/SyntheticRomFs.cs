using pk3DS.Core.CTR;
using Pokemanager.Model.Data;

namespace Pokemanager.Tests.Editing;

/// <summary>
/// Minimal romfs with the three editable GARCs and recognizable data:
/// personal (0x40 entries + final concatenation), move (36-byte files) and levelup.
/// </summary>
internal sealed class SyntheticRomFs : IDisposable
{
    public const int Species = 5;
    public const int MoveCount = 4;

    public string Root { get; } = Directory.CreateTempSubdirectory("pokemanager-romfs-").FullName;
    public string DumpDirectory => Root;
    public string RomFs => Path.Combine(Root, "romfs");

    public SyntheticRomFs()
    {
        byte[][] personal = Enumerable.Range(0, Species).Select(i =>
        {
            var entry = new byte[0x40];
            for (int b = 0; b < 6; b++)
                entry[b] = (byte)(10 * (i + 1) + b); // stats: 10,11,12... per species
            entry[0x06] = 1; entry[0x07] = 2;          // types
            entry[0x18] = (byte)(100 + i * 3);         // abilities 1, 2 and hidden: 100+3i, 101+3i, 102+3i
            entry[0x19] = (byte)(101 + i * 3);
            entry[0x1A] = (byte)(102 + i * 3);
            return entry;
        }).ToArray();
        Write(GameData.PersonalGarc, [.. personal, personal.SelectMany(p => p).ToArray()]);

        Write(GameData.MoveGarc, Enumerable.Range(0, MoveCount).Select(i =>
        {
            var move = new byte[36];
            move[0x03] = (byte)(40 + i); // power
            move[0x05] = 35;             // PP
            return move;
        }).ToArray());

        // Learnset i: move 1 at level 1 and move 2 at level 5, with a -1 terminator.
        byte[] learnset = [1, 0, 1, 0, 2, 0, 5, 0, 0xFF, 0xFF, 0xFF, 0xFF];
        Write(GameData.LevelUpGarc, Enumerable.Repeat(learnset, Species).ToArray());
    }

    public byte[][] ReadGarc(string directory, string garc) =>
        new GARC.MemGARC(File.ReadAllBytes(Path.Combine(directory, garc.Replace('/', Path.DirectorySeparatorChar)))).Files;

    private void Write(string garc, byte[][] files)
    {
        string path = Path.Combine(RomFs, garc.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, GARC.PackGARC(files, GARC.VER_4, 4).Data);
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch (IOException) { }
    }
}
