using System.Text.Json.Nodes;
using pk3DS.Core.CTR;
using pk3DS.Core.Structures.PersonalInfo;
using Pokemanager.Bridge;
using Pokemanager.Model.Build;
using Pokemanager.Model.Data;
using Pokemanager.Model.Dump;
using Pokemanager.Model.Editing;
using Pokemanager.Model.Edits;
using Pokemanager.Model.Projects;

namespace Pokemanager.Tests.Editing;

public sealed class ModBuilderTests : IDisposable
{
    private readonly SyntheticRomFs romfs = new();
    private readonly string modDir = Path.Combine(Path.GetTempPath(), "pokemanager-mod-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        romfs.Dispose();
        if (Directory.Exists(modDir))
            Directory.Delete(modDir, true);
    }

    private EditorSession NewSession(string? randomizedRomFs = null) =>
        EditorSession.Open(new Project { DumpDirectory = romfs.DumpDirectory }, randomizedRomFs);

    private InstallResult Install(EditorSession session, string? randomizedTitle = null) =>
        ModInstaller.Install(modDir, romfs.DumpDirectory, randomizedTitle, ModBuilder.BuildEdits(session));

    /// <summary>Simulates UPR output: &lt;TitleID&gt;/romfs with a different personal table and a code.bin.</summary>
    private string FakeRandomizerOutput(int bulbasaurHp)
    {
        string title = Path.Combine(romfs.Root, "upr", "0004000000055D00");
        byte[][] personal = romfs.ReadGarc(romfs.RomFs, GameData.PersonalGarc);
        personal[1] = (byte[])personal[1].Clone();
        personal[1][0] = (byte)bulbasaurHp;
        personal[^1] = personal[..^1].SelectMany(p => p).ToArray();
        string path = Path.Combine(title, "romfs", "a", "2", "1", "8");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, GARC.PackGARC(personal, GARC.VER_4, 4).Data);
        File.WriteAllBytes(Path.Combine(title, "romfs", "DllField.cro"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(title, "code.bin"), new byte[0x200]);
        return title;
    }

    [Fact]
    public void NoEdits_NoRandomizer_WritesNothing()
    {
        var result = Install(NewSession());

        Assert.Empty(result.Written);
        Assert.False(Directory.Exists(Path.Combine(modDir, "romfs")));
    }

    [Fact]
    public void PersonalEdit_UpdatesEntryAndConcatenatedTable_RestIdentical()
    {
        var session = NewSession();
        session.SetInt(GameTables.Personal, 1, "hp", 150);

        var result = Install(session);

        Assert.Equal(["romfs/a/2/1/8"], result.Written);
        byte[][] original = romfs.ReadGarc(romfs.RomFs, GameData.PersonalGarc);
        byte[][] built = romfs.ReadGarc(Path.Combine(modDir, "romfs"), GameData.PersonalGarc);

        Assert.Equal(original.Length, built.Length);
        Assert.Equal(150, new PersonalInfoXY(built[1]).HP);
        Assert.Equal(150, built[^1][0x40 * 1]);
        for (int i = 0; i < original.Length - 1; i++)
        {
            if (i != 1)
                Assert.Equal(original[i], built[i]);
        }
        Assert.Equal(built[..^1].SelectMany(f => f).ToArray(), built[^1]);
    }

    [Fact]
    public void MoveAndLearnsetEdits_AreWritten()
    {
        var session = NewSession();
        session.SetInt(GameTables.Moves, 2, "power", 250);
        session.Set(GameTables.Learnsets, 3, GameTables.LevelUp, JsonNode.Parse("[[1,3],[50,2]]")!);

        var result = Install(session);

        Assert.Equal(2, result.Written.Count);
        byte[][] moves = romfs.ReadGarc(Path.Combine(modDir, "romfs"), GameData.MoveGarc);
        Assert.Equal(250, moves[2][0x03]);
        Assert.Equal(36, moves[2].Length);
        var learnset = new pk3DS.Core.Structures.Learnset6(romfs.ReadGarc(Path.Combine(modDir, "romfs"), GameData.LevelUpGarc)[3]);
        Assert.Equal([3, 2], learnset.Moves);
        Assert.Equal([1, 50], learnset.Levels);
        Assert.False(File.Exists(Path.Combine(modDir, "romfs", "a", "2", "1", "8")));
    }

