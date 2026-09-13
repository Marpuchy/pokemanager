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

    /// <summary>Partida X/Y en blanco con dos Pokémon en el equipo y uno en la caja 1.</summary>
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
            Nature = Nature.Adamant, // +Atq −AtE
            CurrentLevel = level,
            IV_HP = 31, IV_ATK = 31, IV_DEF = 31, IV_SPA = 31, IV_SPD = 31, IV_SPE = 31,
            EV_ATK = 252,
            OriginalTrainerName = "Prueba",
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

        var result = SaveUpdater.Apply(save, Rom(), Path.Combine(dir, "copias"));

        var sav = SaveUpdater.Load(save);
        Assert.True(sav.ChecksumsValid);
        var p0 = sav.GetPartySlotAtIndex(0);
        var p1 = sav.GetPartySlotAtIndex(1);
        var box = sav.GetBoxSlotAtIndex(0, 0);
        Assert.Equal(104, p0.Ability);  // especie 1, 2.ª habilidad: 101 + 3·1
        Assert.Equal(111, p1.Ability);  // especie 3, oculta: 102 + 3·3
        Assert.Equal(106, box.Ability); // especie 2 en caja, 1.ª: 100 + 3·2

        // Especie 1 del romfs sintético: bytes 0–5 = 20..25 en el orden de disco de X/Y (PS, Atq, Def, Vel, AtE, DfE).
        // Nivel 50, IV 31, 252 EV en Atq, Firme (+Atq −AtE).
        Assert.Equal((2 * 20 + 31) * 50 / 100 + 60, p0.Stat_HPMax);
        Assert.Equal(((2 * 21 + 31 + 63) * 50 / 100 + 5) * 110 / 100, p0.Stat_ATK);
        Assert.Equal((2 * 23 + 31) * 50 / 100 + 5, p0.Stat_SPE);
        Assert.Equal(((2 * 24 + 31) * 50 / 100 + 5) * 90 / 100, p0.Stat_SPA);
        Assert.Equal(p0.Stat_HPMax, p0.Stat_HPCurrent);

        Assert.NotNull(result.BackupPath);
        Assert.Equal(original, File.ReadAllBytes(result.BackupPath!));
        Assert.Equal(3, result.PokemonChecked);
        Assert.Contains(result.Changes, c => c.Location == "Caja 1, hueco 1" && c.Description.StartsWith("habilidad"));
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
        SaveUpdater.Apply(save, Rom(), Path.Combine(dir, "copias"));
        byte[] afterFirst = File.ReadAllBytes(save);

        var second = SaveUpdater.Apply(save, Rom(), Path.Combine(dir, "copias"));

        Assert.Empty(second.Changes);
        Assert.Null(second.BackupPath);
        Assert.Equal(afterFirst, File.ReadAllBytes(save));
    }

    [Fact]
    public void NotASave_Throws()
    {
        string path = Path.Combine(dir, "basura");
        File.WriteAllBytes(path, new byte[1234]);

        Assert.Throws<SaveUpdateException>(() => SaveUpdater.Preview(path, Rom()));
    }
}

/// <summary>
/// Partida real (se omite sin <c>POKEMANAGER_SAVE</c> y <c>POKEMANAGER_DUMP</c>). Trabaja sobre una copia.
/// Con la ROM original, la fórmula de stats debe reproducir exactamente las que guardó el juego.
/// </summary>
public class RealSaveTests
{
    [Fact]
    public void StatFormula_MatchesStatsStoredByTheGame()
    {
        string? save = Environment.GetEnvironmentVariable("POKEMANAGER_SAVE");
        string? dump = Environment.GetEnvironmentVariable("POKEMANAGER_DUMP");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(save) || string.IsNullOrWhiteSpace(dump) || !File.Exists(save),
            "Faltan POKEMANAGER_SAVE o POKEMANAGER_DUMP.");

        string copy = Path.GetTempFileName();
        try
        {
            File.Copy(save!, copy, overwrite: true);
            var result = SaveUpdater.Preview(copy, GameData.Load(Path.Combine(dump!, "romfs")));

            Assert.True(result.PokemonChecked > 0);
            Assert.DoesNotContain(result.Changes, c => c.Description.StartsWith("stats"));
        }
        finally
        {
            File.Delete(copy);
        }
    }
}
