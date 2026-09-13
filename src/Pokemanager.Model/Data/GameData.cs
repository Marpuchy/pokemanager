using pk3DS.Core.CTR;
using pk3DS.Core.Structures;
using pk3DS.Core.Structures.PersonalInfo;
using Pokemanager.Model.Resources;

namespace Pokemanager.Model.Data;

/// <summary>
/// Editable X/Y tables read directly from their GARCs. Each instance owns its copy of the bytes: modifying it does not
/// affect the dump or other instances.
/// </summary>
public sealed class GameData
{
    public const string PersonalGarc = "a/2/1/8";
    public const string MoveGarc = "a/2/1/2";
    public const string LevelUpGarc = "a/2/1/4";

    public PersonalInfoXY[] Personal { get; }
    public Move6[] Moves { get; }
    public Learnset6[] Learnsets { get; }

    private GameData(PersonalInfoXY[] personal, Move6[] moves, Learnset6[] learnsets)
    {
        Personal = personal;
        Moves = moves;
        Learnsets = learnsets;
    }

    public static GameData Load(string romFsPath) => Load(new RomFsLayers(romFsPath));

    public static GameData Load(RomFsLayers layers) => new(
        ReadPersonal(layers),
        ReadFiles(layers, MoveGarc).Select(f => new Move6(f)).ToArray(),
        ReadFiles(layers, LevelUpGarc).Select(f => new Learnset6(f)).ToArray());

    /// <summary>
    /// The personal GARC has one 0x40-byte entry per Pokémon or form and, as its last file, the concatenation of all
    /// of them. The individual entries are the ones read.
    /// </summary>
    private static PersonalInfoXY[] ReadPersonal(RomFsLayers layers)
    {
        byte[][] files = ReadFiles(layers, PersonalGarc);
        return files[..^1].Select(f => new PersonalInfoXY(f)).ToArray();
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
