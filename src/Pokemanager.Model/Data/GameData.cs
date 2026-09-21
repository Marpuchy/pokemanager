using pk3DS.Core.CTR;
using Pokemanager.Model.Dump;
using pk3DS.Core.Structures;
using pk3DS.Core.Structures.PersonalInfo;
using Pokemanager.Model.Resources;

namespace Pokemanager.Model.Data;

/// <summary>
/// Editable tables (personal, moves, learnsets) read directly from the game's GARCs. Each instance owns its copy of the
/// bytes: modifying it does not affect the dump or other instances.
/// </summary>
/// <remarks>
/// The formats differ by family: personal entries are 0x40 (X/Y), 0x50 (ORAS) or 0x54 bytes (Gen 7), all readable as
/// <see cref="PersonalInfoXY"/>; moves are one file each in X/Y and a single "WD" mini archive elsewhere, with
/// <see cref="Move6"/> in Gen 6 and <see cref="Move7"/> in Gen 7; learnsets share <see cref="Learnset6"/>.
/// </remarks>
public sealed class GameData
{
    /// <summary>X/Y paths, kept for existing callers; other games use <see cref="Layout"/>.</summary>
    public const string PersonalGarc = "a/2/1/8";
    public const string MoveGarc = "a/2/1/2";
    public const string LevelUpGarc = "a/2/1/4";

    public GameTitle Title { get; }
    public GameLayout Layout => Title.Layout();
    public PersonalInfoXY[] Personal { get; }
    public Move[] Moves { get; }
    public Learnset6[] Learnsets { get; }

    /// <summary>
    /// Description of each move as edited in the app (<see cref="GameText"/> editable form). The base is the game's English
    /// text, whatever language the app or the game uses; an edited description goes to every language of the game.
    /// </summary>
    public string[] MoveDescriptions { get; }

    /// <summary>
    /// Every trainer of the game, in <c>trdata</c> order. Empty when the game folder was imported before 3.0 and does
    /// not have the trainer archives yet (<see cref="GameImporter.Complete"/> adds them when the project is opened).
    /// </summary>
    public Trainer[] Trainers { get; }

    /// <summary>
    /// One entry per item: what it costs and what it does. The entry is 36 bytes in Generation 6 (measured on the real
    /// X dump: 718 items) and pk3DS's <see cref="Item"/> reads it; entries of another size are left alone.
    /// </summary>
    public Item[] Items { get; }

    /// <summary>
    /// The Mega Stones of this game, read from its own mega evolution table (the item each one needs), sorted and
    /// without repeats. Empty when the game folder does not have that archive or nothing in it looks like a stone.
    /// </summary>
    public int[] MegaStones { get; }

    /// <summary>
    /// The stone each species needs to mega evolve, from the same table (file <c>n</c> of the archive is species
    /// <c>n</c>). A species with two mega evolutions (Charizard, Mewtwo) keeps the first stone the table names.
    /// </summary>
    public IReadOnlyDictionary<int, int> MegaStoneBySpecies { get; }

    private GameData(GameTitle title, PersonalInfoXY[] personal, Move[] moves, Learnset6[] learnsets, string[] moveDescriptions,
        Trainer[] trainers, Item[] items, (int[] Stones, Dictionary<int, int> BySpecies) mega)
    {
        Title = title;
        Personal = personal;
        Moves = moves;
        Learnsets = learnsets;
        MoveDescriptions = moveDescriptions;
        Trainers = trainers;
        Items = items;
        MegaStones = mega.Stones;
        MegaStoneBySpecies = mega.BySpecies;
    }

    public static GameData Load(string romFsPath, GameTitle title = GameTitle.X) => Load(new RomFsLayers(romFsPath), title);

    public static GameData Load(RomFsLayers layers, GameTitle title = GameTitle.X)
    {
        var layout = title.Layout();
        var moves = ReadMoveFiles(layers, title).Select(f => title.Generation() == 6 ? (Move)new Move6(f) : new Move7(f)).ToArray();
        return new GameData(title,
            ReadPersonal(layers, title),
            moves,
            ReadFiles(layers, layout.LevelUp).Select(f => new Learnset6(f)).ToArray(),
            ReadMoveDescriptions(layers, title, moves.Length),
            TrainerArchive.Read(layers, title),
            ReadItems(layers, title),
            ReadMegaStones(layers, title));
    }

    /// <summary>
    /// The item table, or an empty array when the game folder does not have it (imported before 3.0) or the entries are
    /// not the size this application knows.
    /// </summary>
    private static Item[] ReadItems(RomFsLayers layers, GameTitle title)
    {
        try
        {
            byte[][] files = ReadFiles(layers, title.Layout().Items);
            int size = System.Runtime.InteropServices.Marshal.SizeOf<Item>();
            return files.All(f => f.Length == size) ? [.. files.Select(f => new Item(f))] : [];
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or InvalidDataException or ArgumentException)
        {
            return [];
        }
    }

