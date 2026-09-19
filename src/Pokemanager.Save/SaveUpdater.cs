using PKHeX.Core;
using Pokemanager.Save.Resources;
using GameData = Pokemanager.Model.Data.GameData;

namespace Pokemanager.Save;

/// <summary>Where a Pokémon is in the save: a party slot (<see cref="Box"/> null) or a box slot. Zero-based.</summary>
public readonly record struct SaveSlot(int? Box, int Slot)
{
    public bool IsParty => Box is null;
}

public enum ChangeKind
{
    /// <summary><see cref="PokemonChange.Before"/>/<see cref="PokemonChange.After"/> hold one ability ID.</summary>
    Ability,

    /// <summary>Before/After hold the six party stats: HP, Atk, Def, Spe, SpA, SpD.</summary>
    Stats,
}

/// <summary>A change applied to a Pokémon in the save.</summary>
public sealed record PokemonChange(SaveSlot Slot, ushort Species, ChangeKind Kind, IReadOnlyList<int> Before, IReadOnlyList<int> After);

public sealed record SaveUpdateResult(string SavePath, string? BackupPath, int PokemonChecked, IReadOnlyList<PokemonChange> Changes);

public sealed class SaveUpdateException(string message) : Exception(message);

/// <summary>Adapts a save to another version of the ROM (for example, another randomizer seed).</summary>
/// <remarks>
/// In Generations 6 and 7 the save stores each Pokémon's ability and the party stats; the ROM only provides each species'
/// base stats and possible abilities. So when the ROM changes:
/// <list type="bullet">
/// <item>each Pokémon gets the ability with the same number (1, 2 or hidden) in its species' new entry;</item>
/// <item>party stats are recalculated from the new base stats (box stats are computed by the game on withdrawal).</item>
/// </list>
/// Species, level, IVs, EVs, nature and learned moves are not touched.
/// </remarks>
public static class SaveUpdater
{
    /// <summary>Size of the X/Y <c>main</c> file.</summary>
    public const int SizeXY = 0x65600;

    /// <summary>
    /// Loads the save. If PKHeX auto-detection does not recognize it but it has the X/Y size (e.g. a freshly created
    /// save), it is opened as X/Y; checksums are still checked before modifying it.
    /// </summary>
    public static SaveFile Load(string savePath) =>
        Parse(File.ReadAllBytes(savePath)) ?? throw new SaveUpdateException(string.Format(Strings.Save_NotRecognized, savePath));

    /// <summary>
    /// PKHeX detection, or by exact size when it does not recognize the file (a save the game has not fully initialized):
    /// X/Y 0x65600, Omega Ruby/Alpha Sapphire 0x76000, Sun/Moon 0x6BE00, Ultra Sun/Ultra Moon 0x6CC00.
    /// </summary>
    /// <remarks>
    /// **PKHeX parses a copy, never the caller's bytes.** Loading a Generation 7 save clears its MemeCrypto signature
    /// (the signed SHA-256 of the block checksum table, <c>0x6C100</c> in Ultra Sun/Ultra Moon) **in the array it is
    /// given**. Writing re-signs, so that is harmless for PKHeX's own copy — but the writer used to "verify" the bytes it
    /// was about to write by parsing them, and so wrote them with the signature wiped. The game treats an unsigned save as
    /// corrupted (measured on the user's Ultra Moon, 2026-09-19: the only difference between the save the game accepted
    /// and the one it refused was those 0x80 bytes, all zero).
    /// </remarks>
    internal static SaveFile? Parse(byte[] source)
    {
        byte[] data = source.ToArray();
        return SaveUtil.GetSaveFile(data) ?? data.Length switch
        {
            SizeXY => new SAV6XY(data),
            0x76000 => new SAV6AO(data),
            0x6BE00 => new SAV7SM(data),
            0x6CC00 => new SAV7USUM(data),
            _ => null,
        };
    }

    /// <summary>The 3DS games Pokemanager edits: X/Y, Omega Ruby/Alpha Sapphire, Sun/Moon, Ultra Sun/Ultra Moon.</summary>
    public static bool IsSupported(SaveFile sav) => sav is SAV6XY or SAV6AO or SAV7SM or SAV7USUM;

