using pk3DS.Core.CTR;
using pk3DS.Core.Structures;
using pk3DS.Core.Structures.PersonalInfo;

namespace Pokemanager.Model.Data;

/// <summary>
/// Tablas editables de X/Y leídas directamente de sus GARC. Cada instancia tiene sus propias copias
/// de los bytes: modificarla no afecta al volcado ni a otras instancias.
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
    /// El GARC de personal tiene una entrada de 0x40 bytes por Pokémon o forma y, como último archivo,
    /// la concatenación de todas ellas. Las entradas individuales son las que se leen.
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
/// Romfs formado por capas superpuestas, de la más alta a la más baja: cada archivo se toma de la
/// primera capa que lo tenga. Es el mismo principio que LayeredFS del emulador.
/// </summary>
public sealed class RomFsLayers
{
    public IReadOnlyList<string> Roots { get; }

    /// <param name="roots">Carpetas romfs, de mayor a menor prioridad. La última suele ser el volcado.</param>
    public RomFsLayers(params string[] roots)
    {
        if (roots.Length == 0)
            throw new ArgumentException("Hace falta al menos una carpeta romfs.", nameof(roots));
        Roots = roots.Select(Path.GetFullPath).ToArray();
    }

    /// <summary>Ruta física del archivo relativo (<c>a/2/1/8</c>) en la capa más alta que lo contenga.</summary>
    public string Resolve(string relative)
    {
        string native = relative.Replace('/', Path.DirectorySeparatorChar);
        foreach (string root in Roots)
        {
            string path = Path.Combine(root, native);
            if (File.Exists(path))
                return path;
        }
        throw new FileNotFoundException($"{relative} no está en ninguna capa del romfs.", relative);
    }
}
