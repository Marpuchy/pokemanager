using System.Text.Json;
using Pokemanager.Model.Data;
using Pokemanager.Model.Editing;
using Pokemanager.Model.Projects;
using Pokemanager.Randomizer;

namespace Pokemanager.Tests.Randomizer;

public class UprLocatorTests
{
    [Theory]
    [InlineData("java version \"1.8.0_481\"", 8)]
    [InlineData("java version \"25.0.2\" 2026-01-20 LTS", 25)]
    [InlineData("openjdk version \"17.0.12\" 2024-07-16", 17)]
    [InlineData("basura", 0)]
    public void ParseJavaMajor(string output, int expected) => Assert.Equal(expected, UprLocator.ParseJavaMajor(output));

    [Fact]
    public void BundledJar_IsCopiedWithTheApp() => Assert.True(File.Exists(UprLocator.BundledJar), UprLocator.BundledJar);

    [Fact]
    public void CacheKey_ChangesWithSeedAndPreset()
    {
        string rom = Path.GetTempFileName(), jar = Path.GetTempFileName();
        try
        {
            string a = RandomizationCache.Key(rom, [1, 2, 3], 10, jar);
            Assert.Equal(a, RandomizationCache.Key(rom, [1, 2, 3], 10, jar));
            Assert.NotEqual(a, RandomizationCache.Key(rom, [1, 2, 3], 11, jar));
            Assert.NotEqual(a, RandomizationCache.Key(rom, [1, 2, 4], 10, jar));
        }
        finally
        {
            File.Delete(rom);
            File.Delete(jar);
        }
    }

    [Theory]
    [InlineData("Mi random", null)]
    [InlineData("", "Pon un nombre")]
    [InlineData("a/b", "caracteres no válidos")]
    public void ValidateOutputName(string name, string? error)
    {
        string? result = RomBuilder.ValidateName(name);
        if (error is null)
            Assert.Null(result);
        else
            Assert.Contains(error, result);
    }

    [Fact]
    public void OutputRom_GoesNextToBaseRom()
    {
        string baseRom = Path.Combine(Path.GetTempPath(), "juegos", "Pokemon X.3ds");
        Assert.Equal(Path.Combine(Path.GetTempPath(), "juegos", "Partida nico.cxi"), RomBuilder.OutputPath(baseRom, "Partida nico"));
    }

    [Fact]
    public void Catalog_CoversEveryVisibleOptionOfTheBundledUpr()
    {
        // Lista medida sobre UPR ZX 4.6.1; si se actualiza el jar y aparecen opciones nuevas, que no pasen desapercibidas.
        Assert.All(UprOptionCatalog.Options.Values, o => Assert.Contains(o.Group, UprOptionCatalog.Groups));
    }
}

/// <summary>
/// UPR ZX incluido, contra datos reales. Requiere Java 11+ y, para randomizar, <c>POKEMANAGER_DUMP</c> (con el .3ds dentro)
/// y <c>POKEMANAGER_UPR_PRESET</c>. Se omite si falta algo.
/// </summary>
public class UprRealTests
{
    private static UprTools RequireTools()
    {
        var tools = UprLocator.Find();
        Assert.SkipWhen(tools is null, "No hay Java 11+ o falta el jar incluido.");
        return tools!;
    }

    private static (UprTools Tools, byte[] Preset, string Rom, string DumpDir) Require()
    {
        var tools = RequireTools();
        string? preset = Environment.GetEnvironmentVariable("POKEMANAGER_UPR_PRESET");
        string? dump = Environment.GetEnvironmentVariable("POKEMANAGER_DUMP");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(preset) || string.IsNullOrWhiteSpace(dump), "Faltan POKEMANAGER_UPR_PRESET o POKEMANAGER_DUMP.");
        string? rom = new Project { DumpDirectory = dump! }.ResolveRomFile();
        Assert.SkipWhen(rom is null, "No hay .3ds en la carpeta del volcado.");
        return (tools, File.ReadAllBytes(preset!), rom!, dump!);
    }

    [Fact]
    public async Task Settings_RoundTripWithoutChanges_IsIdentical_AndChangesApply()
    {
        var tools = RequireTools();
        var runner = new UprRunner(tools);
        var ct = TestContext.Current.CancellationToken;

        byte[] defaults = await runner.WriteSettingsAsync(null, [], ct);
        var described = await runner.DescribeSettingsAsync(defaults, cancellationToken: ct);
        Assert.True(described.Options.Count > 100);

        // Reescribir todas las opciones con sus mismos valores no cambia el preset.
        var same = described.Options.Select(o => new KeyValuePair<string, string>(o.Name,
            o.Type == "enum" ? o.Value.GetString()! : o.Value.ValueKind == JsonValueKind.True ? "true" : o.Value.ValueKind == JsonValueKind.False ? "false" : o.Value.GetRawText()));
        Assert.Equal(defaults, await runner.WriteSettingsAsync(defaults, same, ct));

        byte[] changed = await runner.WriteSettingsAsync(defaults, [new("AbilitiesMod", "RANDOMIZE"), new("tweak:FASTEST_TEXT", "true")], ct);
        var after = await runner.DescribeSettingsAsync(changed, cancellationToken: ct);
        Assert.Equal("RANDOMIZE", after.Options.Single(o => o.Name == "AbilitiesMod").Value.GetString());
        Assert.True(after.Tweaks.Single(t => t.Name == "FASTEST_TEXT").Value);
    }

    [Fact]
    public async Task SameSeed_SameOutput_AndBecomesEditingBase()
    {
        var (tools, preset, rom, dumpDir) = Require();
        string root = Directory.CreateTempSubdirectory("pokemanager-upr-").FullName;
        var ct = TestContext.Current.CancellationToken;
        try
        {
            string presetFile = Path.Combine(root, "preset.rnqs");
            await File.WriteAllBytesAsync(presetFile, preset, ct);
            var runner = new UprRunner(tools);
            var first = await runner.RandomizeAsync(presetFile, rom, 12345, Path.Combine(root, "a"), cancellationToken: ct);
            var second = await runner.RandomizeAsync(presetFile, rom, 12345, Path.Combine(root, "b"), cancellationToken: ct);

            Assert.Equal("0004000000055D00", Path.GetFileName(first.TitleDirectory));
            Assert.True(File.Exists(Path.Combine(first.TitleDirectory, "code.bin")));
            Assert.Contains("Random Seed: 12345", File.ReadLines(first.LogPath).Take(5));

            string[] files = Directory.GetFiles(first.TitleDirectory, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(first.TitleDirectory, f)).Order().ToArray();
            Assert.NotEmpty(files);
            foreach (string f in files)
                Assert.True(File.ReadAllBytes(Path.Combine(first.TitleDirectory, f))
                    .AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(second.TitleDirectory, f))), $"{f} difiere con la misma semilla");

            // La base de edición es el random: alguna especie difiere del volcado original.
            var session = EditorSession.Open(new Project { DumpDirectory = dumpDir }, Path.Combine(first.TitleDirectory, "romfs"));
            var vanilla = GameData.Load(Path.Combine(dumpDir, "romfs"));
            Assert.Contains(Enumerable.Range(1, 721),
                i => !vanilla.Personal[i].Write().AsSpan().SequenceEqual(session.Original.Personal[i].Write()));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
