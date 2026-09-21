using Pokemanager.Model.Projects;

namespace Pokemanager.Tests.Editing;

/// <summary>The rules of the built ROM as a file that can be carried to another project.</summary>
public sealed class GameRulesFileTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("pokemanager-rules-").FullName;

    public void Dispose() => Directory.Delete(dir, true);

    [Fact]
    public void SaveLoadApply_CarriesTheRulesToAnotherProject()
    {
        var from = new Project { DumpDirectory = dir };
        from.Tweaks.AlwaysShiny = true;
        from.Tweaks.TrainerLevelPercent = 20;
        from.Tweaks.WildLevelPercent = -16.67;
        from.Tweaks.LevelCapByMilestones = true;
        from.Tweaks.LevelCapOverrides[1] = 22;
        from.Shops.FreeRareCandies = true;
        from.Shops.MegaStonesOnSale = true;
        string path = Path.Combine(dir, "rules." + GameRulesFile.Extension);

        GameRulesFile.FromProject(from, Model.Dump.GameTitle.UltraMoon, "a run").Save(path);
        var loaded = GameRulesFile.Load(path);
        var to = new Project { DumpDirectory = dir };
        loaded.ApplyTo(to);

        Assert.Equal(Model.Dump.GameTitle.UltraMoon, loaded.Game);
        Assert.True(to.Tweaks.AlwaysShiny);
        Assert.Equal(20, to.Tweaks.TrainerLevelPercent);
        Assert.Equal(-16.67, to.Tweaks.WildLevelPercent);
        Assert.True(to.Tweaks.LevelCapByMilestones);
        Assert.Equal(22, to.Tweaks.LevelCapOverrides[1]);
        Assert.True(to.Shops.FreeRareCandies);
        Assert.True(to.Shops.MegaStonesOnSale);
        // A copy, not the same objects: changing one project does not move the other.
        to.Tweaks.AlwaysShiny = false;
        Assert.True(from.Tweaks.AlwaysShiny);
    }

    [Fact]
    public void Load_RefusesAnythingElse()
    {
        string path = Path.Combine(dir, "other.gmdata");
        File.WriteAllText(path, "{\"magic\":\"something-else\"}");

        Assert.Throws<InvalidDataException>(() => GameRulesFile.Load(path));
    }
}
