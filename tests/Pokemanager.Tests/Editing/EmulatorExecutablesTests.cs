using Pokemanager.Bridge;

namespace Pokemanager.Tests.Editing;

public sealed class EmulatorExecutablesTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("pokemanager-emu-").FullName;

    public void Dispose() => Directory.Delete(dir, true);

    [Fact]
    public void ConfigHints_ReadGameDirsRomsPathAndRecentFiles()
    {
        string user = Path.Combine(dir, "Citra");
        Directory.CreateDirectory(Path.Combine(user, "config"));
        File.WriteAllLines(Path.Combine(user, "config", "qt-config.ini"),
        [
            @"Paths\romsPath=",
            @"Paths\gamedirs\1\path=INSTALLED",
            @"Paths\gamedirs\3\path=D:/citra/roms",
            @"Paths\recentFiles=""G:/dump/Pokemon X - random.cxi"", D:/citra/roms/other.cxi",
            @"UILayout\geometry=@ByteArray(abc)",
        ]);

        var hints = EmulatorExecutables.ConfigHints(user);

        Assert.Contains(Path.Combine("D:", "citra", "roms").Replace('/', Path.DirectorySeparatorChar), hints.Select(h => h.Replace('/', Path.DirectorySeparatorChar)));
        Assert.Contains(hints, h => h.EndsWith("Pokemon X - random.cxi"));
        Assert.DoesNotContain("INSTALLED", hints);
        Assert.Empty(EmulatorExecutables.ConfigHints(Path.Combine(dir, "missing")));
    }

    [Fact]
    public void Detect_FindsPortableEmulatorNextToTheRomsFolder()
    {
        // D:\citra\roms\game.cxi with the emulator unpacked in D:\citra\citra-windows-msvc-…\citra-qt.exe
        string root = Path.Combine(dir, "citra");
        string roms = Path.Combine(root, "roms");
        string program = Path.Combine(root, "citra-windows-msvc-20240303", OperatingSystem.IsWindows() ? "citra-qt.exe" : "citra-qt");
        Directory.CreateDirectory(roms);
        Directory.CreateDirectory(Path.GetDirectoryName(program)!);
        File.WriteAllBytes(program, [0]);
        File.WriteAllBytes(Path.Combine(roms, "game.cxi"), [0]);

        var found = EmulatorExecutables.Detect([Path.Combine(roms, "game.cxi")]);

        var citra = Assert.Single(found, e => e.Path == Path.GetFullPath(program));
        Assert.Equal("Citra", citra.Name);
        Assert.Equal("Citra", EmulatorExecutables.NameOf(program));
    }
}
