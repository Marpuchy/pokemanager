using Pokemanager.App.Services;
using Pokemanager.Model.Mods;
using Pokemanager.Model.Projects;
using Pokemanager.Randomizer;
using Pokemanager.Tests.Editing;

namespace Pokemanager.Tests.Mods;

/// <summary>
/// The stage the build gained before the randomization: it hands the rest of the pipeline the base ROM with the
/// project's mods packed into it.
/// </summary>
/// <remarks>
/// What cannot be tested here is the packing itself (it needs Java, UPR ZX and a real ROM). What can, and what matters
/// most, is everything around it: <b>a project with no mods gets its own ROM back and nothing is created</b>, and a mod
/// whose files are missing or unknown stops the build with a message instead of quietly producing a ROM without it.
/// </remarks>
public sealed class ModBuildStageTests : IDisposable
{
    private readonly string work = Directory.CreateTempSubdirectory("pokemanager-mod-stage-").FullName;
    private readonly SyntheticRomFs romfs = new();

    public void Dispose()
    {
        romfs.Dispose();
        try { Directory.Delete(work, recursive: true); } catch (IOException) { }
    }

    private string CacheRoot => Path.Combine(work, "cache");
    private string LibraryRoot => Path.Combine(work, "mods");
    private string JarPath => Path.Combine(work, "PokeRandoZX.jar");
    private string RomPath => Path.Combine(work, "base.3ds");

    public ModBuildStageTests()
    {
        File.WriteAllBytes(JarPath, [0xDE, 0xAD]);
        File.WriteAllBytes(RomPath, [0xBE, 0xEF]);
    }

    private UprService NewService() =>
        new(CacheRoot, new UprTools("java", 21, JarPath), new ModLibrary(LibraryRoot));

    private Project NewProject(params string[] mods) => new()
    {
        DumpDirectory = romfs.DumpDirectory,
        RomFile = RomPath,
        Mods = new ModSettings { Enabled = [.. mods] },
    };

    /// <summary>
    /// <b>No mods: the ROM comes back as it went in</b>, no installation is reported and not one folder is created. A
    /// project that never heard of mods builds exactly what it built before.
    /// </summary>
    [Fact]
    public async Task AProjectWithoutModsGetsItsOwnRomBack()
    {
        var upr = NewService();

        var (rom, installed) = await upr.BaseRomAsync(NewProject(), RomPath);

        Assert.Equal(RomPath, rom);
        Assert.Empty(installed);
        Assert.False(Directory.Exists(upr.ModdedRoms.Root));
    }

    /// <summary>A mod id this version does not know stops the build and the message names it.</summary>
    [Fact]
    public async Task AModThisVersionDoesNotKnowStopsTheBuild()
    {
        var upr = NewService();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            upr.BaseRomAsync(NewProject("no-such-mod"), RomPath));

        Assert.Contains("no-such-mod", ex.Message);
        Assert.False(Directory.Exists(upr.ModdedRoms.Root));
    }

    /// <summary>
    /// A known mod whose files are not in the library stops the build, and says where to put them. Quietly building a
    /// ROM without the mod would be the worst outcome: the player would wonder why nothing changed.
    /// </summary>
    [Fact]
    public async Task AKnownModWithoutItsFilesStopsTheBuild()
    {
        var upr = NewService();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            upr.BaseRomAsync(NewProject(ModCatalog.UsumExpansionId), RomPath));

        Assert.Contains(Path.Combine(LibraryRoot, ModCatalog.UsumExpansionId), ex.Message);
        Assert.False(Directory.Exists(upr.ModdedRoms.Root));
    }

    /// <summary>
    /// The library the service hands out is the one the editing session reads from, so the files that are checked are
    /// the files that are read and packed.
    /// </summary>
    [Fact]
    public void TheServiceAndTheProjectLookInTheSameLibrary()
    {
        var upr = NewService();
        var project = NewProject(ModCatalog.UsumExpansionId);
        string romfsLayer = Path.Combine(LibraryRoot, ModCatalog.UsumExpansionId, "romfs");
        Directory.CreateDirectory(romfsLayer);

        Assert.Equal(new[] { romfsLayer, project.RomFsPath }, project.ReadLayers(null, upr.Mods).Roots.ToArray());
    }

    /// <summary>
    /// The Mega Stones of the cache key are read through the project's own stack, so a mod that replaces that table is
    /// seen — and the same method answers on the way in and on the way out, which is what the key depends on.
    /// </summary>
    [Fact]
    public void TheMegaStonesOfTheKeyComeFromTheProjectsOwnStack()
    {
        using var withStones = new SyntheticRomFs(new[] { 656, 657 });
        var project = new Project { DumpDirectory = withStones.DumpDirectory, RomFile = RomPath };
        var upr = NewService();

        Assert.Equal(withStones.MegaStones, upr.MegaStones(project));
        Assert.Equal(upr.MegaStones(project), upr.MegaStones(project)); // cached, same answer
    }
}