    /// <summary>Computes the changes without writing anything.</summary>
    public static SaveUpdateResult Preview(string savePath, GameData rom) => Run(savePath, rom, backupRoot: null, write: false);

    /// <summary>Applies the changes: backup in <paramref name="backupRoot"/>, write, and verification by re-reading the file.</summary>
    public static SaveUpdateResult Apply(string savePath, GameData rom, string backupRoot) => Run(savePath, rom, backupRoot, write: true);

    private static SaveUpdateResult Run(string savePath, GameData rom, string? backupRoot, bool write)
    {
        var sav = Load(savePath);
        if (!IsSupported(sav))
            throw new SaveUpdateException(string.Format(Strings.Save_NotSupported, sav.GetType().Name));
        if (!sav.ChecksumsValid)
            throw new SaveUpdateException(Strings.Save_BadChecksums);

        var changes = new List<PokemonChange>();
        int checkedCount = 0;

        for (int i = 0; i < sav.PartyCount; i++)
        {
            var pk = sav.GetPartySlotAtIndex(i);
            if (pk.Species == 0)
                continue;
            checkedCount++;
            if (UpdatePokemon(pk, rom, new SaveSlot(null, i), changes))
                sav.SetPartySlotAtIndex(pk, i, EntityImportSettings.None);
        }

        for (int box = 0; box < sav.BoxCount; box++)
        {
            for (int slot = 0; slot < sav.BoxSlotCount; slot++)
            {
                var pk = sav.GetBoxSlotAtIndex(box, slot);
                if (pk.Species == 0)
                    continue;
                checkedCount++;
                if (UpdatePokemon(pk, rom, new SaveSlot(box, slot), changes))
                    sav.SetBoxSlotAtIndex(pk, box, slot, EntityImportSettings.None);
            }
        }

        if (!write || changes.Count == 0)
            return new SaveUpdateResult(savePath, null, checkedCount, changes);

        byte[] updated = sav.Write().ToArray();
        Verify(updated, rom);
        string backup = SaveWriter.Write(savePath, updated, backupRoot!);
        return new SaveUpdateResult(savePath, backup, checkedCount, changes);
    }

