using pk3DS.Core.CTR;
using pk3DS.Core.Structures;
using Pokemanager.Model.Data;
using Pokemanager.Model.Dump;
using Pokemanager.Model.Editing;
using Pokemanager.Model.Edits;

namespace Pokemanager.Model.Build;

/// <summary>Computes the GARCs changed by the manual edits.</summary>
/// <remarks>
/// Only GARCs of tables with edits are produced. Inside each GARC only edited entries change; everything else keeps
/// the bytes of the base (dump or randomized dump).
/// </remarks>
public static class ModBuilder
{
    /// <summary>The key of the executable among the outputs; everything else is a <c>romfs/</c> path.</summary>
    public const string CodeFile = "code.bin";

    /// <param name="randomizedTitleDirectory">
    /// UPR ZX output, when the ROM is being randomized: its <c>code.bin</c> is the one the shop changes are made on, so
    /// they land on top of the randomization like every other edit.
    /// </param>
    /// <returns>Relative path inside the title (<c>romfs/a/2/1/8</c>, or <see cref="CodeFile"/>) → contents.</returns>
    public static IReadOnlyDictionary<string, byte[]> BuildEdits(EditorSession session, string? randomizedTitleDirectory = null)
    {
        var edits = session.Project.Edits;
        var layout = session.Current.Layout;
        var outputs = new Dictionary<string, byte[]>();

        if (EditedIds(edits, GameTables.Personal) is { Count: > 0 } personalIds)
            outputs["romfs/" + layout.Personal] = BuildPersonal(session, personalIds);
        if (EditedIds(edits, GameTables.Moves) is { Count: > 0 } moveIds)
            outputs["romfs/" + layout.Moves] = BuildMoves(session, moveIds);
        if (EditedIds(edits, GameTables.Learnsets) is { Count: > 0 } learnsetIds)
            outputs["romfs/" + layout.LevelUp] = BuildGarc(session, layout.LevelUp, learnsetIds, id => session.Current.Learnsets[id].Write());
        // Trainers: the record and the team live in two archives, both written when either changes.
        if (EditedIds(edits, GameTables.Trainers) is { Count: > 0 } trainerIds)
        {
            outputs["romfs/" + layout.TrainerData] =
                BuildGarc(session, layout.TrainerData, trainerIds, id => session.Current.Trainers[id].WriteData());
            outputs["romfs/" + layout.TrainerPokemon] =
                BuildGarc(session, layout.TrainerPokemon, trainerIds, id => session.Current.Trainers[id].WriteTeam());
        }
        if (EditedIds(edits, GameTables.Items) is { Count: > 0 } itemIds)
            outputs["romfs/" + layout.Items] = BuildGarc(session, layout.Items, itemIds, id => session.Current.Items[id].Write());
        if (EditedIds(edits, GameTables.MoveTexts) is { Count: > 0 } descriptionIds)
        {
            foreach (var (path, data) in BuildMoveDescriptions(session, descriptionIds))
                outputs["romfs/" + path] = data;
        }

        BuildShops(session, randomizedTitleDirectory, outputs);
        BuildGameSettings(session, randomizedTitleDirectory, outputs);
        BuildStarterText(session, outputs);
        return outputs;
    }

    /// <summary>
    /// The project's shop changes: the items are put in the ordinary Poké Marts of <c>code.bin</c>, and free Rare
    /// Candies also need their price in the item table. A hand edit of that price wins, as everywhere else.
    /// </summary>
    private static void BuildShops(EditorSession session, string? randomizedTitleDirectory, Dictionary<string, byte[]> outputs)
    {
        var shops = session.Project.Shops;
        if (shops.IsEmpty)
            return;

        // The price lives in the item table; write it unless the user set that price by hand.
        if (shops.FreeRareCandies && !session.Project.Edits.All.Any(e => e.Table == GameTables.Items && e.Id == GameShops.RareCandy))
        {
            string path = session.Current.Layout.Items;
            var garc = outputs.TryGetValue("romfs/" + path, out byte[]? built) ? new GARC.MemGARC(built) : ReadGarc(session, path);
            byte[][] files = garc.Files;
            if (GameShops.RareCandy < files.Length)
            {
                var item = new Item(files[GameShops.RareCandy]) { BuyPrice = 0 };
                files[GameShops.RareCandy] = item.Write();
                outputs["romfs/" + path] = GARC.PackGARC(files, garc.Version, garc.ContentPadding).Data;
            }
        }

        if (GameShops.For(session.Current.Title) is not { } layout)
            return;
        // Generation 6 keeps the table in the executable; Generation 7 in a romfs module of its own.
        byte[]? data = layout.File is { } file ? ReadRomFsFile(session, file) : ReadCode(session, randomizedTitleDirectory);
        if (data is null)
            return;

        // Generation 6 finds the table by the ids the game ships in it, and a randomization that rewrites the shops can
        // take those away. The game's own file always has them, and the randomizer does not move anything (verified: the
        // executable keeps its length and only the values change), so the offset is looked for there and used here.
        int? offset = GameShops.Find(data, layout, session.Current.Items.Length);
        if (offset is null && layout.File is null && ReadCode(session, null) is { } original && original.Length == data.Length)
            offset = GameShops.Find(original, layout, session.Current.Items.Length);
        if (offset is null)
            return;

        var table = GameShops.Read(data, offset.Value, layout);
        List<int> everywhere = shops.FreeRareCandies ? [GameShops.RareCandy] : [];
        if (GameShops.AddToRegularShops(table, layout, everywhere) == 0)
            return;
        GameShops.Write(data, offset.Value, layout, table);
        outputs[layout.File is { } shopFile ? "romfs/" + shopFile : CodeFile] = data;
    }