    /// <summary>
    /// The item of every mega evolution the game defines. pk3DS reads the archive as 8-byte entries of form, method,
    /// argument and a spare; method 1 is "hold this item", and the argument is the stone. Verified on the real X dump:
    /// 30 stones, exactly the items whose names end in -ite minus the Eviolite, which is not one.
    /// </summary>
    private static (int[] Stones, Dictionary<int, int> BySpecies) ReadMegaStones(RomFsLayers layers, GameTitle title)
    {
        try
        {
            int items = ReadItems(layers, title).Length;
            if (items == 0)
                return ([], []);
            var stones = new SortedSet<int>();
            var bySpecies = new Dictionary<int, int>();
            byte[][] files = ReadFiles(layers, title.Layout().MegaEvolutions);
            for (int species = 0; species < files.Length; species++)
            {
                var mega = new MegaEvolutions(files[species]);
                if (mega.Method is null)
                    continue;
                for (int i = 0; i < mega.Method.Length; i++)
                {
                    if (mega.Method[i] != 1 || mega.Argument[i] == 0 || mega.Argument[i] >= items)
                        continue;
                    stones.Add(mega.Argument[i]);
                    bySpecies.TryAdd(species, mega.Argument[i]);
                }
            }
            return ([.. stones], bySpecies);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or InvalidDataException or ArgumentException)
        {
            return ([], []);
        }
    }

    /// <summary>The English move descriptions (empty when the game text is not in the dump).</summary>
    private static string[] ReadMoveDescriptions(RomFsLayers layers, GameTitle title, int count)
    {
        string[] lines = [];
        try
        {
            string garc = GameText.Archive(title, GameLanguage.English);
            lines = GameText.Read(title, ReadFiles(layers, garc)[GameText.MoveDescriptionFile(title)]);
        }
        catch (Exception ex) when (ex is FileNotFoundException or IndexOutOfRangeException or InvalidDataException)
        {
        }
        return Enumerable.Range(0, count).Select(i => i < lines.Length ? GameText.ToEditable(lines[i]).TrimEnd() : "").ToArray();
    }

    /// <summary>
    /// The personal GARC has one entry per Pokémon or form and, as its last file, the concatenation of all of them. The
    /// individual entries are the ones read.
    /// </summary>
    private static PersonalInfoXY[] ReadPersonal(RomFsLayers layers, GameTitle title)
    {
        byte[][] files = ReadFiles(layers, title.Layout().Personal);
        return files[..^1].Select(f => title.Family() switch
        {
            GameFamily.XY => new PersonalInfoXY(f),
            GameFamily.ORAS => (PersonalInfoXY)new PersonalInfoORAS(f),
            _ => new PersonalInfoSM(f),
        }).ToArray();
    }

    /// <summary>One byte array per move, unpacking the "WD" mini archive where the game uses one.</summary>
    internal static byte[][] ReadMoveFiles(RomFsLayers layers, GameTitle title)
    {
        var layout = title.Layout();
        byte[][] files = ReadFiles(layers, layout.Moves);
        return layout.MovesPacked ? Mini.UnpackMini(files[0], "WD") : files;
    }

    internal static byte[][] ReadFiles(RomFsLayers layers, string garc) =>
        new GARC.MemGARC(File.ReadAllBytes(layers.Resolve(garc))).Files;
}

/// <summary>
/// A romfs made of stacked layers, highest first: each file is taken from the first layer that has it. Same principle
/// as the emulator's LayeredFS.
/// </summary>
public sealed class RomFsLayers
{
    public IReadOnlyList<string> Roots { get; }

    /// <param name="roots">romfs folders, highest priority first. The last one is usually the dump.</param>
    public RomFsLayers(params string[] roots)
    {
        if (roots.Length == 0)
            throw new ArgumentException(Strings.Layers_NeedOne, nameof(roots));
        Roots = roots.Select(Path.GetFullPath).ToArray();
    }

    /// <summary>Physical path of the relative file (<c>a/2/1/8</c>) in the highest layer that contains it.</summary>
    public string Resolve(string relative)
    {
        string native = relative.Replace('/', Path.DirectorySeparatorChar);
        foreach (string root in Roots)
        {
            string path = Path.Combine(root, native);
            if (File.Exists(path))
                return path;
        }
        throw new FileNotFoundException(string.Format(Strings.Layers_NotFound, relative), relative);
    }
}
