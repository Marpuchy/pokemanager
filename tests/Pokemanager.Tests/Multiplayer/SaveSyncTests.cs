using PKHeX.Core;
using Pokemanager.Multiplayer;
using Pokemanager.Save;
using Pokemanager.Tests.Editing;
using GameData = Pokemanager.Model.Data.GameData;

namespace Pokemanager.Tests.Multiplayer;

public sealed class SaveSyncTests : IDisposable
{
    private readonly SyntheticRomFs romfs = new();
    private readonly string dir = Directory.CreateTempSubdirectory("pokemanager-savesync-").FullName;
    private DateTime clock = new(2026, 9, 14, 12, 0, 0);

    public void Dispose()
    {
        romfs.Dispose();
        Directory.Delete(dir, true);
    }

    private SaveDocument Open()
    {
        var sav = new SAV6XY { OT = "Ash", Language = (int)LanguageID.English };
        string path = Path.Combine(dir, "main");
        File.WriteAllBytes(path, sav.Write().ToArray());
        return SaveDocument.Open(path, GameData.Load(romfs.RomFs));
    }

    private static PKM Add(SaveDocument doc, SaveSlot slot, ushort species, int location, int metDay)
    {
        var pk = doc.Create(species, 10);
        pk.MetLocation = (ushort)location;
        pk.MetDay = (byte)metDay;
        doc.Set(slot, pk);
        return doc.Get(slot);
    }

    private SaveSnapshot Snapshot(SaveDocument doc) => SaveSnapshot.Read(doc, clock = clock.AddMinutes(10));

    [Fact]
    public void FirstSync_ReportsTheRunInCaptureOrder_ThenOnlyWhatChanged()
    {
        var doc = Open();
        var rules = new LinkRules { DeathDetection = DeathDetection.FaintedInParty | DeathDetection.GraveyardBox | DeathDetection.Released };
        var starter = Add(doc, new SaveSlot(null, 0), 1, location: 8, metDay: 1);
        var boxed = Add(doc, new SaveSlot(0, 0), 2, location: 12, metDay: 3);
        var second = Add(doc, new SaveSlot(null, 1), 3, location: 10, metDay: 2);
        var traded = doc.Create(4, 10);
        traded.OriginalTrainerName = "Gary";
        doc.Set(new SaveSlot(0, 1), traded);
        doc.SetMilestone(0, true);

        var room = LinkRoom.Create("Duo", "Ash", Snapshot(doc), rules);
        var first = room.Synchronize(Snapshot(doc));

        Assert.Equal([starter.EncryptionConstant, second.EncryptionConstant, boxed.EncryptionConstant],
            first.OfType<PokemonCaptured>().Select(c => c.Key));
        Assert.Equal(10, first.OfType<PokemonCaptured>().Single(c => c.Key == second.EncryptionConstant).Location);
        Assert.Equal(0, Assert.Single(first.OfType<MilestoneEarned>()).Index);
        Assert.Equal([starter.EncryptionConstant, second.EncryptionConstant], Assert.Single(first.OfType<SaveSynced>()).Party);
        Assert.Empty(first.OfType<PokemonDied>());

        // Play: the starter faints, the second one evolves, the boxed one goes to the graveyard, a new capture, a badge.
        var pk = doc.Get(new SaveSlot(null, 0));
        pk.Stat_HPCurrent = 0;
        doc.Set(new SaveSlot(null, 0), pk);
        pk = doc.Get(new SaveSlot(null, 1));
        pk.Species = 4;
        doc.Set(new SaveSlot(null, 1), pk);
        doc.Set(new SaveSlot(doc.BoxCount - 1, 0), doc.Get(new SaveSlot(0, 0)));
        doc.Clear(new SaveSlot(0, 0));
        var caught = Add(doc, new SaveSlot(1, 0), 2, location: 14, metDay: 4);
        doc.SetMilestone(1, true);

        var next = room.Synchronize(Snapshot(doc));

        Assert.Equal(caught.EncryptionConstant, Assert.Single(next.OfType<PokemonCaptured>()).Key);
        var evolved = Assert.Single(next.OfType<PokemonUpdated>());
        Assert.Equal((second.EncryptionConstant, (ushort)4), (evolved.Key, evolved.Species));
        Assert.Equal(
            [(starter.EncryptionConstant, DeathCause.FaintedInParty), (boxed.EncryptionConstant, DeathCause.GraveyardBox)],
            next.OfType<PokemonDied>().Select(d => (d.Key, d.Cause)));
        Assert.Equal(1, Assert.Single(next.OfType<MilestoneEarned>()).Index);

        // Nothing new: only the sync itself. Releasing is noticed.
        Assert.IsType<SaveSynced>(Assert.Single(room.Synchronize(Snapshot(doc))));
        doc.Clear(new SaveSlot(1, 0));
        Assert.Equal((caught.EncryptionConstant, DeathCause.Released),
            room.Synchronize(Snapshot(doc)).OfType<PokemonDied>().Select(d => (d.Key, d.Cause)).Single());

        var state = room.State();
        Assert.Equal("Ash", state.Players[room.LocalPlayerId].Name);
        Assert.Equal(3, state.PokemonOf(room.LocalPlayerId).Count(p => p.Status == PokemonStatus.Dead));
        Assert.Equal(2, state.Players[room.LocalPlayerId].Milestones.Count);
    }
}
