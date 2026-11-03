using System.IO.Compression;
using Pokemanager.App.Services;
using Pokemanager.Bridge;
using Pokemanager.Model.Dump;
using Pokemanager.Model.Mods;
using Pokemanager.Model.Projects;
using Pokemanager.Randomizer;
using Pokemanager.Tests.Editing;

namespace Pokemanager.Tests.Mods;

/// <summary>
/// The second class of mod: a <b>3GX plugin the emulator loads</b>, which is not a change to the ROM at all.
/// </summary>
/// <remarks>
/// <para>
/// Two things are being kept honest here. First, that a plugin mod stays <b>out of everything that belongs to the
/// ROM</b>: it is not a read layer, it is not packed into a base ROM, it asks for no build and it does not tell two
/// versions of the history apart — because the moment it did any of those, turning a follower on would invalidate a
/// randomization and a built ROM for nothing. Second, that installing it does what it says: one file copied into the
/// emulator's own folder, taken back out again, and <b>refused while the emulator is open</b>.
/// </para>
/// <para>
/// Synthetic data and no network: the folders are temporary, the plugin is four bytes, and the archive is made on the
/// spot. The one thing not tested here is the emulator actually loading it, which needs a 3DS title and a running
/// emulator.
/// </para>
/// </remarks>
public sealed class ModPluginTests : IDisposable
{
    private const string PluginId = "test-follower";
    private const string RomModId = "test-expansion";
    private const string PluginName = "Gen7FieldFollower.3gx";

    private static readonly byte[] Plugin = [0x33, 0x47, 0x58, 0x00];

    private readonly SyntheticRomFs romfs = new();
    private readonly string work = Directory.CreateTempSubdirectory("pokemanager-plugin-").FullName;

    public void Dispose()
    {
        romfs.Dispose();
        try { Directory.Delete(work, recursive: true); } catch (IOException) { }
    }

    private string LibraryRoot => Path.Combine(work, "mods");

    /// <summary>Stands for the emulator's user folder; the plugin folder is worked out from it as the real one is.</summary>
    private string EmulatorRoot => Path.Combine(work, "Azahar");

    private string PluginFolder => EmulatorUserFolders.PluginDirectory(EmulatorRoot, GameTitle.UltraSun.TitleIdHex());

    private ModLibrary Library => new(LibraryRoot);

    // ------------------------------------------------------------------ the two definitions

    /// <summary>A plugin mod shaped like the real one: one <c>.3gx</c>, and a zip per game that carries it.</summary>
    private static ModDefinition PluginMod() => new(
        PluginId, "Test follower", "nobody", "a plugin made up for a test", "3.1",
        "https://example.invalid/mods/1",
        [new ModLicense("CC BY-NC-ND 4.0", "a test")],
        [GameTitle.Sun, GameTitle.Moon, GameTitle.UltraSun, GameTitle.UltraMoon],
        [new ModFile(ModFileArea.Plugin, PluginName) { Required = true }])
    {
        Kind = ModKind.EmulatorPlugin,
        SetupNote = "switch the loader on",
        Conflicts = ["only one plugin loads"],
        Downloads =
        [
            ModDownload.Archive(
                ModSource.Direct("https://example.invalid/dl/1", "https://example.invalid/mods/1", "sun.zip"),
                GameTitle.UltraSun) with { Provides = ["plugin/" + PluginName] },
        ],
    };

    /// <summary>A mod of the old class, to show the two are not treated the same way.</summary>
    private static ModDefinition RomMod() => new(
        RomModId, "Test expansion", "nobody", "a mod made up for a test", "1.0", "https://example.invalid",
        [new ModLicense("CC0-1.0", "a test")],
        [GameTitle.UltraSun],
        [new ModFile(ModFileArea.RomFs, "a/0/1/7") { Required = true }]);

    private void Provide(string modId, string servedPath, byte[] data)
    {
        string path = Library.PathFor(modId, servedPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, data);
    }

    private Project NewProject(params string[] mods) => new()
    {
        DumpDirectory = romfs.DumpDirectory,
        Mods = new ModSettings { Enabled = [.. mods] },
    };

    // ================================================================== it is not part of the ROM

