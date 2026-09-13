using Pokemanager.Model.Dump;

namespace Pokemanager.Tests.Dump;

/// <summary>
/// Tests against a real Pokémon X dump. Skipped when the environment variable
/// <c>POKEMANAGER_DUMP</c> does not point to a folder with <c>romfs</c> and <c>exefs</c>.
/// </summary>
public class RealDumpTests
{
    private static readonly string? DumpDir = Environment.GetEnvironmentVariable("POKEMANAGER_DUMP");

    private static (string RomFs, string ExeFs) RequireDump()
    {
        Assert.SkipWhen(string.IsNullOrWhiteSpace(DumpDir), "POKEMANAGER_DUMP is not set.");
        string romFs = Path.Combine(DumpDir!, "romfs"), exeFs = Path.Combine(DumpDir!, "exefs");
        Assert.SkipUnless(Directory.Exists(romFs) && Directory.Exists(exeFs), $"{DumpDir} does not contain romfs and exefs.");
        return (romFs, exeFs);
    }

    [Fact]
    public void Open_PokemonX_LoadsModel()
    {
        var (romFs, exeFs) = RequireDump();

        var dump = GameDump.Open(romFs, exeFs, GameLanguage.Spanish);

        Assert.Equal(GameTitle.X, dump.Title);
        var bulbasaur = dump.Config.Personal[1];
        Assert.Equal([45, 49, 49, 45, 65, 65], bulbasaur.Stats); // HP, Atk, Def, Spe, SpA, SpD
        // Game text in the chosen game language (Spanish).
        Assert.Equal("Charizard", dump.Config.GetText(pk3DS.Core.TextName.SpeciesNames)[6]);
        Assert.Equal("Placaje", dump.Config.GetText(pk3DS.Core.TextName.MoveNames)[33]);
    }

    [Fact]
    public void Open_DoesNotModifyDump()
    {
        var (romFs, exeFs) = RequireDump();
        var before = Snapshot(romFs, exeFs);

        _ = GameDump.Open(romFs, exeFs, GameLanguage.Spanish);

        Assert.Equal(before, Snapshot(romFs, exeFs));
    }

    private static List<string> Snapshot(params string[] dirs) =>
        dirs.SelectMany(d => new DirectoryInfo(d).EnumerateFiles("*", SearchOption.AllDirectories))
            .Select(f => $"{f.FullName}|{f.Length}|{f.LastWriteTimeUtc.Ticks}")
            .Order()
            .ToList();
}
