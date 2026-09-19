using Pokemanager.Model.Projects;

namespace Pokemanager.Tests.Editing;

public sealed class ProjectTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("pokemanager-project-").FullName;

    public void Dispose() => Directory.Delete(dir, true);

    private string Touch(string name)
    {
        string path = Path.Combine(dir, name);
        File.WriteAllBytes(path, [0]);
        return path;
    }

    [Fact]
    public void FindBaseRom_IgnoresRandomizedRomsCreatedNextToIt()
    {
        // Real case: "… - random.cxi" sorts before "….3ds" and was picked as the base.
        string original = Touch("Pokemon X (Europe) (En,Ja,Fr,De,Es,It,Ko).3ds");
        Touch("Pokemon X (Europe) (En,Ja,Fr,De,Es,It,Ko) - random.cxi");
        Touch("Pokemon X (Europe) (En,Ja,Fr,De,Es,It,Ko) - random.cxi.log");

        Assert.Equal(original, Project.FindBaseRom(dir));
    }

    [Fact]
    public void FindBaseRom_Prefers3dsOverCxi_EvenWithoutLog()
    {
        Touch("a.cxi");
        string original = Touch("z.3ds");

        Assert.Equal(original, Project.FindBaseRom(dir));
    }

    [Fact]
    public void FindBaseRom_OnlyRandomizedRoms_ReturnsNull()
    {
        Touch("random.cxi");
        Touch("random.cxi.log");

        Assert.Null(Project.FindBaseRom(dir));
    }

    [Fact]
    public void ExplicitRomFile_Wins()
    {
        Touch("base.3ds");
        string chosen = Touch("other.cci");

        Assert.Equal(chosen, new Project { DumpDirectory = dir, RomFile = chosen }.ResolveRomFile());
    }

    /// <summary>
    /// The game options and the shops are part of the project file. They were only kept in memory: the project was saved
    /// without them, so every one of them (shiny, level options, level cap, free Rare Candies) was gone on reopening.
    /// </summary>
    [Fact]
    public void SaveLoad_KeepsTheGameOptionsAndTheShops()
    {
        var project = new Project { DumpDirectory = dir };
        project.Shops.FreeRareCandies = true;
        project.Tweaks.AlwaysShiny = true;
        project.Tweaks.WildLevelPercent = -16.67;
        project.Tweaks.StaticLevelPercent = -16.67;
        project.Tweaks.TrainerLevelPercent = 10;
        project.Tweaks.LevelCapByMilestones = true;
        project.Tweaks.LevelCap = 14;
        project.Tweaks.LevelCapOverrides[2] = 30;
        string path = Path.Combine(dir, "p.json");

        project.Save(path);
        var loaded = Project.Load(path);

        Assert.True(loaded.Shops.FreeRareCandies);
        Assert.True(loaded.Tweaks.AlwaysShiny);
        Assert.Equal(-16.67, loaded.Tweaks.WildLevelPercent);
        Assert.Equal(-16.67, loaded.Tweaks.StaticLevelPercent);
        Assert.Equal(10, loaded.Tweaks.TrainerLevelPercent);
        Assert.True(loaded.Tweaks.LevelCapByMilestones);
        Assert.Equal(14, loaded.Tweaks.LevelCap);
        Assert.Equal(30, loaded.Tweaks.LevelCapOverrides[2]);
    }
}
