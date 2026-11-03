using pk3DS.Core.CTR;
using Pokemanager.Model.Build;
using Pokemanager.Model.Data;
using Pokemanager.Model.Dump;
using Pokemanager.Model.Editing;
using Pokemanager.Model.Edits;
using Pokemanager.Model.Projects;

namespace Pokemanager.Tests.Editing;

/// <summary>
/// Evolutions as a table: hand-made records for both generations (Generation 6 keeps the level in the argument,
/// Generation 7 has a level and a form of its own), and the real Ultra Sun game folder when it is there.
/// </summary>
public sealed class EvolutionTableTests
{
    private static byte[] Record6(ushort method, ushort argument, ushort species)
    {
        var data = new byte[0x30];
        BitConverter.GetBytes(method).CopyTo(data, 0);
        BitConverter.GetBytes(argument).CopyTo(data, 2);
        BitConverter.GetBytes(species).CopyTo(data, 4);
        return data;
    }

    private static byte[] Record7(ushort method, ushort argument, ushort species, sbyte form, byte level)
    {
        var data = new byte[0x40];
        BitConverter.GetBytes(method).CopyTo(data, 0);
        BitConverter.GetBytes(argument).CopyTo(data, 2);
        BitConverter.GetBytes(species).CopyTo(data, 4);
        data[6] = (byte)form;
        data[7] = level;
        return data;
    }

    [Fact]
    public void Gen6_TheLevelIsTheArgument_AndARecordRoundTrips()
    {
        byte[] raw = Record6(4, 16, 2); // Bulbasaur: level 16 into Ivysaur
        var slots = new EvolutionSlots(raw, generation7: false);

        var (index, e) = Assert.Single(slots.InUse());
        Assert.Equal(0, index);
        Assert.Equal(16, EvolutionSlots.LevelOf(e, generation7: false));
        Assert.Equal(raw, slots.Write());

        e.Method = 8; // use an item
        e.Argument = 82; // Fire Stone
        var reread = new EvolutionSlots(slots.Write(), generation7: false).Slot(0)!;
        Assert.Equal(8, reread.Method);
        Assert.Equal(82, reread.Argument);
        Assert.Equal(EvolutionArgument.Item, EvolutionSlots.ArgumentOf(reread.Method));
    }

    [Fact]
    public void Gen7_HasItsOwnLevelAndForm()
    {
        var slots = new EvolutionSlots(Record7(33, 0, 105, 1, 28), generation7: true); // Cubone at night into Alolan Marowak
        var e = slots.Slot(0)!;
        Assert.Equal(28, EvolutionSlots.LevelOf(e, generation7: true));
        Assert.Equal(1, e.Form);
        Assert.Equal(0, slots.Slot(1)!.Method); // empty slots stay empty
        Assert.Single(slots.InUse());
    }

    [Fact]
    public void ARecordOfAnotherSize_IsKeptAsItWas()
    {
        byte[] odd = [1, 2, 3];
        var slots = new EvolutionSlots(odd, generation7: true);
        Assert.False(slots.IsReadable);
        Assert.Empty(slots.InUse());
        Assert.Equal(odd, slots.Write());
    }

    /// <summary>Real data: Bulbasaur evolves at 16, an edit changes one record of the archive and nothing else.</summary>
    [Fact]
    public void OnTheRealGame_AnEditBuildsOnlyThatRecord()
    {
        string? dump = Environment.GetEnvironmentVariable("POKEMANAGER_USUM_DUMP");
        Assert.SkipWhen(string.IsNullOrEmpty(dump), "POKEMANAGER_USUM_DUMP is not set.");

        var project = new Project { DumpDirectory = dump!, Game = GameTitle.UltraSun };
        var session = EditorSession.Open(project, null);
        Assert.Equal(16, session.GetInt(GameTables.Evolutions, 1, "e1.level"));
        Assert.Equal(4, session.GetInt(GameTables.Evolutions, 1, "e1.method"));
        Assert.Equal(2, session.GetInt(GameTables.Evolutions, 1, "e1.species"));

        session.SetInt(GameTables.Evolutions, 1, "e1.level", 10);
        var outputs = ModBuilder.BuildEdits(session);
        string path = "romfs/" + GameTitle.UltraSun.Layout().Evolution;
        Assert.True(outputs.ContainsKey(path));

        var before = new GARC.MemGARC(File.ReadAllBytes(Path.Combine(project.RomFsPath, GameTitle.UltraSun.Layout().Evolution)));
        var after = new GARC.MemGARC(outputs[path]);
        Assert.Equal(before.FileCount, after.FileCount);
        var changed = Enumerable.Range(0, before.FileCount).Where(i => !before.GetFile(i).SequenceEqual(after.GetFile(i))).ToList();
        Assert.Equal([1], changed);
        Assert.Equal(10, after.GetFile(1)[7]);
    }
}