    [Fact]
    public void MoveDescription_EnglishBase_EditGoesToEveryLanguage()
    {
        var session = NewSession();
        // The base shown is the English text (language 2), with real line breaks.
        Assert.Equal("Move 2\n(language 2)", session.GetOriginal(GameTables.MoveTexts, 2, GameTables.Description).GetValue<string>());

        session.Set(GameTables.MoveTexts, 2, GameTables.Description, JsonValue.Create("Hits hard.\r\nUses [brackets] and \\ too.  "));
        // The same text as the base again is not an edit.
        session.Set(GameTables.MoveTexts, 1, GameTables.Description, JsonValue.Create("Move 1\n(language 2)\n"));
        Assert.False(session.IsModified(GameTables.MoveTexts, 1, GameTables.Description));

        var outputs = ModBuilder.BuildEdits(session);

        var title = GameTitle.X;
        int file = GameText.MoveDescriptionFile(title);
        Assert.Equal(title.Layout().LanguageCount, outputs.Count);
        for (int language = 0; language < title.Layout().LanguageCount; language++)
        {
            byte[] archive = outputs["romfs/" + GameText.Archive(title, language)];
            byte[][] files = new GARC.MemGARC(archive).Files;
            string[] lines = GameText.Read(title, files[file]);
            Assert.Equal(@"Hits hard.\nUses \[brackets] and \\ too.", lines[2]);
            Assert.Equal(SyntheticRomFs.Description(1, language), lines[1]); // untouched, in its own language
            Assert.Equal(SyntheticRomFs.Description(3, language), lines[3]);
            Assert.Equal(romfs.ReadGarc(romfs.RomFs, GameText.Archive(title, language))[0], files[0]); // other text files as they were
        }
    }

    [Fact]
    public void EditableText_KeepsVariables_EscapesTheRest()
    {
        const string line = @"Raises [VAR 0100(0000)]\nby \\ one \[stage].";
        string editable = GameText.ToEditable(line);

        Assert.Equal("Raises [VAR 0100(0000)]\nby \\ one [stage].", editable);
        Assert.Equal(line, GameText.FromEditable(editable));
    }

    [Fact]
    public void RandomizedBase_IsTheOriginalForEditing()
    {
        string title = FakeRandomizerOutput(bulbasaurHp: 99);

        var session = NewSession(Path.Combine(title, "romfs"));

        Assert.Equal(99, session.GetOriginal(GameTables.Personal, 1, "hp").GetValue<int>());
        Assert.Equal(30, session.GetInt(GameTables.Personal, 2, "hp")); // untouched by the randomizer: comes from the dump
        session.SetInt(GameTables.Personal, 1, "hp", 20); // the dump value is now an edit on top of the random base
        Assert.True(session.IsModified(GameTables.Personal, 1, "hp"));
    }

    [Fact]
    public void Install_CopiesRandomizer_CodeToExefs_EditsOnTop()
    {
        string title = FakeRandomizerOutput(bulbasaurHp: 99);
        var session = NewSession(Path.Combine(title, "romfs"));
        session.SetInt(GameTables.Personal, 2, "atk", 200);

        var result = Install(session, title);

        Assert.Contains("romfs/DllField.cro", result.Written);
        Assert.Contains("exefs/code.bin", result.Written);
        Assert.True(File.Exists(Path.Combine(modDir, "exefs", "code.bin")));
        byte[][] personal = romfs.ReadGarc(Path.Combine(modDir, "romfs"), GameData.PersonalGarc);
        Assert.Equal(99, new PersonalInfoXY(personal[1]).HP);   // from the randomizer
        Assert.Equal(200, new PersonalInfoXY(personal[2]).ATK); // from the edit
        Assert.Single(result.Written, w => w == "romfs/a/2/1/8");
    }

    [Fact]
    public void Reinstall_WithoutRandomizer_RemovesItsFiles_KeepsForeignFiles()
    {
        string title = FakeRandomizerOutput(bulbasaurHp: 99);
        Install(NewSession(Path.Combine(title, "romfs")), title);
        string foreign = Path.Combine(modDir, "romfs", "other-mod.bin");
        File.WriteAllText(foreign, "not ours");

        var result = Install(NewSession());

        Assert.Equal(3, result.Removed.Count);
        Assert.False(File.Exists(Path.Combine(modDir, "exefs", "code.bin")));
        Assert.True(File.Exists(foreign));
    }

    [Fact]
    public void ModDirectoryInsideDump_IsRefused()
    {
        var session = NewSession();
        session.SetInt(GameTables.Personal, 0, "hp", 1);

        Assert.Throws<InvalidOperationException>(() =>
            ModInstaller.Install(Path.Combine(romfs.RomFs, "mods"), romfs.DumpDirectory, null, ModBuilder.BuildEdits(session)));
    }

    [Fact]
    public void ModDirectory_FollowsEmulatorLayout()
    {
        string dir = EmulatorUserFolders.ModDirectory(Path.Combine("C:", "Azahar"), "0004000000055D00");

        Assert.Equal(Path.Combine("C:", "Azahar", "load", "mods", "0004000000055D00"), dir);
    }
}
