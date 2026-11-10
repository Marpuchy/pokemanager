using Pokemanager.App.Services;
using Pokemanager.Model.Data;
using Pokemanager.Model.Editing;
using Pokemanager.Model.Projects;
using Pokemanager.Randomizer;
using Pokemanager.Tests.Editing;

namespace Pokemanager.Tests.App;

/// <summary>
/// The randomization cache is keyed on the item options too (<c>allItems</c> and the "good items" list), and the
/// application writes it on one path and reads it on another: both have to work out the same key. They did not, so a
/// project with "all items" or the Mega Stones in the pool never found its own randomization again.
/// </summary>
public sealed class UprCacheTests : IDisposable
{
    private const long Seed = 20260922;
    private static readonly byte[] Preset = [1, 2, 3, 4, 5];

    private readonly string work = Directory.CreateTempSubdirectory("pokemanager-upr-cache-").FullName;
    private readonly SyntheticRomFs romfs = new(new[] { 656, 657, 685 });

    private string CacheRoot => Path.Combine(work, "cache");
    private string JarPath => Path.Combine(work, "PokeRandoZX.jar");
    private string RomPath => Path.Combine(work, "base.3ds");

    public UprCacheTests()
    {
        File.WriteAllBytes(JarPath, [0xDE, 0xAD]);
        File.WriteAllBytes(RomPath, [0xBE, 0xEF]);
    }

    public void Dispose()
    {
        romfs.Dispose();
        try { Directory.Delete(work, recursive: true); } catch (IOException) { }
    }

    private Project NewProject(bool allItems, bool megaStonesInPool) => new()
    {
        DumpDirectory = romfs.DumpDirectory,
        RomFile = RomPath,
        Randomization = new RandomizationSettings
        {
            Enabled = true,
            Seed = Seed,
            Preset = Preset,
            AllowAllItems = allItems,
            MegaStonesInPool = megaStonesInPool,
        },
    };

    private UprService NewService() => new(CacheRoot, new UprTools("java", 21, JarPath));

    /// <summary>A finished randomization on disk, exactly as <c>RandomizationCache.GetOrCreateAsync</c> leaves one.</summary>
    private void WriteCacheEntry(bool allItems, IReadOnlyList<int>? goodItems)
    {
        string dir = Path.Combine(CacheRoot, RandomizationCache.Key(RomPath, Preset, Seed, JarPath, allItems, goodItems));
        Directory.CreateDirectory(Path.Combine(dir, "output", new string('0', 16))); // the title directory
        File.WriteAllText(Path.Combine(dir, "output", "upr.log"), "log");
        File.WriteAllText(Path.Combine(dir, ".complete"), DateTime.UtcNow.ToString("O"));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ARandomizationIsFoundAgainWithTheOptionsItWasBuiltWith(bool allItems, bool megaStonesInPool)
    {
        var project = NewProject(allItems, megaStonesInPool);
        // The key the build works out, from the data the editing session has: what BuildRomAsync used to pass by hand.
        var session = EditorSession.Open(project);
        WriteCacheEntry(allItems, megaStonesInPool ? session.Current.MegaStones : null);

        var cached = NewService().TryGetCached(project, Preset);

        Assert.NotNull(cached);
        Assert.Equal(Seed, cached!.Seed);
        Assert.Equal(new string('0', 16), Path.GetFileName(cached.TitleDirectory));
    }

    /// <summary>
    /// The options really are part of the key: a randomization built without them is not the one a project with them
    /// asks for, so the test above cannot pass by the lookup ignoring them.
    /// </summary>
    [Fact]
    public void ARandomizationBuiltWithoutTheItemOptionsIsNotTheOneAProjectWithThemAsksFor()
    {
        WriteCacheEntry(allItems: false, goodItems: null);

        Assert.Null(NewService().TryGetCached(NewProject(allItems: true, megaStonesInPool: false), Preset));
        Assert.Null(NewService().TryGetCached(NewProject(allItems: false, megaStonesInPool: true), Preset));
        Assert.NotNull(NewService().TryGetCached(NewProject(allItems: false, megaStonesInPool: false), Preset));
    }

    /// <summary>
    /// The Mega Stones the key uses come from the project's dump, and they are the game's own list: the same one the
    /// editing session reads, which is where the build used to take it from.
    /// </summary>
    [Fact]
    public void TheMegaStonesOfTheKeyAreTheGamesOwnList()
    {
        var project = NewProject(allItems: false, megaStonesInPool: true);
        var upr = NewService();

        Assert.Equal(new[] { 656, 657, 685 }, romfs.MegaStones);
        Assert.Equal(romfs.MegaStones, upr.MegaStones(project));
        Assert.Equal(EditorSession.Open(project).Current.MegaStones, upr.MegaStones(project));
        Assert.Equal(romfs.MegaStones, GameData.ReadMegaStones(new RomFsLayers(project.RomFsPath), project.Game));

        var options = upr.CacheOptions(project);
        Assert.False(options.AllItems);
        Assert.Equal(romfs.MegaStones, options.GoodItems);
    }

    /// <summary>With the option off the stones stay out of the key, whatever the game has.</summary>
    [Fact]
    public void WithoutTheOptionTheStonesStayOutOfTheKey()
    {
        var options = NewService().CacheOptions(NewProject(allItems: true, megaStonesInPool: false));

        Assert.True(options.AllItems);
        Assert.Null(options.GoodItems);
    }

    /// <summary>A randomization under a key that is no longer the current one (the patched randomizer changed).</summary>
    private string WriteEarlierEntry(long seed, byte[] preset)
    {
        string dir = Path.Combine(CacheRoot, "OLDKEY0000000000000000AA");
        Directory.CreateDirectory(Path.Combine(dir, "output", new string('0', 16)));
        File.WriteAllText(Path.Combine(dir, "output", "upr.log"), $"Randomizer Version: 4.6.1\nRandom Seed: {seed}\nSettings String: x\n");
        File.WriteAllBytes(Path.Combine(dir, "preset.rnqs"), preset);
        File.WriteAllText(Path.Combine(dir, ".complete"), DateTime.UtcNow.ToString("O"));
        return dir;
    }

    [Fact]
    public void TheInstalledRandomizationIsStillReadAfterTheRandomizerChanged()
    {
        var project = NewProject(false, false);
        project.Randomization.InstalledSeed = Seed;
        WriteEarlierEntry(Seed, Preset);

        var cached = NewService().TryGetCached(project, Preset);

        Assert.NotNull(cached);
        Assert.True(cached!.Earlier);
        Assert.Equal("OLDKEY0000000000000000AA", cached.CacheKey);
    }

    [Fact]
    public void AnEarlierRandomizationIsNotUsedForAnotherSeedPresetOrWhenNothingIsInstalled()
    {
        var project = NewProject(false, false);
        WriteEarlierEntry(Seed, Preset);
        Assert.Null(NewService().TryGetCached(project, Preset)); // never built: nothing is installed

        project.Randomization.InstalledSeed = Seed;
        Assert.Null(NewService().TryGetCached(project, [9, 9])); // another preset

        Directory.Delete(CacheRoot, recursive: true);
        WriteEarlierEntry(Seed + 1, Preset);
        Assert.Null(NewService().TryGetCached(project, Preset)); // another seed
    }
}
