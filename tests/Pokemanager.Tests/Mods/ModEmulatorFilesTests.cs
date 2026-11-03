using Pokemanager.Model.Build;
using Pokemanager.Model.Mods;

namespace Pokemanager.Tests.Mods;

/// <summary>
/// A mod's files too big for the ROM go to the emulator's LayeredFS folder: copied once, left alone when they are
/// already there, and taken away by a build that no longer needs them — without touching anything else in the folder.
/// </summary>
public sealed class ModEmulatorFilesTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("pokemanager-modfiles-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(root, recursive: true); } catch (IOException) { }
    }

    private ModContent Source(string titlePath, string text)
    {
        string path = Path.Combine(root, "library", titlePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return new ModContent(titlePath, path, "");
    }

    [Fact]
    public void CopiedOnce_KeptAfterwards_RemovedWhenNoLongerWanted()
    {
        string mods = Path.Combine(root, "user", "load", "mods", "00040000001B5100");
        var models = Source("romfs/a/0/9/4", "models");
        // Something the user put there by hand: never touched.
        Directory.CreateDirectory(Path.Combine(mods, "romfs", "a", "0", "1"));
        File.WriteAllText(Path.Combine(mods, "romfs", "a", "0", "1", "7"), "theirs");

        var first = ModEmulatorFiles.Sync(mods, [models]);
        Assert.Equal(["romfs/a/0/9/4"], first.Copied);
        Assert.Equal("models", File.ReadAllText(Path.Combine(mods, "romfs", "a", "0", "9", "4")));

        var second = ModEmulatorFiles.Sync(mods, [models]);
        Assert.Empty(second.Copied);
        Assert.Equal(["romfs/a/0/9/4"], second.Kept);

        var off = ModEmulatorFiles.Sync(mods, []);
        Assert.Equal(["romfs/a/0/9/4"], off.Removed);
        Assert.False(File.Exists(Path.Combine(mods, "romfs", "a", "0", "9", "4")));
        Assert.False(File.Exists(Path.Combine(mods, ModEmulatorFiles.ManifestName)));
        Assert.Equal("theirs", File.ReadAllText(Path.Combine(mods, "romfs", "a", "0", "1", "7")));
    }

    [Fact]
    public void NothingWantedAndNothingThere_LeavesNoFolder()
    {
        string mods = Path.Combine(root, "user", "load", "mods", "00040000001B5100");
        var result = ModEmulatorFiles.Sync(mods, []);
        Assert.Empty(result.Removed);
        Assert.False(Directory.Exists(mods));
    }
}
