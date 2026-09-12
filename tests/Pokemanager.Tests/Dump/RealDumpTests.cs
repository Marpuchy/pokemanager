using Pokemanager.Model.Dump;

namespace Pokemanager.Tests.Dump;

/// <summary>
/// Pruebas contra un volcado real de Pokémon X. Se omiten si la variable de entorno
/// <c>POKEMANAGER_DUMP</c> no apunta a una carpeta con <c>romfs</c> y <c>exefs</c>.
/// </summary>
public class RealDumpTests
{
    private static readonly string? DumpDir = Environment.GetEnvironmentVariable("POKEMANAGER_DUMP");

    private static (string RomFs, string ExeFs) RequireDump()
    {
        Assert.SkipWhen(string.IsNullOrWhiteSpace(DumpDir), "POKEMANAGER_DUMP no está definida.");
        string romFs = Path.Combine(DumpDir!, "romfs"), exeFs = Path.Combine(DumpDir!, "exefs");
        Assert.SkipUnless(Directory.Exists(romFs) && Directory.Exists(exeFs), $"{DumpDir} no contiene romfs y exefs.");
        return (romFs, exeFs);
    }

    [Fact]
    public void Open_PokemonX_LoadsModel()
    {
        var (romFs, exeFs) = RequireDump();

        var dump = GameDump.Open(romFs, exeFs, GameLanguage.Spanish);

        Assert.Equal(GameTitle.X, dump.Title);
        var bulbasaur = dump.Config.Personal[1];
        Assert.Equal([45, 49, 49, 45, 65, 65], bulbasaur.Stats); // PS, Atq, Def, Vel, AtqEsp, DefEsp
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
