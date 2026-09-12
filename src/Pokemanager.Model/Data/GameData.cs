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

    public static GameData Load(string romFsPath) => new(
        ReadPersonal(romFsPath),
        ReadFiles(romFsPath, MoveGarc).Select(f => new Move6(f)).ToArray(),
        ReadFiles(romFsPath, LevelUpGarc).Select(f => new Learnset6(f)).ToArray());

    /// <summary>
    /// El GARC de personal tiene una entrada de 0x40 bytes por Pokémon o forma y, como último archivo,
    /// la concatenación de todas ellas. Las entradas individuales son las que se leen.
    /// </summary>
    private static PersonalInfoXY[] ReadPersonal(string romFsPath)
    {
        byte[][] files = ReadFiles(romFsPath, PersonalGarc);
        return files[..^1].Select(f => new PersonalInfoXY(f)).ToArray();
    }

    internal static byte[][] ReadFiles(string romFsPath, string garc) =>
        new GARC.MemGARC(File.ReadAllBytes(RomFsFile(romFsPath, garc))).Files;

    internal static string RomFsFile(string romFsPath, string relative) =>
        Path.Combine(romFsPath, relative.Replace('/', Path.DirectorySeparatorChar));
}