    /// <summary>
    /// <b>A plugin mod is not a layer.</b> Its folder is in the library like any other mod's, but the romfs stack is
    /// the single dump folder it was before mods existed, and the executable is the dump's — exactly what a project
    /// with no mods at all reads.
    /// </summary>
    [Fact]
    public void APluginModIsNotAReadLayer()
    {
        // A romfs folder and an executable put in the plugin mod's own library folder on purpose: even then they
        // must not become a layer, because what decides that is the kind of mod and not what is lying about.
        Provide(ModCatalog.FollowerId, "plugin/" + PluginName, Plugin);
        Provide(ModCatalog.FollowerId, "romfs/a/0/1/7", [1, 2, 3]);
        Provide(ModCatalog.FollowerId, "exefs/code.bin", [4, 5, 6]);
        var project = NewProject(ModCatalog.FollowerId);

        Assert.Equal(new[] { romfs.RomFs }, project.ReadLayers(null, Library).Roots.ToArray());
        Assert.Equal(Path.Combine(romfs.DumpDirectory, "exefs", "code.bin"), project.CodePath(Library));
        Assert.Empty(Library.RomFsRoots(project.Mods));
        Assert.Null(Library.CodePath(project.Mods));
    }

    /// <summary>
    /// A mod that does bring files to the ROM still does, with the plugin enabled beside it: the plugin is taken out
    /// of the stack and nothing else is. This is the test that would fail if the filter were a blanket one.
    /// </summary>
    [Fact]
    public void ARomModBesideAPluginIsStillALayer()
    {
        Provide(ModCatalog.FollowerId, "plugin/" + PluginName, Plugin);
        Provide(ModCatalog.UsumExpansionId, "romfs/a/0/1/7", [1, 2, 3]);
        Directory.CreateDirectory(Library.RomFsRoot(ModCatalog.FollowerId));
        var project = NewProject(ModCatalog.FollowerId, ModCatalog.UsumExpansionId);

        var roots = project.ReadLayers(null, Library).Roots;

        Assert.Equal(new[] { Library.RomFsRoot(ModCatalog.UsumExpansionId), romfs.RomFs }, roots.ToArray());
        Assert.Equal([ModCatalog.UsumExpansionId], project.Mods.EnabledRomMods.ToArray());
    }

    /// <summary>An id this version does not know still counts, so the build goes on refusing with its name.</summary>
    [Fact]
    public void AnUnknownIdIsStillTreatedAsARomMod()
    {
        var project = NewProject("no-such-mod");

        Assert.Equal(["no-such-mod"], project.Mods.EnabledRomMods.ToArray());
        Assert.False(project.Mods.HasNoRomMods);
    }

    /// <summary>
    /// <b>It puts nothing in the ROM</b>, so there is nothing for the randomization's cache key to be made of: the
    /// installation reports no contents however many files happen to be in its folder.
    /// </summary>
    [Fact]
    public void APluginModContributesNothingToThePackedRom()
    {
        var mod = PluginMod();
        Provide(PluginId, "plugin/" + PluginName, Plugin);
        // Something that would be packed if this were a ROM mod, to prove the kind and not the path decides.
        Provide(PluginId, "romfs/a/0/1/7", [7, 7, 7]);

        var installation = Library.Inspect(mod);

        Assert.Empty(installation.Contents);
        var plugin = installation.Plugin;
        Assert.NotNull(plugin);
        Assert.Equal("plugin/" + PluginName, plugin!.ServedPath);
        Assert.False(plugin.IntoRom);
        Assert.True(plugin.IsPlugin);
        Assert.True(installation.CanInstallPlugin);
    }

    /// <summary>
    /// <b>It is not in the history's fingerprint.</b> Two projects that differ only by the plugin built the same ROM,
    /// so a version of the history must not be told apart by it — otherwise installing a follower would make every
    /// stored version look stale.
    /// </summary>
    [Fact]
    public void APluginModDoesNotChangeTheFingerprintButARomModDoes()
    {
        string plain = ProjectHistory.Fingerprint(NewProject());
        string withPlugin = ProjectHistory.Fingerprint(NewProject(ModCatalog.FollowerId));
        string withRomMod = ProjectHistory.Fingerprint(NewProject(ModCatalog.UsumExpansionId));

        Assert.Equal(plain, withPlugin);
        Assert.NotEqual(plain, withRomMod);
    }

