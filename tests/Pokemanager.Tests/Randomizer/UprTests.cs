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
}

/// <summary>
/// UPR ZX real. Requiere <c>POKEMANAGER_UPR_JAR</c> (PokeRandoZX.jar), <c>POKEMANAGER_UPR_PRESET</c> (.rnqs),
/// <c>POKEMANAGER_DUMP</c> (con el .3ds dentro) y Java 11+. Se omite si falta algo.
/// </summary>
public class UprRealTests
{
    private static (UprTools Tools, string Preset, string Rom, string DumpDir) Require()
    {
        string? jar = Environment.GetEnvironmentVariable("POKEMANAGER_UPR_JAR");
        string? preset = Environment.GetEnvironmentVariable("POKEMANAGER_UPR_PRESET");
        string? dump = Environment.GetEnvironmentVariable("POKEMANAGER_DUMP");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(jar) || string.IsNullOrWhiteSpace(preset) || string.IsNullOrWhiteSpace(dump),
            "Faltan POKEMANAGER_UPR_JAR, POKEMANAGER_UPR_PRESET o POKEMANAGER_DUMP.");
        string? rom = new Project { DumpDirectory = dump! }.ResolveRomFile();
        Assert.SkipWhen(rom is null, "No hay .3ds en la carpeta del volcado.");
        var java = UprLocator.FindJava();
        Assert.SkipWhen(java is null, "No hay Java 11+.");
        return (new UprTools(java!.Value.Path, java.Value.Major, jar!), preset!, rom!, dump!);
    }

    [Fact]
    public async Task SameSeed_SameOutput_AndBecomesEditingBase()
    {
        var (tools, preset, rom, dumpDir) = Require();
        string root = Directory.CreateTempSubdirectory("pokemanager-upr-").FullName;
        try
        {
            var runner = new UprRunner(tools);
            var first = await runner.RunAsync(preset, rom, 12345, Path.Combine(root, "a"), cancellationToken: TestContext.Current.CancellationToken);
            var second = await runner.RunAsync(preset, rom, 12345, Path.Combine(root, "b"), cancellationToken: TestContext.Current.CancellationToken);

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
