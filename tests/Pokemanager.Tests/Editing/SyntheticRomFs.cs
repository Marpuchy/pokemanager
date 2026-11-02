using System.Buffers.Binary;
using pk3DS.Core.CTR;
using Pokemanager.Model.Data;
using Pokemanager.Model.Dump;

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

    /// <summary>The Mega Stones this romfs was built with, sorted and without repeats, as the reader returns them.</summary>
    public int[] MegaStones { get; }

    /// <param name="megaStones">
    /// Item ids to put in the mega evolution table, which also writes the item table the reader needs to bound them.
    /// Null (the default) leaves both archives out, as they were before: the games that have them come from a dump.
    /// </param>
    public SyntheticRomFs(int[]? megaStones = null)
    {
        MegaStones = megaStones?.Distinct().Order().ToArray() ?? [];
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

        // Game text, one archive per language (a/0/7/2 …): the move descriptions file says "Move i (language l)" on two lines.
        int descriptions = GameText.MoveDescriptionFile(GameTitle.X);
        for (int language = 0; language < GameTitle.X.Layout().LanguageCount; language++)
        {
            var files = Enumerable.Range(0, descriptions + 2).Select(_ => GameText.Write(GameTitle.X, ["text"])).ToArray();
            files[descriptions] = GameText.Write(GameTitle.X, [.. Enumerable.Range(0, MoveCount).Select(i => Description(i, language))]);
            Write(GameText.Archive(GameTitle.X, language), files);
        }

        if (megaStones is { Length: > 0 })
            WriteMegaEvolutions(megaStones);
    }

    /// <summary>
    /// The item table and the mega evolution table, the two archives <c>GameData.ReadMegaStones</c> reads: one 36-byte
    /// file per item (only the count matters, it bounds the stone ids) and, per species, two 8-byte entries of form,
    /// method, argument and a spare — method 1 is "hold this item" and the argument is the stone.
    /// </summary>
    private void WriteMegaEvolutions(int[] stones)
    {
        var layout = GameTitle.X.Layout();
        int item = System.Runtime.InteropServices.Marshal.SizeOf<pk3DS.Core.Structures.Item>();
        Write(layout.Items, [.. Enumerable.Range(0, stones.Max() + 1).Select(_ => new byte[item])]);

        Write(layout.MegaEvolutions, [.. stones.Select(stone =>
        {
            var file = new byte[16]; // two entries: the reader ignores anything shorter
            BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(0), 1);              // form
            BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(2), 1);              // method: hold this item
            BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(4), (ushort)stone);  // argument: the stone
            return file;
        })]);
    }

    /// <summary>A move description as the game file has it (pk3DS syntax).</summary>
    public static string Description(int move, int language) => $@"Move {move}\n(language {language})";

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
