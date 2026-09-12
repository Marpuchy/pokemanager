using System.Text.Json.Nodes;
using pk3DS.Core.Structures.PersonalInfo;
using Pokemanager.Bridge;
using Pokemanager.Model.Build;
using Pokemanager.Model.Data;
using Pokemanager.Model.Editing;
using Pokemanager.Model.Edits;
using Pokemanager.Model.Projects;

namespace Pokemanager.Tests.Editing;

public class ModBuilderTests
{
    private static (EditorSession Session, string ModDir) Setup(SyntheticRomFs romfs)
    {
        var session = EditorSession.Open(new Project { DumpDirectory = romfs.DumpDirectory });
        return (session, Path.Combine(Path.GetTempPath(), "pokemanager-mod-" + Guid.NewGuid().ToString("N")));
    }

    [Fact]
    public void NoEdits_WritesNoGarc()
    {
        using var romfs = new SyntheticRomFs();
        var (session, modDir) = Setup(romfs);
        try
        {
            var result = ModBuilder.Build(session, modDir);

            Assert.Empty(result.Written);
            Assert.False(Directory.Exists(Path.Combine(modDir, "romfs")));
        }
        finally { Directory.Delete(modDir, true); }
    }

    [Fact]
    public void PersonalEdit_UpdatesEntryAndConcatenatedTable_RestIdentical()
    {
        using var romfs = new SyntheticRomFs();
        var (session, modDir) = Setup(romfs);
        try
        {
            session.SetInt(GameTables.Personal, 1, "hp", 150);

            var result = ModBuilder.Build(session, modDir);

            Assert.Equal(["romfs/a/2/1/8"], result.Written);
            byte[][] original = romfs.ReadGarc(romfs.RomFs, GameData.PersonalGarc);
            byte[][] built = romfs.ReadGarc(Path.Combine(modDir, "romfs"), GameData.PersonalGarc);

            Assert.Equal(original.Length, built.Length);
            Assert.Equal(150, new PersonalInfoXY(built[1]).HP);
            Assert.Equal(150, built[^1][0x40 * 1]); // la copia concatenada también
            for (int i = 0; i < original.Length - 1; i++)
            {
                if (i != 1)
                    Assert.Equal(original[i], built[i]);
            }
            Assert.Equal(built[..^1].SelectMany(f => f).ToArray(), built[^1]);
        }
        finally { Directory.Delete(modDir, true); }
    }

    [Fact]
    public void MoveAndLearnsetEdits_AreWritten()
    {
        using var romfs = new SyntheticRomFs();
        var (session, modDir) = Setup(romfs);
        try
        {
            session.SetInt(GameTables.Moves, 2, "power", 250);
            session.Set(GameTables.Learnsets, 3, GameTables.LevelUp, JsonNode.Parse("[[1,3],[50,2]]")!);

            var result = ModBuilder.Build(session, modDir);

            Assert.Equal(2, result.Written.Count);
            byte[][] moves = romfs.ReadGarc(Path.Combine(modDir, "romfs"), GameData.MoveGarc);
            Assert.Equal(250, moves[2][0x03]);
            Assert.Equal(36, moves[2].Length);
            // El mod solo contiene los GARC editados: se lee el de learnsets directamente.
            var learnset = new pk3DS.Core.Structures.Learnset6(romfs.ReadGarc(Path.Combine(modDir, "romfs"), GameData.LevelUpGarc)[3]);
            Assert.Equal([3, 2], learnset.Moves);
            Assert.Equal([1, 50], learnset.Levels);
            Assert.False(File.Exists(Path.Combine(modDir, "romfs", "a", "2", "1", "8")));
        }
        finally { Directory.Delete(modDir, true); }
    }

    [Fact]
    public void Rebuild_RemovesStaleOutput_KeepsForeignFiles()
    {
        using var romfs = new SyntheticRomFs();
        var (session, modDir) = Setup(romfs);
        try
        {
            session.SetInt(GameTables.Moves, 0, "pp", 5);
            ModBuilder.Build(session, modDir);
            string foreign = Path.Combine(modDir, "romfs", "otro-mod.bin");
            File.WriteAllText(foreign, "no es nuestro");

            session.SetInt(GameTables.Moves, 0, "pp", 35); // vuelve al original: ya no hay ediciones
            var result = ModBuilder.Build(session, modDir);

            Assert.Equal(["romfs/a/2/1/2"], result.Removed);
            Assert.False(File.Exists(Path.Combine(modDir, "romfs", "a", "2", "1", "2")));
            Assert.True(File.Exists(foreign));
        }
        finally { Directory.Delete(modDir, true); }
    }

    [Fact]
    public void ModDirectoryInsideDump_IsRefused()
    {
        using var romfs = new SyntheticRomFs();
        var (session, _) = Setup(romfs);
        session.SetInt(GameTables.Personal, 0, "hp", 1);

        Assert.Throws<InvalidOperationException>(() => ModBuilder.Build(session, Path.Combine(romfs.RomFs, "mods")));
    }

    [Fact]
    public void ModDirectory_FollowsEmulatorLayout()
    {
        string dir = EmulatorUserFolders.ModDirectory(Path.Combine("C:", "Azahar"), "0004000000055D00");

        Assert.Equal(Path.Combine("C:", "Azahar", "load", "mods", "0004000000055D00"), dir);
    }
}