    private static bool UpdatePokemon(PKM pk, GameData rom, SaveSlot slot, List<PokemonChange> changes)
    {
        var personal = Personal(rom, pk);
        if (personal is null)
            return false;

        bool changed = false;
        int newAbility = personal.Abilities[AbilityIndex(pk.AbilityNumber)];
        if (newAbility != 0 && pk.Ability != newAbility)
        {
            changes.Add(new PokemonChange(slot, pk.Species, ChangeKind.Ability, [pk.Ability], [newAbility]));
            pk.Ability = newAbility;
            changed = true;
        }

        if (slot.IsParty)
        {
            int[] stats = CalculateStats(pk, personal);
            int[] old = [pk.Stat_HPMax, pk.Stat_ATK, pk.Stat_DEF, pk.Stat_SPE, pk.Stat_SPA, pk.Stat_SPD];
            if (!stats.AsSpan().SequenceEqual(old))
            {
                bool fullHp = pk.Stat_HPCurrent >= pk.Stat_HPMax;
                int hp = fullHp ? stats[0] : Math.Clamp(pk.Stat_HPCurrent, pk.Stat_HPCurrent > 0 ? 1 : 0, stats[0]);
                changes.Add(new PokemonChange(slot, pk.Species, ChangeKind.Stats, old, stats));
                (pk.Stat_HPMax, pk.Stat_ATK, pk.Stat_DEF, pk.Stat_SPE, pk.Stat_SPA, pk.Stat_SPD) = (stats[0], stats[1], stats[2], stats[3], stats[4], stats[5]);
                pk.Stat_HPCurrent = hp;
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>Personal entry of the Pokémon's species and form in the ROM.</summary>
    internal static pk3DS.Core.Structures.PersonalInfo.PersonalInfoXY? Personal(GameData rom, PKM pk)
    {
        if (pk.Species >= rom.Personal.Length)
            return null;
        int index = rom.Personal[pk.Species].FormeIndex(pk.Species, pk.Form);
        return index < rom.Personal.Length ? rom.Personal[index] : null;
    }

    /// <summary>Deletes the backups in <paramref name="backupRoot"/> except the <paramref name="keep"/> most recent.</summary>
    public static void PruneBackups(string backupRoot, int keep)
    {
        if (!Directory.Exists(backupRoot))
            return;
        foreach (string dir in Directory.GetDirectories(backupRoot).OrderByDescending(Path.GetFileName, StringComparer.Ordinal).Skip(keep))
        {
            try { Directory.Delete(dir, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>PKHeX AbilityNumber: 1 = first, 2 = second, 4 = hidden.</summary>
    internal static int AbilityIndex(int abilityNumber) => abilityNumber switch { 2 => 1, 4 => 2, _ => 0 };

    /// <summary>
    /// Generation 6–7 formula. HP = ⌊(2B + IV + ⌊EV/4⌋)·L/100⌋ + L + 10;
    /// others = ⌊(⌊(2B + IV + ⌊EV/4⌋)·L/100⌋ + 5)·nature⌋. Order: HP, Atk, Def, Spe, SpA, SpD. In Generation 7 a
    /// Hyper Trained stat counts as IV 31.
    /// </summary>
    internal static int[] CalculateStats(PKM pk, pk3DS.Core.Structures.PersonalInfo.PersonalInfoXY personal)
    {
        // The level comes from EXP with the ROM's growth rate, which the randomizer may have changed.
        int level = Experience.GetLevel(pk.EXP, (byte)personal.EXPGrowth);
        int[] baseStats = [personal.HP, personal.ATK, personal.DEF, personal.SPE, personal.SPA, personal.SPD];
        int[] ivs = [pk.IV_HP, pk.IV_ATK, pk.IV_DEF, pk.IV_SPE, pk.IV_SPA, pk.IV_SPD];
        if (pk is IHyperTrain ht)
        {
            bool[] trained = [ht.HT_HP, ht.HT_ATK, ht.HT_DEF, ht.HT_SPE, ht.HT_SPA, ht.HT_SPD];
            for (int i = 0; i < 6; i++)
                if (trained[i])
                    ivs[i] = 31;
        }
        int[] evs = [pk.EV_HP, pk.EV_ATK, pk.EV_DEF, pk.EV_SPE, pk.EV_SPA, pk.EV_SPD];

        var stats = new int[6];
        // Shedinja (292) always has 1 HP.
        stats[0] = pk.Species == 292 ? 1 : ((2 * baseStats[0] + ivs[0] + evs[0] / 4) * level / 100) + level + 10;

        // Nature n raises stat (n / 5) and lowers stat (n % 5), in the order Atk, Def, Spe, SpA, SpD.
        int nature = (int)pk.Nature;
        int up = nature / 5, down = nature % 5;
        for (int s = 1; s < 6; s++)
        {
            int value = ((2 * baseStats[s] + ivs[s] + evs[s] / 4) * level / 100) + 5;
            if (up != down)
            {
                if (s - 1 == up) value = value * 110 / 100;
                else if (s - 1 == down) value = value * 90 / 100;
            }
            stats[s] = value;
        }
        return stats;
    }

    /// <summary>Re-reads the new bytes: they must be a valid save with the expected abilities.</summary>
    private static void Verify(byte[] updated, GameData rom)
    {
        var reread = Parse(updated) ?? throw new SaveUpdateException(Strings.Save_CannotReread);

        foreach (var pk in reread.PartyData.Concat(reread.BoxData).Where(p => p.Species != 0))
        {
            if (Personal(rom, pk) is { } personal
                && personal.Abilities[AbilityIndex(pk.AbilityNumber)] is var expected and not 0
                && pk.Ability != expected)
                throw new SaveUpdateException(string.Format(Strings.Save_VerifyAbility, pk.Species, expected));
        }
    }
}
