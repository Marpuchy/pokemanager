using Pokemanager.Randomizer;

namespace Pokemanager.Tests.Mods;

/// <summary>
/// The second place a mod has to enter: <b>the build</b>. A base ROM with the mod packed into it is what UPR is handed,
/// and it is cached because packing a 3DS ROM costs tens of seconds and gigabytes while the modded base only changes
/// when the mod or the ROM does.
/// </summary>
/// <remarks>
/// <b>One method works out the key.</b> The randomization cache was once keyed differently on the way in and on the way
/// out and never found its own output again; these tests hold the line here — the key a lookup asks for is the key the
/// write used, and nothing about the order the files were listed in can change it. No ROM and no Java: the entry is
/// written by hand, exactly as <c>GetOrCreateAsync</c> leaves one.
/// </remarks>
public sealed class ModdedRomCacheTests : IDisposable
{
    private readonly string work = Directory.CreateTempSubdirectory("pokemanager-modded-rom-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(work, recursive: true); } catch (IOException) { }
    }

    private string CacheRoot => Path.Combine(work, "cache");
    private string RomPath => Path.Combine(work, "base.3ds");

    private ModdedRomCache Cache => new(CacheRoot);

    private static ModdedRomFile File1 => new("romfs/a/0/1/7", "/wherever/personal", "AA11");
    private static ModdedRomFile File2 => new("code.bin", "/wherever/code.bin", "BB22");

    public ModdedRomCacheTests() => File.WriteAllBytes(RomPath, [0xBE, 0xEF]);

    /// <summary>A finished entry on disk, exactly as <c>GetOrCreateAsync</c> leaves one.</summary>
    private void WriteEntry(params ModdedRomFile[] files)
    {
        string dir = Cache.DirectoryFor(RomPath, files);
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "base" + RomBuilder.Extension), [1, 2, 3]);
        File.WriteAllText(Path.Combine(dir, ".complete"), DateTime.UtcNow.ToString("O"));
    }

    /// <summary>
    /// What the write stored is what the lookup finds. This is the bug of the randomization cache, not repeated: both
    /// sides go through <see cref="ModdedRomCache.Key"/> and there is nowhere else for them to disagree.
    /// </summary>
    [Fact]
    public void TheKeyIsTheSameOnTheWayInAndOnTheWayOut()
    {
        WriteEntry(File1, File2);

        string? found = Cache.TryGet(RomPath, [File1, File2]);

        Assert.NotNull(found);
        Assert.True(File.Exists(found!));
        Assert.Equal(Cache.DirectoryFor(RomPath, [File1, File2]), Path.GetDirectoryName(found));
    }

    /// <summary>
    /// And the order the caller happened to list the files in cannot change it: the key sorts them. Without this, two
    /// builds of the same mod could miss each other's entry depending on how the library enumerated the folder.
    /// </summary>
    [Fact]
    public void TheOrderTheFilesAreListedInDoesNotChangeTheKey()
    {
        Assert.Equal(ModdedRomCache.Key(RomPath, [File1, File2]), ModdedRomCache.Key(RomPath, [File2, File1]));

        WriteEntry(File2, File1);
        Assert.NotNull(Cache.TryGet(RomPath, [File1, File2]));
    }

    /// <summary>
    /// The contents really are part of the key: a different file under the same served path is a different base ROM,
    /// so the old one is not handed back. That is what makes "the modded base changes when the mod changes" true.
    /// </summary>
    [Fact]
    public void ADifferentFileIsADifferentBaseRom()
    {
        WriteEntry(File1, File2);

        var changed = File1 with { Sha256 = "CC33" };

        Assert.NotEqual(ModdedRomCache.Key(RomPath, [File1, File2]), ModdedRomCache.Key(RomPath, [changed, File2]));
        Assert.Null(Cache.TryGet(RomPath, [changed, File2]));
        Assert.Null(Cache.TryGet(RomPath, [File1])); // and one file fewer is not the same ROM either
    }

    /// <summary>
    /// The path on disk is <b>not</b> part of the key — only the served path and the contents are. The same files
    /// moved to another folder of the library are the same modded base, and repacking three gigabytes would be waste.
    /// </summary>
    [Fact]
    public void WhereTheFileSitsOnDiskIsNotPartOfTheKey() =>
        Assert.Equal(ModdedRomCache.Key(RomPath, [File1]),
            ModdedRomCache.Key(RomPath, [File1 with { SourcePath = "/somewhere/else/personal" }]));

    /// <summary>A different base ROM is a different entry, as it is for the randomization cache.</summary>
    [Fact]
    public void ADifferentBaseRomIsADifferentEntry()
    {
        string other = Path.Combine(work, "other.3ds");
        File.WriteAllBytes(other, [0xFE, 0xED]);

        Assert.NotEqual(ModdedRomCache.Key(RomPath, [File1]), ModdedRomCache.Key(other, [File1]));
    }

    /// <summary>An entry that was never finished is not handed out: only the marker says it is complete.</summary>
    [Fact]
    public void AnUnfinishedEntryIsNotUsed()
    {
        string dir = Cache.DirectoryFor(RomPath, [File1]);
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "base" + RomBuilder.Extension), [1]);

        Assert.Null(Cache.TryGet(RomPath, [File1]));
    }

    /// <summary>Nothing cached at all: the lookup says so instead of throwing.</summary>
    [Fact]
    public void AnEmptyCacheFindsNothing() => Assert.Null(Cache.TryGet(RomPath, [File1]));

    /// <summary>
    /// With no files there is nothing to pack, and the caller is meant to keep using the plain ROM: this refuses
    /// rather than quietly packing a copy of the base ROM and handing it over as "modded".
    /// </summary>
    [Fact]
    public async Task PackingWithNoFilesIsRefused() =>
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Cache.GetOrCreateAsync(null!, RomPath, []));

    /// <summary>
    /// The prune keeps the newest entries and never the one just used, whatever its date: these are whole ROMs, so the
    /// caller keeps very few.
    /// </summary>
    [Fact]
    public void PruningKeepsTheNewestAndTheOneInUse()
    {
        WriteEntry(File1);
        string oldest = Cache.DirectoryFor(RomPath, [File1]);
        File.SetLastWriteTimeUtc(Path.Combine(oldest, ".complete"), DateTime.UtcNow.AddDays(-2));
        WriteEntry(File2);
        string newest = Cache.DirectoryFor(RomPath, [File2]);

        Cache.Prune(keep: 1);
        Assert.True(Directory.Exists(newest));
        Assert.False(Directory.Exists(oldest));

        WriteEntry(File1);
        File.SetLastWriteTimeUtc(Path.Combine(oldest, ".complete"), DateTime.UtcNow.AddDays(-2));
        Cache.Prune(keep: 1, keepRom: Path.Combine(oldest, "base" + RomBuilder.Extension));
        Assert.True(Directory.Exists(oldest));
        Assert.True(Directory.Exists(newest));
    }

    /// <summary>The cache follows the data folder, like the randomization cache and the save backups.</summary>
    [Fact]
    public void TheDefaultRootIsUnderTheDataFolder() =>
        Assert.Equal("modded-roms", Path.GetFileName(ModdedRomCache.DefaultRoot));
}
