using pk3DS.Core.CTR;
using Pokemanager.Model.Build;
using Pokemanager.Model.Data;
using Pokemanager.Model.Dump;
using Pokemanager.Model.Editing;
using Pokemanager.Model.Projects;

namespace Pokemanager.Tests.Editing;

/// <summary>
/// UPR ZX rewrites a TM's description only in English, so the Spanish bag described the moves the TMs taught before
/// the randomization (2026-11-10, user). The build rewrites it in every language.
/// </summary>
public sealed class MachineDescriptionTests
{
    [Fact]
    public void TheItemNumbers_AreTheThreeBlocks()
    {
        Assert.Equal(328, GameMachines.Item(0));
        Assert.Equal(419, GameMachines.Item(91));
        Assert.Equal(618, GameMachines.Item(92));
        Assert.Equal(620, GameMachines.Item(94));
        Assert.Equal(690, GameMachines.Item(95));
        Assert.Equal(694, GameMachines.Item(99));
    }

    [Fact]
    public void RealUltraSun_EveryLanguageDescribesTheMoveTheTmTeaches()
    {
        string? dump = Environment.GetEnvironmentVariable("POKEMANAGER_USUM_DUMP");
        Assert.SkipWhen(string.IsNullOrEmpty(dump), "POKEMANAGER_USUM_DUMP is not set.");
        var project = new Project { DumpDirectory = dump!, Game = GameTitle.UltraSun };
        var session = EditorSession.Open(project, null);
        int[] machines = GameMachines.Read(File.ReadAllBytes(Path.Combine(project.ExeFsPath, "code.bin")), GameTitle.UltraSun)!;
        Assert.Equal(GameMachines.Count, machines.Length);

        var outputs = ModBuilder.BuildEdits(session);
        int moveFile = GameText.MoveDescriptionFile(GameTitle.UltraSun);
        int itemFile = GameText.FileIndex(GameTitle.UltraSun, pk3DS.Core.TextName.ItemFlavor);
        string path = GameText.Archive(GameTitle.UltraSun, GameLanguage.Spanish);
        var files = new GARC.MemGARC(outputs["romfs/" + path]).Files;
        string[] moves = GameText.Read(GameTitle.UltraSun, files[moveFile]);
        string[] items = GameText.Read(GameTitle.UltraSun, files[itemFile]);
        // The game's text keeps no trailing space, so compare without it.
        Assert.All(Enumerable.Range(0, GameMachines.Count),
            tm => Assert.Equal(moves[machines[tm]].TrimEnd(), items[GameMachines.Item(tm)].TrimEnd()));
    }
}
