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
        // Caso real: «… - random.cxi» ordena antes que «….3ds» y era elegida como base.
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
        string chosen = Touch("otra.cci");

        Assert.Equal(chosen, new Project { DumpDirectory = dir, RomFile = chosen }.ResolveRomFile());
    }
}
