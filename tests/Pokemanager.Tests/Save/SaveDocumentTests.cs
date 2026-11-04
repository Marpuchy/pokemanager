using PKHeX.Core;
using Pokemanager.Save;
using Pokemanager.Tests.Editing;
using GameData = Pokemanager.Model.Data.GameData;

namespace Pokemanager.Tests.Save;

public sealed class SaveDocumentTests : IDisposable
{
    private readonly SyntheticRomFs romfs = new();
    private readonly string dir = Directory.CreateTempSubdirectory("pokemanager-savedoc-").FullName;

    public void Dispose()
    {
        romfs.Dispose();
        Directory.Delete(dir, true);
    }

    /// <summary>Blank X/Y save with one Pokémon (species 1, level 20) in the party.</summary>
    private SaveDocument Open()
    {
        var sav = new SAV6XY { OT = "Test", Language = (int)LanguageID.English };
        string path = Path.Combine(dir, "main");
        File.WriteAllBytes(path, sav.Write().ToArray());
        var doc = SaveDocument.Open(path, GameData.Load(romfs.RomFs));
        doc.Set(new SaveSlot(null, 0), doc.Create(1, 20));
        return doc;
    }

    /// <summary>Drag and drop in the save editor (2026-11-04, user): boxes and party, swapping when the target is taken.</summary>
    [Fact]
    public void Move_BetweenBoxesAndParty_SwapsAndKeepsTheParty()
    {
        var doc = Open();                                   // party: species 1
        var party0 = new SaveSlot(null, 0);
        var box0 = new SaveSlot(0, 0);
        var box1 = new SaveSlot(0, 1);
        doc.Set(box0, doc.Create(2, 10));

        // The last Pokémon of the party cannot go into an empty box slot.
        Assert.Equal(SaveDocument.MoveResult.PartyWouldBeEmpty, doc.Move(party0, box1, out _));

        // Box → end of the party.
        Assert.Equal(SaveDocument.MoveResult.Moved, doc.Move(box0, new SaveSlot(null, 4), out var placed));
        Assert.Equal(new SaveSlot(null, 1), placed);
        Assert.Equal(2, doc.PartyCount);
        Assert.True(doc.IsEmpty(box0));
        var inParty = doc.Get(placed);
        Assert.Equal(2, inParty.Species);
        Assert.Equal(doc.Stats(inParty), (int[])[inParty.Stat_HPMax, inParty.Stat_ATK, inParty.Stat_DEF, inParty.Stat_SPE, inParty.Stat_SPA, inParty.Stat_SPD]);

        // Party → empty box slot: the party closes the gap.
        Assert.Equal(SaveDocument.MoveResult.Moved, doc.Move(party0, box1, out placed));
        Assert.Equal(box1, placed);
        Assert.Equal(1, doc.PartyCount);
        Assert.Equal(2, doc.Get(party0).Species);
        Assert.Equal(1, doc.Get(box1).Species);

        // Party ↔ occupied box slot swap.
        Assert.Equal(SaveDocument.MoveResult.Moved, doc.Move(party0, box1, out _));
        Assert.Equal(1, doc.Get(party0).Species);
        Assert.Equal(2, doc.Get(box1).Species);

        // Box ↔ box swap and reorder inside the party.
        doc.Set(box0, doc.Create(3, 5));
        Assert.Equal(SaveDocument.MoveResult.Moved, doc.Move(box0, box1, out _));
        Assert.Equal(2, doc.Get(box0).Species);
        Assert.Equal(3, doc.Get(box1).Species);
        doc.Move(box0, new SaveSlot(null, 5), out _);         // party: 1, 2
        Assert.Equal(SaveDocument.MoveResult.Moved, doc.Move(new SaveSlot(null, 0), new SaveSlot(null, 1), out placed));
        Assert.Equal((2, 1), ((int)doc.Get(party0).Species, (int)doc.Get(new SaveSlot(null, 1)).Species));
        Assert.Equal(new SaveSlot(null, 1), placed);

        // A full party takes nothing more from a box.
        for (int i = 0; i < 4; i++)
            doc.Set(new SaveSlot(null, 6), doc.Create(1, 5));
        Assert.Equal(6, doc.PartyCount);
        Assert.Equal(SaveDocument.MoveResult.PartyFull, doc.Move(box1, new SaveSlot(null, 6), out _));
        Assert.Empty(doc.Validate());
    }

