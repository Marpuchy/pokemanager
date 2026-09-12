using System.Text.Json.Nodes;
using Pokemanager.Model.Editing;
using Pokemanager.Model.Edits;
using Pokemanager.Model.Projects;

namespace Pokemanager.Tests.Editing;

public class EditorSessionTests
{
    private static EditorSession NewSession(SyntheticRomFs romfs) =>
        EditorSession.Open(new Project { DumpDirectory = romfs.DumpDirectory });

    [Fact]
    public void SetInt_ChangesCurrentAndRecordsEdit_OriginalUntouched()
    {
        using var romfs = new SyntheticRomFs();
        var session = NewSession(romfs);

        session.SetInt(GameTables.Personal, 1, "hp", 150);

        Assert.Equal(150, session.GetInt(GameTables.Personal, 1, "hp"));
        Assert.Equal(20, session.GetOriginal(GameTables.Personal, 1, "hp").GetValue<int>());
        Assert.True(session.IsModified(GameTables.Personal, 1, "hp"));
        var edit = Assert.Single(session.Project.Edits.All);
        Assert.Equal(new EditKey("personal", 1, "hp"), edit.Key);
        Assert.Equal(150, edit.Value.GetValue<int>());
    }

    [Fact]
    public void SettingBackToOriginal_RemovesEdit()
    {
        using var romfs = new SyntheticRomFs();
        var session = NewSession(romfs);

        session.SetInt(GameTables.Personal, 1, "hp", 150);
        session.SetInt(GameTables.Personal, 1, "hp", 20);

        Assert.Equal(0, session.Project.Edits.Count);
        Assert.False(session.IsModified(GameTables.Personal, 1));
    }

    [Fact]
    public void Revert_UndoesAllFieldsOfEntry()
    {
        using var romfs = new SyntheticRomFs();
        var session = NewSession(romfs);
        session.SetInt(GameTables.Personal, 2, "atk", 200);
        session.SetInt(GameTables.Personal, 2, "type2", 9);
        session.SetInt(GameTables.Personal, 3, "atk", 1);

        session.Revert(GameTables.Personal, 2);

        Assert.Equal(31, session.GetInt(GameTables.Personal, 2, "atk"));
        Assert.Equal(2, session.GetInt(GameTables.Personal, 2, "type2"));
        Assert.Single(session.Project.Edits.All);
    }

    [Fact]
    public void Learnset_IsStoredSortedByLevel()
    {
        using var romfs = new SyntheticRomFs();
        var session = NewSession(romfs);

        session.Set(GameTables.Learnsets, 0, GameTables.LevelUp, JsonNode.Parse("[[20,3],[1,1],[7,2]]")!);

        Assert.Equal("[[1,1],[7,2],[20,3]]", session.Get(GameTables.Learnsets, 0, GameTables.LevelUp).ToJsonString());
        Assert.True(session.IsModified(GameTables.Learnsets, 0));
    }

    [Fact]
    public void NegativeSignedFields_RoundTrip()
    {
        using var romfs = new SyntheticRomFs();
        var session = NewSession(romfs);

        session.SetInt(GameTables.Moves, 0, "priority", -7);

        Assert.Equal(-7, session.GetInt(GameTables.Moves, 0, "priority"));
    }

    [Fact]
    public void ProjectSaveAndLoad_ReappliesEdits()
    {
        using var romfs = new SyntheticRomFs();
        var session = NewSession(romfs);
        session.SetInt(GameTables.Personal, 4, "spe", 99);
        session.SetInt(GameTables.Moves, 3, "power", 120);
        session.Set(GameTables.Learnsets, 2, GameTables.LevelUp, JsonNode.Parse("[[1,3]]")!);
        session.Project.EmulatorUserDirectory = @"C:\emu";
        string file = Path.Combine(romfs.Root, "proyecto.json");

        session.Project.Save(file);
        var reopened = EditorSession.Open(Project.Load(file));

        Assert.Equal(3, reopened.Project.Edits.Count);
        Assert.Equal(99, reopened.GetInt(GameTables.Personal, 4, "spe"));
        Assert.Equal(120, reopened.GetInt(GameTables.Moves, 3, "power"));
        Assert.Equal("[[1,3]]", reopened.Get(GameTables.Learnsets, 2, GameTables.LevelUp).ToJsonString());
        Assert.Equal(@"C:\emu", reopened.Project.EmulatorUserDirectory);
        Assert.DoesNotContain(File.ReadAllText(file), "\u0000"); // texto legible, sin bytes del juego
    }

    [Fact]
    public void Open_WithEditOutOfRange_Throws()
    {
        using var romfs = new SyntheticRomFs();
        var project = new Project { DumpDirectory = romfs.DumpDirectory };
        project.Edits.Set(GameTables.Personal, 999, "hp", JsonValue.Create(1));

        Assert.Throws<InvalidDataException>(() => EditorSession.Open(project));
    }
}