    /// <summary>
    /// Generation 7: the scene where the player picks a starter names the three of them in its text, and a randomization
    /// rewrites **only the English one** (measured in the user's Ultra Moon: the English file names the randomized three,
    /// the Spanish one still says Rowlet, Litten and Popplio). So the game tells a Spanish player they are choosing
    /// Litten and hands them something else. Every language's scene is rewritten here with the species the ROM gives:
    /// each name the game ships with is swapped for the one in the same slot, which leaves the sentence alone.
    /// </summary>
    private static void BuildStarterText(EditorSession session, Dictionary<string, byte[]> outputs)
    {
        var title = session.Current.Title;
        var layout = title.Layout();
        if (layout.StarterTextFile < 0 || GameStarters.Read(session.Layers, title) is not { } starters)
            return;
        int[] shipped = GameStarters.Vanilla(title);
        if (starters.SequenceEqual(shipped))
            return;

        for (int language = 0; language < layout.LanguageCount; language++)
        {
            if (GameText.StoryArchive(title, language) is not { } storyPath)
                return;
            var names = ReadAnywhere(session, GameText.Archive(title, language));
            var story = ReadAnywhere(session, storyPath);
            if (names is null || story is null)
                continue;

            string[] lines;
            List<GameStarters.StarterSwap> swaps;
            try
            {
                string[] species = GameText.Read(title, names.Files[GameText.SpeciesNameFile(title)]);
                string[] types = TypeNames(title, names);
                swaps = [];
                for (int slot = 0; slot < shipped.Length; slot++)
                {
                    if (shipped[slot] >= species.Length || starters[slot] >= species.Length)
                        continue;
                    swaps.Add(new GameStarters.StarterSwap(species[shipped[slot]], species[starters[slot]],
                        TypeName(types, TypeOf(session, shipped[slot])), TypeName(types, TypeOf(session, starters[slot]))));
                }
                lines = GameText.Read(title, story.Files[layout.StarterTextFile]);
            }
            catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentOutOfRangeException or InvalidDataException)
            {
                continue; // a language whose files are not what this expects
            }

            if (GameStarters.Rewrite(lines, swaps) == 0)
                continue;
            byte[][] files = story.Files;
            files[layout.StarterTextFile] = GameText.Write(title, lines);
            outputs["romfs/" + storyPath] = GARC.PackGARC(files, story.Version, story.ContentPadding).Data;
        }
    }

    /// <summary>The first type of a species as the ROM has it now (a randomization may have changed it).</summary>
    private static int TypeOf(EditorSession session, int species) =>
        species > 0 && species < session.Current.Personal.Length ? session.Current.Personal[species].Types[0] : -1;

    private static string? TypeName(string[] types, int type) => (uint)type < types.Length ? types[type] : null;

    /// <summary>The type names of a language, or an empty list when that text is not where it is expected.</summary>
    private static string[] TypeNames(GameTitle title, GARC.MemGARC names)
    {
        try
        {
            int file = GameText.TypeNameFile(title);
            return file < 0 ? [] : GameText.Read(title, names.Files[file]);
        }
        catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentOutOfRangeException or InvalidDataException)
        {
            return [];
        }
    }
    /// <summary>
    /// A GARC from the session's layers or, when no layer has it, straight out of the base ROM — the story text is far
    /// too big to keep a copy of and is only needed while a ROM is being built.
    /// </summary>
    private static GARC.MemGARC? ReadAnywhere(EditorSession session, string path)
    {
        try
        {
            return ReadGarc(session, path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
        }
        try
        {
            if (session.Project.ResolveRomFile() is not { } rom || !File.Exists(rom))
                return null;
            using var reader = RomReader.Open(rom);
            return reader.ReadRomFs(path) is { } data ? new GARC.MemGARC(data) : null;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or RomReadException
                                       or IndexOutOfRangeException or EndOfStreamException)
        {
            return null;
        }
    }

    /// <summary>
    /// How the game itself behaves: changes to the executable that are found by the bytes around them. The shops may
    /// have written it already, so the buffer they left is the one that is changed.
    /// </summary>
    private static void BuildGameSettings(EditorSession session, string? randomizedTitleDirectory, Dictionary<string, byte[]> outputs)
    {
        var game = session.Project.Tweaks;
        BuildLevelCap(session, outputs);
        if (!game.AlwaysShiny)
            return;
        byte[]? code = outputs.TryGetValue(CodeFile, out byte[]? built) ? built : ReadCode(session, randomizedTitleDirectory);
        if (code is null)
            return;
        if (game.AlwaysShiny && GameCode.MakeEveryPokemonShiny(code))
            outputs[CodeFile] = code;
    }

    /// <summary>The level cap, written into the experience table: every level above it needs more than can be earned.</summary>
    private static void BuildLevelCap(EditorSession session, Dictionary<string, byte[]> outputs)
    {
        if (session.Project.Tweaks.LevelCap is not { } cap)
            return;
        string path = session.Current.Title.Layout().Experience;
        if (path.Length == 0 || !File.Exists(session.Layers.Resolve(path)))
            return;
        var garc = ReadGarc(session, path);
        byte[][] files = garc.Files; // read once: every read of Files is a new copy
        if (!ExperienceTable.Cap(files, cap))
            return;
        outputs["romfs/" + path] = GARC.PackGARC(files, garc.Version, garc.ContentPadding).Data;
    }

    /// <summary>
    /// A romfs file that is not a GARC, through the session's layers: the randomizer's copy when it made one, the game
    /// folder's otherwise. Null when the game folder does not have it (imported before this version).
    /// </summary>
    private static byte[]? ReadRomFsFile(EditorSession session, string path)
    {
        try
        {
            return File.ReadAllBytes(session.Layers.Resolve(path));
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    /// <summary>The executable the build starts from: the randomizer's when it ran, the game folder's otherwise.</summary>
    private static byte[]? ReadCode(EditorSession session, string? randomizedTitleDirectory)
    {
        string[] candidates =
        [
            .. randomizedTitleDirectory is null ? Array.Empty<string>() : [Path.Combine(randomizedTitleDirectory, CodeFile)],
            Path.Combine(session.Project.DumpDirectory, "exefs", CodeFile),
        ];
        foreach (string path in candidates)
        {
            if (File.Exists(path))
                return File.ReadAllBytes(path);
        }
        return null;
    }

    /// <summary>
    /// Edited move descriptions go to the text archive of every language the game has, so the game shows them in whatever
    /// language it runs; the other descriptions of each language are left as they are.
    /// </summary>
    private static IEnumerable<(string Path, byte[] Data)> BuildMoveDescriptions(EditorSession session, HashSet<int> ids)
    {
        var title = session.Current.Title;
        int file = GameText.MoveDescriptionFile(title);
        for (int language = 0; language < title.Layout().LanguageCount; language++)
        {
            string path = GameText.Archive(title, language);
            GARC.MemGARC garc;
            string[] lines;
            try
            {
                garc = ReadGarc(session, path);
                lines = GameText.Read(title, garc.Files[file]);
            }
            catch (Exception ex) when (ex is FileNotFoundException or IndexOutOfRangeException or InvalidDataException)
            {
                continue; // a language the dump does not have
            }
            foreach (int id in ids.Where(id => id < lines.Length))
                lines[id] = GameText.FromEditable(session.Current.MoveDescriptions[id]);
            byte[][] files = garc.Files;
            files[file] = GameText.Write(title, lines);
            yield return (path, GARC.PackGARC(files, garc.Version, garc.ContentPadding).Data);
        }
    }

    private static HashSet<int> EditedIds(EditSet edits, string table) =>
        edits.All.Where(e => e.Table == table).Select(e => e.Id).ToHashSet();

    /// <summary>Personal: edited individual entries plus the concatenated table in the last file.</summary>
    private static byte[] BuildPersonal(EditorSession session, HashSet<int> ids)
    {
        string path = session.Current.Layout.Personal;
        var garc = ReadGarc(session, path);
        byte[][] files = garc.Files;
        foreach (int id in ids)
            files[id] = session.Current.Personal[id].Write().ToArray();
        files[^1] = files[..^1].SelectMany(f => f).ToArray();
        return GARC.PackGARC(files, garc.Version, garc.ContentPadding).Data;
    }

    /// <summary>Moves: edited files, or the edited entries of the "WD" mini archive repacked into its single file.</summary>
    private static byte[] BuildMoves(EditorSession session, HashSet<int> ids)
    {
        var layout = session.Current.Layout;
        if (!layout.MovesPacked)
            return BuildGarc(session, layout.Moves, ids, id => session.Current.Moves[id].Write());

        var garc = ReadGarc(session, layout.Moves);
        byte[][] files = garc.Files;
        byte[][] moves = Mini.UnpackMini(files[0], "WD");
        foreach (int id in ids)
            moves[id] = session.Current.Moves[id].Write().ToArray();
        files[0] = Mini.PackMini(moves, "WD");
        return GARC.PackGARC(files, garc.Version, garc.ContentPadding).Data;
    }

    private static byte[] BuildGarc(EditorSession session, string path, HashSet<int> ids, Func<int, byte[]> write)
    {
        var garc = ReadGarc(session, path);
        byte[][] files = garc.Files;
        foreach (int id in ids)
            files[id] = write(id).ToArray();
        return GARC.PackGARC(files, garc.Version, garc.ContentPadding).Data;
    }

    private static GARC.MemGARC ReadGarc(EditorSession session, string path) =>
        new(File.ReadAllBytes(session.Layers.Resolve(path)));
}