    /// <summary>
    /// <b>It asks for no modded base ROM.</b> A project whose only mod is a plugin gets its own ROM back untouched
    /// and no cache folder is created — the same answer a project with no mods at all gets, which is the point.
    /// </summary>
    [Fact]
    public async Task APluginModAsksForNoModdedBaseRom()
    {
        string jar = Path.Combine(work, "PokeRandoZX.jar");
        string rom = Path.Combine(work, "base.3ds");
        File.WriteAllBytes(jar, [0xDE, 0xAD]);
        File.WriteAllBytes(rom, [0xBE, 0xEF]);
        var upr = new UprService(Path.Combine(work, "cache"), new UprTools("java", 21, jar), Library);
        var project = NewProject(ModCatalog.FollowerId);
        project.RomFile = rom;

        var (built, installed) = await upr.BaseRomAsync(project, rom);

        Assert.Equal(rom, built);
        Assert.Empty(installed);
        Assert.False(Directory.Exists(upr.ModdedRoms.Root));
    }

    // ================================================================== installing it in the emulator

    /// <summary>
    /// Installing is a copy into <c>sdmc/luma/plugins/&lt;TitleID&gt;</c> under the emulator's user folder, and
    /// uninstalling takes that same file back out. Nothing else of the emulator is touched.
    /// </summary>
    [Fact]
    public void InstallingCopiesThePluginWhereTheEmulatorLooksAndUninstallingRemovesIt()
    {
        var mod = PluginMod();
        Provide(PluginId, "plugin/" + PluginName, Plugin);
        string expected = Path.Combine(EmulatorRoot, "sdmc", "luma", "plugins", GameTitle.UltraSun.TitleIdHex(), PluginName);

        Assert.False(ModPluginInstaller.Inspect(PluginFolder, mod).Installed);

        var installed = ModPluginInstaller.Install(PluginFolder, mod, Library);

        Assert.True(installed.Installed);
        Assert.Equal(Path.GetFullPath(expected), installed.Path);
        Assert.Equal(Plugin, File.ReadAllBytes(expected));
        Assert.Equal((long)Plugin.Length, installed.Bytes);
        Assert.Equal(ModLibrary.Hash(expected), installed.Sha256);
        // Nothing is left half-written beside it.
        Assert.Empty(Directory.GetFiles(PluginFolder, "*.partial"));

        var removed = ModPluginInstaller.Uninstall(PluginFolder, mod);

        Assert.False(removed.Installed);
        Assert.False(File.Exists(expected));
    }

    /// <summary>
    /// <b>It refuses to write while the emulator is open</b>, both ways round — the emulator reads its plugin folder
    /// when it loads a title, so a file written under a running one is at best ignored. The refusal names what is
    /// running, and <b>nothing on disk moves</b>.
    /// </summary>
    [Fact]
    public void ItRefusesToWriteWhileTheEmulatorIsOpen()
    {
        var mod = PluginMod();
        Provide(PluginId, "plugin/" + PluginName, Plugin);
        string[] running = ["azahar"];

        var refused = Assert.Throws<InvalidOperationException>(() => ModPluginInstaller.Install(PluginFolder, mod, Library, running));

        Assert.Contains("azahar", refused.Message);
        Assert.False(Directory.Exists(PluginFolder));

        // Installed with nothing running, it then refuses to be removed while something is.
        ModPluginInstaller.Install(PluginFolder, mod, Library);
        var refusedRemoval = Assert.Throws<InvalidOperationException>(() => ModPluginInstaller.Uninstall(PluginFolder, mod, running));

        Assert.Contains("azahar", refusedRemoval.Message);
        Assert.True(ModPluginInstaller.Inspect(PluginFolder, mod).Installed);
    }

    /// <summary>With nothing in the library there is nothing to install, and the message says where it should be.</summary>
    [Fact]
    public void WithoutThePluginInTheLibraryThereIsNothingToInstall()
    {
        var mod = PluginMod();

        var ex = Assert.Throws<InvalidOperationException>(() => ModPluginInstaller.Install(PluginFolder, mod, Library));

        Assert.Contains(PluginName, ex.Message);
        Assert.False(Library.Inspect(mod).CanInstallPlugin);
    }

