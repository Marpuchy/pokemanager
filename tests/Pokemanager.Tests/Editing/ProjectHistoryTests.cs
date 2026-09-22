using System.Text.Json.Nodes;
using Pokemanager.Model.Edits;
using Pokemanager.Model.Projects;

namespace Pokemanager.Tests.Editing;

public sealed class ProjectHistoryTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("pokemanager-history-").FullName;

    public void Dispose() => Directory.Delete(dir, true);

    private string ProjectPath => Path.Combine(dir, "game.json");

    private static Project NewProject(long seed, int hp = 0)
    {
        var project = new Project { DumpDirectory = @"C:\dump" };
        project.Randomization.Enabled = true;
        project.Randomization.Seed = seed;
        project.Randomization.Preset = [1, 2, 3];
        project.Randomization.PresetName = "alex";
        if (hp > 0)
            project.Edits.Set(GameTables.Personal, 1, "hp", JsonValue.Create(hp));
        return project;
    }

    [Fact]
    public void Record_StoresConfigAndSave_NextToTheProject()
    {
        string save = Path.Combine(dir, "main");
        File.WriteAllBytes(save, [9, 9, 9]);
        var history = new ProjectHistory(ProjectPath);

        var version = history.Record(NewProject(111, hp: 50), VersionKind.Built, romPath: @"C:\rom.cxi", savePath: save);

        Assert.Equal(Path.Combine(dir, "game.history"), history.Root);
        var listed = Assert.Single(history.List());
        Assert.Equal(version.Id, listed.Id);
        Assert.Equal(111, listed.Seed);
        Assert.Equal(1, listed.EditCount);
        Assert.Equal(@"C:\rom.cxi", listed.RomPath);
        Assert.True(listed.HasSave);
        Assert.Equal([9, 9, 9], File.ReadAllBytes(history.SaveFile(listed)!));
        Assert.Equal(111, history.LoadProject(listed).Randomization.Seed);
    }

    [Fact]
    public void RestoringAVersion_BringsBackItsGameOptions_ButNotTheCapOfTheBuiltRom()
    {
        var version = NewProject(1);
        version.Tweaks.LevelCap = 18;
        version.Tweaks.AlwaysShiny = true;
        version.Tweaks.TrainerLevelPercent = 20;
        version.Shops.FreeRareCandies = true;
        version.Tweaks.InstalledLevelCap = 18;

        var target = NewProject(2);
        target.Tweaks.LevelCap = 34;
        target.Tweaks.InstalledLevelCap = 30; // the ROM on disk, which a restore does not change by itself

        ProjectHistory.RestoreInto(target, version);

        Assert.Equal(18, target.Tweaks.LevelCap);
        Assert.True(target.Tweaks.AlwaysShiny);
        Assert.Equal(20, target.Tweaks.TrainerLevelPercent);
        Assert.True(target.Shops.FreeRareCandies);
        Assert.Equal(30, target.Tweaks.InstalledLevelCap);
    }

    [Fact]
    public void TheGameOptions_AreAPartOfWhatIdentifiesABuild()
    {
        var a = NewProject(1);
        var b = NewProject(1);
        Assert.Equal(ProjectHistory.Fingerprint(a), ProjectHistory.Fingerprint(b));

        b.Tweaks.LevelCap = 18;
        Assert.NotEqual(ProjectHistory.Fingerprint(a), ProjectHistory.Fingerprint(b));

        a.Tweaks.LevelCap = 18;
        Assert.Equal(ProjectHistory.Fingerprint(a), ProjectHistory.Fingerprint(b));

        b.Shops.MegaStonesOnSale = true;
        Assert.NotEqual(ProjectHistory.Fingerprint(a), ProjectHistory.Fingerprint(b));

        // What was built is not an input: two projects that ask for the same ROM are the same version.
        a.Shops.MegaStonesOnSale = true;
        b.Tweaks.InstalledLevelCap = 12;
        Assert.Equal(ProjectHistory.Fingerprint(a), ProjectHistory.Fingerprint(b));
    }

    [Fact]
    public void SameConfigTwice_ReplacesInsteadOfStacking_DifferentConfigStacks()
    {
        var history = new ProjectHistory(ProjectPath);

        history.Record(NewProject(1), VersionKind.Built);
        history.Record(NewProject(1), VersionKind.Built);
        Assert.Single(history.List());

        history.Record(NewProject(2), VersionKind.Built);
        var list = history.List();
        Assert.Equal(2, list.Count);
        Assert.Equal(2, list[0].Seed); // newest first
    }

    [Fact]
    public void RestoreInto_BringsBackSeedPresetAndEdits_KeepsPaths()
    {
        var current = NewProject(999, hp: 80);
        current.Randomization.LastBuiltRom = @"C:\current.cxi";
        current.Randomization.OutputName = "current";
        current.Edits.Set(GameTables.Moves, 3, "power", JsonValue.Create(10));
        var old = NewProject(123, hp: 40);
        old.Randomization.Preset = [7, 7];
        old.Randomization.PresetName = "other";

        var diff = ProjectHistory.Compare(current, old);
        Assert.True(diff.Seed && diff.Preset && diff.Any);
        Assert.Equal((0, 1, 1), (diff.EditsAdded, diff.EditsRemoved, diff.EditsChanged));

        ProjectHistory.RestoreInto(current, old);

        Assert.Equal(123, current.Randomization.Seed);
        Assert.Equal([7, 7], current.Randomization.Preset);
        Assert.Equal("other", current.Randomization.PresetName);
        Assert.Equal(1, current.Edits.Count);
        Assert.Equal(40, current.Edits.All.Single().Value.GetValue<int>());
        Assert.Equal(@"C:\current.cxi", current.Randomization.LastBuiltRom);
        Assert.Equal("current", current.Randomization.OutputName);
        Assert.False(ProjectHistory.Compare(current, old).Any);
    }

    [Fact]
    public void Prune_KeepsNewestAutomatic_AndEveryManual()
    {
        var history = new ProjectHistory(ProjectPath);
        history.Record(NewProject(1), VersionKind.Manual, note: "before gym 3");
        for (int seed = 2; seed <= 6; seed++)
            history.Record(NewProject(seed), VersionKind.Built);

        history.Prune(keep: 2);

        var list = history.List();
        Assert.Equal([6L, 5L, 1L], list.Select(v => v.Seed));
        Assert.Equal("before gym 3", list[^1].Note);
    }
}
