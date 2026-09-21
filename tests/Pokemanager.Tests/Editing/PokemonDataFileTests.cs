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

    /// <summary>
    /// A file from a bigger game (Ultra Sun into Pokémon X): entries this game does not have and values it cannot look
    /// up are skipped and reported, and what does fit is applied. It used to take the application down.
    /// </summary>
    [Fact]
    public void DataFromABiggerGame_SkipsWhatThisGameDoesNotHave()
    {
        var file = new PokemonDataFile { Scope = PokemonDataScope.Edits, Kind = PokemonDataKind.All, Game = GameTitle.UltraSun };
        file.Add(GameTables.Personal, 1, "hp", JsonValue.Create(150));                      // fits
        file.Add(GameTables.Personal, 800, "hp", JsonValue.Create(80));                     // no such species here
        file.Add(GameTables.Learnsets, 900, GameTables.LevelUp, JsonNode.Parse("[[1,2]]")!); // no such learnset here
        file.Add(GameTables.Personal, 2, "ability1", JsonValue.Create(220));                // no such ability here
        file.Add(GameTables.Personal, 2, "type1", JsonValue.Create(40));                    // no such type
        file.Add(GameTables.Learnsets, 1, GameTables.LevelUp, JsonNode.Parse("[[1,2],[5,700]]")!); // move 700 does not exist
        file.Add(GameTables.Moves, 3, "power", JsonValue.Create(90));                       // fits
        file.Add(GameTables.Moves, 700, "power", JsonValue.Create(90));                     // no such move here

        var target = NewSession();
        var result = file.ApplyTo(target, GameTitle.X, replaceEdits: false);

        Assert.Equal(2, result.Applied);
        Assert.Equal(6, result.Skipped.Count);
        Assert.True(result.OtherGame);
        Assert.Equal(150, target.GetInt(GameTables.Personal, 1, "hp"));
        Assert.Equal(90, target.GetInt(GameTables.Moves, 3, "power"));
        // Nothing of what was skipped was written: the ability and the learnset are the ROM's.
        Assert.Equal(target.GetOriginal(GameTables.Personal, 2, "ability1").GetValue<int>(), target.GetInt(GameTables.Personal, 2, "ability1"));
        Assert.Equal(target.GetOriginal(GameTables.Learnsets, 1, GameTables.LevelUp).ToJsonString(),
            target.Get(GameTables.Learnsets, 1, GameTables.LevelUp).ToJsonString());
    }

    /// <summary>
    /// The row number of the personal table is a different Pokémon in each game (760 is Mega Venusaur in Pokémon X and
    /// Bewear in Ultra Moon), so a file says what Pokémon each of its rows held and the import puts it on that one.
    /// </summary>
    [Fact]
    public void RowsAreTranslatedByPokemon_NotByNumber()
    {
        var file = new PokemonDataFile { Scope = PokemonDataScope.Edits, Kind = PokemonDataKind.Pokemon, Game = GameTitle.UltraSun };
        file.Rows[4] = [2, 0];    // what that game kept in row 4 is species 2, which this game keeps in row 2
        file.Rows[3] = [400, 0];  // a species this game does not have
        file.Add(GameTables.Personal, 4, "hp", JsonValue.Create(151));
        file.Add(GameTables.Personal, 3, "hp", JsonValue.Create(99));

        var target = NewSession();
        int before = target.GetInt(GameTables.Personal, 4, "hp");
        var result = file.ApplyTo(target, GameTitle.X, replaceEdits: false);

        Assert.Equal(1, result.Applied);
        Assert.Equal(151, target.GetInt(GameTables.Personal, 2, "hp"));   // landed on species 2
        Assert.Equal(before, target.GetInt(GameTables.Personal, 4, "hp")); // row 4 untouched
        Assert.Single(result.Skipped);
        Assert.Contains("400", result.Skipped[0]);
    }

    /// <summary>A file with no row map, from another game: only the rows that are a species in both games are used.</summary>
    [Fact]
    public void WithoutARowMap_OnlyTheSpeciesRowsOfAnotherGameAreUsed()
    {
        var file = new PokemonDataFile { Scope = PokemonDataScope.Edits, Kind = PokemonDataKind.Pokemon, Game = GameTitle.UltraSun };
        file.Add(GameTables.Personal, 2, "hp", JsonValue.Create(151));
        file.Add(GameTables.Learnsets, 2, GameTables.LevelUp, JsonNode.Parse("[[1,2]]")!);

        var target = NewSession();
        var result = file.ApplyTo(target, GameTitle.X, replaceEdits: false);

        Assert.Equal(2, result.Applied);
        Assert.Equal(151, target.GetInt(GameTables.Personal, 2, "hp"));
    }

    /// <summary>A trainer number is a different trainer in another game: those are not imported at all.</summary>
    [Fact]
    public void TrainersOfAnotherGameAreNotImported()
    {
        var file = new PokemonDataFile { Scope = PokemonDataScope.Edits, Kind = PokemonDataKind.Trainers, Game = GameTitle.UltraSun };
        file.Add(GameTables.Trainers, 1, "ai", JsonValue.Create(7));

        var result = file.ApplyTo(NewSession(), GameTitle.X, replaceEdits: false);

        Assert.Equal(0, result.Applied);
        Assert.Single(result.Skipped);
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
    public void PokemonAndMoves_AreExportedApart()
    {
        var source = NewSession();
        source.SetInt(GameTables.Personal, 1, "hp", 150);
        source.Set(GameTables.Learnsets, 1, GameTables.LevelUp, JsonNode.Parse("[[1,2]]")!);
        source.SetInt(GameTables.Moves, 3, "power", 90);
        source.SetInt(GameTables.Moves, 2, "type", 9);

        var moves = PokemonDataFile.FromEdits(source, GameTitle.X, kind: PokemonDataKind.Moves);
        var pokemon = PokemonDataFile.FromEdits(source, GameTitle.X, kind: PokemonDataKind.Pokemon);
        Assert.Equal([GameTables.Moves], moves.Tables.Keys);
        Assert.Equal(2, moves.ValueCount);
        Assert.Equal([GameTables.Learnsets, GameTables.Personal], pokemon.Tables.Keys);

        // Every value of every move (data and description), and no Pokémon.
        var allMoves = PokemonDataFile.FromCurrent(source, GameTitle.X, kind: PokemonDataKind.Moves);
        Assert.Equal([GameTables.Moves, GameTables.MoveTexts], allMoves.Tables.Keys);
        Assert.Equal(GameTables.MoveTable.Count(source.Current) * (GameTables.MoveTable.Fields.Count + 1), allMoves.ValueCount);

        Assert.Equal("mvdata", PokemonDataFile.ExtensionOf(PokemonDataKind.Moves));
        Assert.Equal("pkdata", PokemonDataFile.ExtensionOf(PokemonDataKind.Pokemon));
        string path = Path.Combine(dir, "moves." + PokemonDataFile.MovesExtension);
        moves.Save(path);
        var loaded = PokemonDataFile.Load(path);
        Assert.Equal(PokemonDataKind.Moves, loaded.Kind);

        // Replacing with a moves file undoes the move changes only: the Pokémon changes stay.
        var target = NewSession();
        target.SetInt(GameTables.Personal, 2, "def", 99);
        target.SetInt(GameTables.Moves, 1, "pp", 5);
        loaded.ApplyTo(target, GameTitle.X, replaceEdits: true);
        Assert.Equal(99, target.GetInt(GameTables.Personal, 2, "def"));
        Assert.False(target.IsModified(GameTables.Moves, 1, "pp"));
        Assert.Equal(90, target.GetInt(GameTables.Moves, 3, "power"));
        Assert.Equal(3, target.Project.Edits.Count);
    }

    [Fact]
    public void FilesWithoutKind_HoldEverything()
    {
        string path = Path.Combine(dir, "old." + PokemonDataFile.Extension);
        File.WriteAllText(path, """{"magic":"pokemanager-pokemon-data","format":1,"scope":"Edits","tables":{"move":{"3":{"power":80}}}}""");

        var file = PokemonDataFile.Load(path);

        Assert.Equal(PokemonDataKind.All, file.Kind);
        Assert.Equal(GameTables.All.Count, PokemonDataFile.TablesOf(file.Kind).Count);
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
