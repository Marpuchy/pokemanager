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

    private GameData(GameTitle title, PersonalInfoXY[] personal, Move[] moves, Learnset6[] learnsets, string[] moveDescriptions)
    {
        Title = title;
        Personal = personal;
        Moves = moves;
        Learnsets = learnsets;
        MoveDescriptions = moveDescriptions;
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
            ReadMoveDescriptions(layers, title, moves.Length));
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
