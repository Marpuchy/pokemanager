using PKHeX.Core;
using Pokemanager.Save;
using Pokemanager.Tests.Editing;
using GameData = Pokemanager.Model.Data.GameData;

namespace Pokemanager.Tests.Save;

public sealed class SaveUpdaterTests : IDisposable
{
    private readonly SyntheticRomFs romfs = new();
    private readonly string dir = Directory.CreateTempSubdirectory("pokemanager-save-").FullName;

    public void Dispose()
    {
        romfs.Dispose();
        Directory.Delete(dir, true);
    }

    /// <summary>Blank X/Y save with two Pokémon in the party and one in box 1.</summary>
    private string CreateSave()
    {
        var sav = new SAV6XY();
        sav.SetPartySlotAtIndex(Pokemon(species: 1, abilityNumber: 2, ability: 5, level: 50), 0, EntityImportSettings.None);
        sav.SetPartySlotAtIndex(Pokemon(species: 3, abilityNumber: 4, ability: 5, level: 30), 1, EntityImportSettings.None);
        sav.SetBoxSlotAtIndex(Pokemon(species: 2, abilityNumber: 1, ability: 5, level: 10), 0, 0, EntityImportSettings.None);
        string path = Path.Combine(dir, "main");
        File.WriteAllBytes(path, sav.Write().ToArray());
        return path;
    }

    private static PK6 Pokemon(ushort species, int abilityNumber, int ability, byte level)
    {
        var pk = new PK6
        {
            Species = species,
            AbilityNumber = abilityNumber,
            Ability = ability,
            Nature = Nature.Adamant, // +Atk −SpA
            CurrentLevel = level,
            IV_HP = 31, IV_ATK = 31, IV_DEF = 31, IV_SPA = 31, IV_SPD = 31, IV_SPE = 31,
            EV_ATK = 252,
            OriginalTrainerName = "Test",
        };
        pk.Stat_Level = level;
        pk.Stat_HPMax = pk.Stat_HPCurrent = 1;
        pk.RefreshChecksum();
        return pk;
    }

    private GameData Rom() => GameData.Load(romfs.RomFs);

    [Fact]
    public void Apply_SetsAbilityBySlot_RecalculatesPartyStats_BacksUpAndVerifies()
    {
        string save = CreateSave();
        byte[] original = File.ReadAllBytes(save);

        var result = SaveUpdater.Apply(save, Rom(), Path.Combine(dir, "backups"));

        var sav = SaveUpdater.Load(save);
        Assert.True(sav.ChecksumsValid);
        var p0 = sav.GetPartySlotAtIndex(0);
        var p1 = sav.GetPartySlotAtIndex(1);
        var box = sav.GetBoxSlotAtIndex(0, 0);
        Assert.Equal(104, p0.Ability);  // species 1, 2nd ability: 101 + 3·1
        Assert.Equal(111, p1.Ability);  // species 3, hidden: 102 + 3·3
        Assert.Equal(106, box.Ability); // species 2 in a box, 1st: 100 + 3·2

        // Species 1 of the synthetic romfs: bytes 0–5 = 20..25 in X/Y on-disk order (HP, Atk, Def, Spe, SpA, SpD).
        // Level 50, IV 31, 252 Atk EVs, Adamant (+Atk −SpA).
        Assert.Equal((2 * 20 + 31) * 50 / 100 + 60, p0.Stat_HPMax);
        Assert.Equal(((2 * 21 + 31 + 63) * 50 / 100 + 5) * 110 / 100, p0.Stat_ATK);
        Assert.Equal((2 * 23 + 31) * 50 / 100 + 5, p0.Stat_SPE);
        Assert.Equal(((2 * 24 + 31) * 50 / 100 + 5) * 90 / 100, p0.Stat_SPA);
        Assert.Equal(p0.Stat_HPMax, p0.Stat_HPCurrent);

        Assert.NotNull(result.BackupPath);
        Assert.Equal(original, File.ReadAllBytes(result.BackupPath!));
        Assert.Equal(3, result.PokemonChecked);
        Assert.Contains(result.Changes, c => c.Slot == new SaveSlot(0, 0) && c.Kind == ChangeKind.Ability);
    }

    [Fact]
    public void Preview_DoesNotWrite()
    {
        string save = CreateSave();
        byte[] before = File.ReadAllBytes(save);

        var result = SaveUpdater.Preview(save, Rom());

        Assert.NotEmpty(result.Changes);
        Assert.Null(result.BackupPath);
        Assert.Equal(before, File.ReadAllBytes(save));
    }

    [Fact]
    public void Apply_Twice_SecondTimeHasNothingToChange()
    {
        string save = CreateSave();
        SaveUpdater.Apply(save, Rom(), Path.Combine(dir, "backups"));
        byte[] afterFirst = File.ReadAllBytes(save);

        var second = SaveUpdater.Apply(save, Rom(), Path.Combine(dir, "backups"));

        Assert.Empty(second.Changes);
        Assert.Null(second.BackupPath);
        Assert.Equal(afterFirst, File.ReadAllBytes(save));
    }

    [Fact]
    public void NotASave_Throws()
    {
        string path = Path.Combine(dir, "garbage");
        File.WriteAllBytes(path, new byte[1234]);

        Assert.Throws<SaveUpdateException>(() => SaveUpdater.Preview(path, Rom()));
    }
}

/// <summary>
/// Real save (skipped without <c>POKEMANAGER_SAVE</c> and <c>POKEMANAGER_DUMP</c>). Works on a copy.
/// With the original ROM, the stat formula must reproduce exactly the stats stored by the game.
/// </summary>
public class RealSaveTests
{
    [Fact]
    public void StatFormula_MatchesStatsStoredByTheGame()
    {
        string? save = Environment.GetEnvironmentVariable("POKEMANAGER_SAVE");
        string? dump = Environment.GetEnvironmentVariable("POKEMANAGER_DUMP");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(save) || string.IsNullOrWhiteSpace(dump) || !File.Exists(save),
            "POKEMANAGER_SAVE or POKEMANAGER_DUMP is not set.");

        string copy = Path.GetTempFileName();
        try
        {
            File.Copy(save!, copy, overwrite: true);
            var result = SaveUpdater.Preview(copy, GameData.Load(Path.Combine(dump!, "romfs")));

            Assert.True(result.PokemonChecked > 0);
            Assert.DoesNotContain(result.Changes, c => c.Kind == ChangeKind.Stats);
        }
        finally
        {
            File.Delete(copy);
        }
    }
}