    /// <summary>A mod that is built into the ROM has no plugin to install, and saying so is better than copying a file.</summary>
    [Fact]
    public void ARomModCannotBeInstalledAsAPlugin()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => ModPluginInstaller.Install(PluginFolder, RomMod(), Library));

        Assert.Contains("Test expansion", ex.Message);
    }

    /// <summary>
    /// <b>The emulator loads one plugin per game</b>, so another <c>.3gx</c> in the folder means this one will not
    /// load. It is reported by name, measured in the folder — and it is <b>left alone</b>: it is not ours to delete,
    /// and uninstalling removes only our own file.
    /// </summary>
    [Fact]
    public void AnotherPluginInTheFolderIsReportedAndNeverRemoved()
    {
        var mod = PluginMod();
        Provide(PluginId, "plugin/" + PluginName, Plugin);
        Directory.CreateDirectory(PluginFolder);
        string other = Path.Combine(PluginFolder, "SomebodyElse.3gx");
        File.WriteAllBytes(other, [1]);

        var installed = ModPluginInstaller.Install(PluginFolder, mod, Library);

        Assert.True(installed.Installed);
        Assert.True(installed.HasConflict);
        Assert.Equal(["SomebodyElse.3gx"], installed.OtherPlugins);

        var removed = ModPluginInstaller.Uninstall(PluginFolder, mod);

        Assert.False(removed.Installed);
        Assert.True(File.Exists(other));
        Assert.True(removed.HasConflict);
    }

    // ================================================================== the archive and the catalogue

    /// <summary>
    /// <b>The plugin comes out of an archive laid out for the emulator, not for the game.</b> The author's zips carry
    /// a <c>luma</c> folder, so there is no <c>romfs/</c> to look for: the <c>.3gx</c> is taken by its own name and
    /// kept at <c>plugin/&lt;name&gt;</c>, which is where the library and the installer look for it.
    /// </summary>
    [Fact]
    public async Task APluginArchiveIsUnpackedByTakingTheGxOutOfIt()
    {
        var mod = PluginMod();
        byte[] zip = Zip(
            ("README.txt", [0x68, 0x69]),
            ("luma/plugins/00040000001B5000/" + PluginName, Plugin));
        var downloader = new ModDownloader(new OneAnswer(zip), Library);

        var report = await downloader.DownloadAsync(mod, GameTitle.UltraSun);

        var outcome = Assert.Single(report.Outcomes);
        Assert.Equal(ModDownloadStatus.Done, outcome.Status);
        Assert.Equal(["plugin/" + PluginName], outcome.Written);
        Assert.Equal(Plugin, File.ReadAllBytes(Library.PathFor(PluginId, "plugin/" + PluginName)));
        // The readme stayed in the zip, as it does for a ROM mod's release.
        Assert.False(File.Exists(Library.PathFor(PluginId, "plugin/README.txt")));
        Assert.True(Library.Inspect(mod).CanInstallPlugin);
    }

    /// <summary>
    /// <b>Four games, four zips, and the project's own one is the one fetched.</b> Each archive is built for one
    /// title, so handing Sun's to an Ultra Moon project would be a plugin that does nothing.
    /// </summary>
    [Fact]
    public void TheCatalogueChoosesTheDownloadOfTheProjectsGame()
    {
        var mod = ModCatalog.Find(ModCatalog.FollowerId)!;

        Assert.Equal(4, mod.Downloads.Count);
        Assert.All(mod.Downloads, d => Assert.NotNull(d.Game));

        Assert.Equal("ultra_sun_c8a6f.zip", Only(GameTitle.UltraSun).Source.AssetName);
        Assert.Equal("ultra_moon_1e982.zip", Only(GameTitle.UltraMoon).Source.AssetName);
        Assert.Equal("sun_cef82.zip", Only(GameTitle.Sun).Source.AssetName);
        Assert.Equal("moon_6cd18.zip", Only(GameTitle.Moon).Source.AssetName);

        // Nothing of one game reaches another, and a game the mod is not for gets nothing at all.
        Assert.Empty(mod.DownloadsFor(GameTitle.X));
        Assert.False(mod.Supports(GameTitle.X));

        ModDownload Only(GameTitle game) => Assert.Single(mod.DownloadsFor(game));
    }

    /// <summary>
    /// The entry as it was verified against the author's own page: the canonical name, the author, version 3.1, the
    /// licence that page declares, the four title ids taken from <see cref="GameTitle"/> and the size of every zip.
    /// <b>No hash is known</b>, so none is invented: the model already says it cannot confirm the file.
    /// </summary>
    [Fact]
    public void TheFollowerEntryIsTheDataThatWasVerified()
    {
        var mod = ModCatalog.Find(ModCatalog.FollowerId)!;

        Assert.Equal("Pokemon Follower Mod (Includes SM)", mod.Name);
        Assert.Equal("Aqua_", mod.Author);
        Assert.Equal("3.1", mod.Version);
        Assert.Equal("https://gamebanana.com/mods/694400", mod.SourceUrl);
        Assert.Equal("CC BY-NC-ND 4.0", Assert.Single(mod.Licenses).Name);

        // The kind is what everything else follows from.
        Assert.Equal(ModKind.EmulatorPlugin, mod.Kind);
        Assert.True(mod.IsPlugin);
        Assert.False(mod.ChangesTheRom);

        // One file, required, in the emulator's folder and never in the ROM.
        var plugin = Assert.Single(mod.Files);
        Assert.Equal(ModFileArea.Plugin, plugin.Area);
        Assert.Equal("Gen7FieldFollower.3gx", plugin.Path);
        Assert.True(plugin.Required);
        Assert.False(plugin.IntoRom);
        Assert.Empty(mod.RomFiles);
        Assert.Equal(plugin, mod.PluginFile!);

        // The four games, by the title ids the application already keeps.
        Assert.Equal([GameTitle.Sun, GameTitle.Moon, GameTitle.UltraSun, GameTitle.UltraMoon], mod.Games.ToArray());
        Assert.Equal(["0004000000164800", "0004000000175E00", "00040000001B5000", "00040000001B5100"],
            mod.Games.Select(g => g.TitleIdHex()).ToArray());

        // Every zip is the same size, and not one hash was measured, so not one is claimed.
        Assert.All(mod.Downloads, d => Assert.Equal(310_732L, d.Bytes!.Value));
        Assert.All(mod.Downloads, d => Assert.False(d.HashKnown));
        Assert.False(mod.HasKnownHashes);
        Assert.All(mod.Files, f => Assert.Null(f.Sha256));
    }

    /// <summary>
    /// The links are plain HTTPS on the author's host and are <b>followed through their redirect</b> (GameBanana's
    /// <c>/dl/</c> answers 302 towards its file cache, and the shared fetcher is built to follow it); behind each one
    /// there is still the mod's own page for a human to open.
    /// </summary>
    [Fact]
    public void EveryDownloadIsFetchableAndHasThePageBehindIt()
    {
        var mod = ModCatalog.Find(ModCatalog.FollowerId)!;

        Assert.True(mod.CanDownload);
        Assert.All(mod.Downloads, d =>
        {
            Assert.Equal(ModSourceKind.DirectUrl, d.Source.Kind);
            Assert.Equal("gamebanana.com", d.Source.DownloadUri!.Host);
            Assert.Equal("gamebanana.com", d.Source.HostText);
            Assert.Equal("https://gamebanana.com/mods/694400", d.Source.PageUrl);
            Assert.Equal(["plugin/Gen7FieldFollower.3gx"], d.Provides);
        });
    }

    /// <summary>
    /// What the user has to know before installing travels with the entry: the five faults the author admits to, the
    /// three things it will not work alongside, and the loader that has to be switched on or nothing happens at all.
    /// </summary>
    [Fact]
    public void WhatTheUserNeedsToKnowBeforeInstallingTravelsWithTheEntry()
    {
        var mod = ModCatalog.Find(ModCatalog.FollowerId)!;

        Assert.Equal(5, mod.KnownIssues.Count);
        Assert.All(mod.KnownIssues, issue => Assert.NotEmpty(issue));
        Assert.Equal(3, mod.Conflicts.Count);
        Assert.All(mod.Conflicts, conflict => Assert.NotEmpty(conflict));
        Assert.NotNull(mod.SetupNote);
        Assert.NotEmpty(mod.SetupNote!);
    }

    /// <summary>It is offered for all four games, and for nothing else.</summary>
    [Fact]
    public void ItIsOfferedForTheFourGamesAndNoOther()
    {
        foreach (var game in new[] { GameTitle.Sun, GameTitle.Moon, GameTitle.UltraSun, GameTitle.UltraMoon })
            Assert.Contains(ModCatalog.Find(ModCatalog.FollowerId)!, ModCatalog.For(game));

        Assert.Empty(ModCatalog.For(GameTitle.X));
        Assert.Empty(ModCatalog.For(GameTitle.OmegaRuby));
    }

    // ------------------------------------------------------------------ helpers

    private static byte[] Zip(params (string Name, byte[] Data)[] entries)
    {
        var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, data) in entries)
            {
                var entry = zip.CreateEntry(name);
                using var stream = entry.Open();
                stream.Write(data);
            }
        }
        return buffer.ToArray();
    }

    /// <summary>A fetcher that hands over one body from memory: no socket is opened in this suite either.</summary>
    private sealed class OneAnswer(byte[] body) : IModFetcher
    {
        public Task<ModFetchResponse> GetAsync(Uri url, string? cookies, CancellationToken cancel) =>
            Task.FromResult(new ModFetchResponse(
                new MemoryStream(body, writable: false), body.Length, "application/zip", null, null));
    }
}