    [Fact]
    public void Create_UsesTheRom_AbilityGrowthMovesAndStats()
    {
        var doc = Open();

        var pk = doc.Get(new SaveSlot(null, 0));

        Assert.Equal(1, pk.Species);
        Assert.Equal(20, doc.Level(pk));
        Assert.Equal(Experience.GetEXP(20, 0), pk.EXP); // synthetic ROM growth rate 0
        Assert.Equal(1, pk.AbilityNumber);
        Assert.Equal(103, pk.Ability); // synthetic ROM: species 1, first ability = 100 + 3·1
        Assert.Equal([1, 2, 0, 0], pk.Moves.Select(m => (int)m)); // learnset: move 1 at level 1, move 2 at level 5 (oldest first)
        Assert.Equal(doc.MaxPP(2, 0), pk.Move2_PP);
        int[] stored = [pk.Stat_HPMax, pk.Stat_ATK, pk.Stat_DEF, pk.Stat_SPE, pk.Stat_SPA, pk.Stat_SPD];
        Assert.Equal(doc.Stats(pk), stored);
        Assert.Equal("Test", pk.OriginalTrainerName);
        Assert.Empty(doc.Check(pk));
    }

    [Fact]
    public void Set_NormalizesAbilityNumberAndStats_AfterChangingSpeciesAndLevel()
    {
        var doc = Open();
        var slot = new SaveSlot(null, 0);
        var pk = doc.Get(slot);

        pk.Species = 3;
        pk.AbilityNumber = 4; // hidden
        doc.SetLevel(pk, 50);
        doc.Set(slot, pk);

        var stored = doc.Get(slot);
        Assert.Equal(111, stored.Ability); // species 3 hidden: 102 + 3·3
        Assert.Equal(50, stored.Stat_Level);
        Assert.Equal(doc.Stats(stored)[0], stored.Stat_HPMax);
        Assert.True(doc.IsDirty);
    }

    [Fact]
    public void Validate_BlocksWhatWouldBreakTheGame()
    {
        var doc = Open();
        var slot = new SaveSlot(null, 0);
        var pk = doc.Get(slot);
        pk.EV_HP = 252; pk.EV_ATK = 252; pk.EV_DEF = 252;
        pk.Move1 = 3; pk.Move2 = 3;
        pk.Move3 = 999;
        doc.Set(slot, pk);

        var problems = doc.Validate();

        Assert.Equal(3, problems.Count);
        Assert.All(problems, p => Assert.Equal(slot, p.Slot));
        Assert.Throws<SaveUpdateException>(() => doc.Write(Path.Combine(dir, "backups")));
    }

    [Fact]
    public void LastPartyPokemon_CannotBeRemoved_BoxSlotsCan()
    {
        var doc = Open();
        doc.Set(new SaveSlot(4, 7), doc.Create(2, 10));

        Assert.False(doc.Clear(new SaveSlot(null, 0)));
        Assert.True(doc.Clear(new SaveSlot(4, 7)));
        Assert.True(doc.IsEmpty(new SaveSlot(4, 7)));
    }

    [Fact]
    public void PartySlotsFillInOrder()
    {
        var doc = Open();

        var placed = doc.Set(new SaveSlot(null, 5), doc.Create(2, 5));

        Assert.Equal(new SaveSlot(null, 1), placed);
        Assert.Equal(2, doc.PartyCount);
    }

    [Fact]
    public void TrainerExtras_RoundTrip()
    {
        var doc = Open();
        var started = new DateTime(2026, 5, 26, 10, 30, 0);
        doc.MegaEvolutionUnlocked = true;
        doc.Vivillon = 99; // clamped to 19
        doc.BoxesUnlocked = 20;
        doc.SetSaying(0, "Hi there!");
        doc.SetSaying(4, new string('x', 40)); // too long: ignored
        doc.GameStarted = started;
        doc.HallOfFame = started.AddDays(3);
        doc.SetRecord(0, 12345);
        doc.SetMaison(PKHeX.Core.BattleStyle6.Double, current: false, super: true, 49);
        doc.OPowerPoints = 50;
        doc.UnlockAllOPowers();
        doc.FillPokePuffs();
        doc.SetPosition(157, 100.5f, 0, -20, 0);

        doc.Write(Path.Combine(dir, "backups"));
        var reread = SaveDocument.Open(doc.SavePath, GameData.Load(romfs.RomFs));

        Assert.True(reread.MegaEvolutionUnlocked);
        Assert.Equal(19, reread.Vivillon);
        Assert.Equal(20, reread.BoxesUnlocked);
        Assert.Equal("Hi there!", reread.GetSaying(0));
        Assert.NotEqual(40, reread.GetSaying(4).Length);
        Assert.Equal(started, reread.GameStarted);
        Assert.Equal(started.AddDays(3), reread.HallOfFame);
        Assert.Equal(12345, reread.GetRecord(0));
        Assert.Equal(49, reread.GetMaison(PKHeX.Core.BattleStyle6.Double, false, true));
        Assert.Equal(0, reread.GetMaison(PKHeX.Core.BattleStyle6.Double, true, true));
        Assert.Equal(50, reread.OPowerPoints);
        Assert.True(reread.PokePuffCount > 0);
        Assert.Equal((157, 100.5f, 0f, -20f, 0), reread.Position);
        Assert.NotEmpty(reread.RecordNames);
    }

