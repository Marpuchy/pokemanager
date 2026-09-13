using System.Text.Json.Nodes;
using Pokemanager.Model.Dump;
using Pokemanager.Model.Editing;
using Pokemanager.Model.Edits;
using Pokemanager.Model.Projects;

namespace Pokemanager.Tests.Editing;

public sealed class PokemonDataFileTests : IDisposable
{
    private readonly SyntheticRomFs romfs = new();
    private readonly string dir = Directory.CreateTempSubdirectory("pokemanager-data-").FullName;

    public void Dispose()
    {
        romfs.Dispose();
        Directory.Delete(dir, true);
    }

    private EditorSession NewSession() => EditorSession.Open(new Project { DumpDirectory = romfs.DumpDirectory });

    [Fact]
    public void Edits_RoundTripThroughFile_IntoAnotherProject()
    {
        var source = NewSession();
        source.SetInt(GameTables.Personal, 1, "hp", 150);
        source.SetInt(GameTables.Personal, 2, "type1", 9);
        source.SetInt(GameTables.Moves, 3, "power", 90);
        source.Set(GameTables.Learnsets, 1, GameTables.LevelUp, JsonNode.Parse("[[1,2],[7,3]]")!);
        string path = Path.Combine(dir, "mine." + PokemonDataFile.Extension);
        PokemonDataFile.FromEdits(source, GameTitle.X, "seed 1").Save(path);

        var target = NewSession();
        var loaded = PokemonDataFile.Load(path);
        var result = loaded.ApplyTo(target, GameTitle.X, replaceEdits: false);

        Assert.Equal(PokemonDataScope.Edits, loaded.Scope);
        Assert.Equal("seed 1", loaded.Description);
        Assert.Equal(4, result.Applied);
        Assert.Empty(result.Skipped);
        Assert.False(result.OtherGame);
        Assert.Equal(150, target.GetInt(GameTables.Personal, 1, "hp"));
        Assert.Equal(9, target.GetInt(GameTables.Personal, 2, "type1"));
        Assert.Equal(90, target.GetInt(GameTables.Moves, 3, "power"));
        Assert.Equal("[[1,2],[7,3]]", target.Get(GameTables.Learnsets, 1, GameTables.LevelUp).ToJsonString());
    }

    [Fact]
    public void AllData_OnlyDifferencesFromTheBaseBecomeEdits()
    {
        var source = NewSession();
        source.SetInt(GameTables.Personal, 4, "spe", 200);
        string path = Path.Combine(dir, "all." + PokemonDataFile.Extension);
        PokemonDataFile.FromCurrent(source, GameTitle.Y).Save(path);

        var target = NewSession();
        var result = PokemonDataFile.Load(path).ApplyTo(target, GameTitle.X, replaceEdits: false);

        Assert.Equal(1, result.Applied);
        Assert.True(result.SameAsBase > 100);
        Assert.True(result.OtherGame);
        Assert.Equal(1, target.Project.Edits.Count);
    }

    [Fact]
    public void Replace_UndoesPreviousEdits_MergeKeepsThem()
    {
        var file = new PokemonDataFile { Scope = PokemonDataScope.Edits };
        file.Add(GameTables.Personal, 1, "atk", 77);

        var merge = NewSession();
        merge.SetInt(GameTables.Personal, 2, "def", 99);
        file.ApplyTo(merge, GameTitle.X, replaceEdits: false);
        Assert.Equal(2, merge.Project.Edits.Count);

        var replace = NewSession();
        replace.SetInt(GameTables.Personal, 2, "def", 99);
        file.ApplyTo(replace, GameTitle.X, replaceEdits: true);
        Assert.Equal(1, replace.Project.Edits.Count);
        Assert.Equal(32, replace.GetInt(GameTables.Personal, 2, "def")); // back to the base (synthetic: 10·3 + 2)
    }

    [Fact]
    public void ValuesThatDoNotFit_AreSkippedNotFatal()
    {
        var file = new PokemonDataFile();
        file.Add(GameTables.Personal, 999, "hp", 10);
        file.Add(GameTables.Personal, 1, "nonsense", 10);
        file.Add("trainers", 1, "x", 1);
        file.Add(GameTables.Personal, 1, "hp", 33);

        var session = NewSession();
        var result = file.ApplyTo(session, GameTitle.X, replaceEdits: false);

        Assert.Equal(3, result.Skipped.Count);
        Assert.Equal(1, result.Applied);
    }

    [Fact]
    public void OtherJson_IsRejected()
    {
        string path = Path.Combine(dir, "project.json");
        File.WriteAllText(path, """{"format":2,"dumpDirectory":"x"}""");

        Assert.Throws<InvalidDataException>(() => PokemonDataFile.Load(path));
    }
}
