using Pokemanager.Model.Build;
using Pokemanager.Model.Data;
using Pokemanager.Model.Editing;
using Pokemanager.Model.Dump;
using Pokemanager.Model.Edits;
using Pokemanager.Model.Projects;

namespace Pokemanager.Tests.Dump;

/// <summary>
/// The game options on the real Pokémon X dump (skipped without <c>POKEMANAGER_DUMP</c>): the shiny branch is where
/// this application expects it, and switching it on changes that one byte of the executable and nothing else.
/// </summary>
public class RealDumpGameTests
{
    private static string RequireDump()
    {
        string? dumpDir = Environment.GetEnvironmentVariable("POKEMANAGER_DUMP");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(dumpDir), "POKEMANAGER_DUMP is not set.");
        Assert.SkipUnless(Directory.Exists(Path.Combine(dumpDir!, "exefs")), $"{dumpDir} does not contain exefs.");
        return dumpDir!;
    }

    [Fact]
    public void TheGameHasTheShinyBranch()
    {
        byte[] code = File.ReadAllBytes(Path.Combine(RequireDump(), "exefs", "code.bin"));

        Assert.True(GameCode.HasShinyBranch(code));
        Assert.False(GameCode.IsEveryPokemonShiny(code));
    }

    [Fact]
    public void EveryPokemonShiny_ChangesOneByteOfTheExecutable()
    {
        string dumpDir = RequireDump();
        var project = new Project { DumpDirectory = dumpDir };
        project.Tweaks.AlwaysShiny = true;
        var session = EditorSession.Open(project);

        var built = ModBuilder.BuildEdits(session);

        byte[] original = File.ReadAllBytes(Path.Combine(dumpDir, "exefs", "code.bin"));
        byte[] code = built[ModBuilder.CodeFile];
        Assert.Equal(original.Length, code.Length);
        var moved = Enumerable.Range(0, code.Length).Where(i => code[i] != original[i]).ToList();
        Assert.Single(moved);
        Assert.Equal(0x0A, original[moved[0]]);
        Assert.Equal(0xEA, code[moved[0]]);
        Assert.True(GameCode.IsEveryPokemonShiny(code));
        // Nothing of the romfs was touched: this option is only in the executable.
        Assert.DoesNotContain(built.Keys, k => k.StartsWith("romfs/", StringComparison.Ordinal));
    }

    /// <summary>The level cap changes the experience table and nothing else, and only above the cap.</summary>
    [Fact]
    public void LevelCap_RaisesOnlyTheLevelsAboveIt()
    {
        string dumpDir = RequireDump();
        var project = new Project { DumpDirectory = dumpDir };
        project.Tweaks.LevelCap = 20;
        var session = EditorSession.Open(project);

        var built = ModBuilder.BuildEdits(session);

        Assert.Equal(["romfs/a/2/1/7"], built.Keys);
        var original = new pk3DS.Core.CTR.GARC.MemGARC(File.ReadAllBytes(Path.Combine(dumpDir, "romfs", "a", "2", "1", "7"))).Files;
        var capped = new pk3DS.Core.CTR.GARC.MemGARC(built["romfs/a/2/1/7"]).Files;
        Assert.Equal(8, capped.Length);
        for (int rate = 0; rate < capped.Length; rate++)
        {
            Assert.True(ExperienceTable.IsTable(original[rate]));
            for (int level = 0; level <= 20; level++)
                Assert.Equal(ExperienceTable.Read(original[rate], level), ExperienceTable.Read(capped[rate], level));
            for (int level = 21; level <= 100; level++)
                Assert.True(ExperienceTable.Read(capped[rate], level) > ExperienceTable.Read(capped[rate], level - 1));
            Assert.True(ExperienceTable.Read(capped[rate], 21) >= ExperienceTable.Unreachable);
        }
    }

    /// <summary>Trainer levels scaled at build time, except one edited by hand, which keeps its value.</summary>
    [Fact]
    public void TrainerLevels_AreScaled_ButAHandEditWins()
    {
        var project = new Project { DumpDirectory = RequireDump() };
        project.Tweaks.TrainerLevelPercent = 20;
        var session = EditorSession.Open(project);
        var korrina = Enumerable.Range(0, session.Current.Trainers.Length)
            .First(i => !session.Current.Trainers[i].Unreadable && session.Current.Trainers[i].Count == 3 && session.Current.Trainers[i].Ai == 0x07);
        int[] before = [.. session.Current.Trainers[korrina].Team.Select(m => m.Level)];
        session.SetInt(GameTables.Trainers, korrina, "p1.level", 40);

        var built = ModBuilder.BuildEdits(session);

        var layout = session.Current.Title.Layout();
        var data = new pk3DS.Core.CTR.GARC.MemGARC(built["romfs/" + layout.TrainerData]).Files;
        var team = new pk3DS.Core.CTR.GARC.MemGARC(built["romfs/" + layout.TrainerPokemon]).Files;
        int[] after = [.. Trainer.Read(session.Current.Title, data[korrina], team[korrina]).Team.Select(m => m.Level)];
        Assert.Equal(40, after[0]);
        for (int slot = 1; slot < before.Length; slot++)
            Assert.Equal(GameLevels.Scale(before[slot], 20), after[slot]);
        // The editor's own copy is not touched by a build.
        Assert.Equal(before[1], session.Current.Trainers[korrina].Team.ElementAt(1).Level);
    }

    [Fact]
    public void WithoutTheOption_TheExecutableIsNotBuilt()
    {
        var session = EditorSession.Open(new Project { DumpDirectory = RequireDump() });

        Assert.DoesNotContain(ModBuilder.CodeFile, ModBuilder.BuildEdits(session).Keys);
    }
}