    [Fact]
    public void GiveItem_AddsToTheRightPocket_StacksAndCaps()
    {
        var doc = Open();
        const int rareCandy = 50, masterBall = 1;

        Assert.Equal(3, doc.GiveItem(rareCandy, 3));
        Assert.Equal(2, doc.GiveItem(rareCandy, 2));
        Assert.Equal(1, doc.GiveItem(masterBall, 1));
        var medicine = doc.Pouches.First(p => p.Type == InventoryType.Medicine);
        Assert.Equal(5, medicine.Items.Single(i => i.Index == rareCandy).Count);
        Assert.Equal(medicine.MaxCount - 5, doc.GiveItem(rareCandy, 5000));
        Assert.Equal(0, doc.GiveItem(rareCandy, 1));
        Assert.Contains(doc.Pouches.First(p => p.Type == InventoryType.Items).Items, i => i.Index == masterBall);
        Assert.Equal(0, doc.GiveItem(0, 1));

        doc.Write(Path.Combine(dir, "backups"));
        var reread = SaveDocument.Open(doc.SavePath, GameData.Load(romfs.RomFs));
        Assert.Equal(medicine.MaxCount, reread.Pouches.First(p => p.Type == InventoryType.Medicine).Items.Single(i => i.Index == rareCandy).Count);
    }

    [Fact]
    public void Write_RefusedWhenTheGameSavedMeanwhile()
    {
        var doc = Open();
        doc.TrainerName = "Mine";
        var other = new SAV6XY { OT = "Game" };
        File.WriteAllBytes(doc.SavePath, other.Write().ToArray()); // the emulator saved

        Assert.True(doc.ChangedOnDisk());
        Assert.Throws<SaveUpdateException>(() => doc.Write(Path.Combine(dir, "backups")));
        Assert.Equal("Game", SaveDocument.Open(doc.SavePath, GameData.Load(romfs.RomFs)).TrainerName);
    }

    [Fact]
    public void ReplaceFile_BacksUpCurrentAndRejectsNonSaves()
    {
        var doc = Open();
        doc.Write(Path.Combine(dir, "backups"));
        string copy = Path.Combine(dir, "copy");
        File.WriteAllBytes(copy, new SAV6XY { OT = "Old" }.Write().ToArray());
        string junk = Path.Combine(dir, "junk");
        File.WriteAllBytes(junk, new byte[100]);

        string backup = SaveDocument.ReplaceFile(doc.SavePath, copy, Path.Combine(dir, "backups"));

        Assert.Equal("Old", SaveDocument.Open(doc.SavePath, GameData.Load(romfs.RomFs)).TrainerName);
        Assert.Equal("Test", SaveDocument.Open(backup, GameData.Load(romfs.RomFs)).TrainerName);
        Assert.Throws<SaveUpdateException>(() => SaveDocument.ReplaceFile(doc.SavePath, junk, Path.Combine(dir, "backups")));
    }

    [Fact]
    public void Write_TrainerBagDex_BackupAndReread()
    {
        var doc = Open();
        doc.TrainerName = "Puchy";
        doc.Money = 50_000_000; // clamped
        doc.SetBadge(2, true);
        var pouch = doc.Pouches.First(p => p.Type == InventoryType.Medicine);
        ushort potion = doc.PouchItems(pouch)[0];
        doc.SetPouch(pouch, [(potion, 5)]);
        doc.SetCaught(4, true);

        var result = doc.Write(Path.Combine(dir, "backups"));

        Assert.False(doc.IsDirty);
        Assert.True(File.Exists(result.BackupPath));
        var reread = SaveDocument.Open(result.SavePath, GameData.Load(romfs.RomFs));
        Assert.Equal("Puchy", reread.TrainerName);
        Assert.Equal((uint)SaveDocument.MaxMoney, reread.Money);
        Assert.True(reread.GetBadge(2));
        Assert.False(reread.GetBadge(1));
        var item = reread.Pouches.First(p => p.Type == InventoryType.Medicine).Items[0];
        Assert.Equal((potion, 5), ((ushort)item.Index, item.Count));
        Assert.True(reread.GetCaught(4) && reread.GetSeen(4));
        Assert.Equal(1, reread.PartyCount);
    }
}
