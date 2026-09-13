using PKHeX.Core;
using GameData = Pokemanager.Model.Data.GameData;

namespace Pokemanager.Save;

/// <summary>Un cambio aplicado a un Pokémon de la partida.</summary>
/// <param name="Location">«Equipo 1», «Caja 3, hueco 12»…</param>
public sealed record PokemonChange(string Location, ushort Species, string Description);

public sealed record SaveUpdateResult(string SavePath, string? BackupPath, int PokemonChecked, IReadOnlyList<PokemonChange> Changes);

public sealed class SaveUpdateException(string message) : Exception(message);

/// <summary>
/// Adapta una partida de X/Y a otra versión de la ROM (por ejemplo, otra semilla del randomizer).
/// </summary>
/// <remarks>
/// En la 6.ª generación la partida guarda la habilidad de cada Pokémon y las stats del equipo; la ROM solo
/// aporta las stats base y las habilidades posibles de cada especie. Por eso, al cambiar la ROM, hay que:
/// <list type="bullet">
/// <item>asignar a cada Pokémon la habilidad de su mismo número (1, 2 u oculta) en la nueva especie;</item>
/// <item>recalcular las stats del equipo con las nuevas stats base (las de las cajas se calculan al sacarlos).</item>
/// </list>
/// Especie, nivel, IV, EV, naturaleza y movimientos aprendidos no se tocan.
/// </remarks>
public static class SaveUpdater
{
    /// <summary>Tamaño del archivo <c>main</c> de X/Y.</summary>
    public const int SizeXY = 0x65600;

    /// <summary>
    /// Carga la partida. Si la autodetección de PKHeX no la reconoce pero tiene el tamaño de X/Y (p. ej. una partida
    /// recién creada), se abre como X/Y; las sumas de control se comprueban igualmente antes de modificarla.
    /// </summary>
    public static SaveFile Load(string savePath)
    {
        byte[] data = File.ReadAllBytes(savePath);
        if (SaveUtil.GetSaveFile(data) is { } sav)
            return sav;
        if (data.Length == SizeXY)
            return new SAV6XY(data);
        throw new SaveUpdateException($"No se reconoce la partida: {savePath}");
    }

    /// <summary>Calcula los cambios sin escribir nada.</summary>
    public static SaveUpdateResult Preview(string savePath, GameData rom) => Run(savePath, rom, backupRoot: null, write: false);

    /// <summary>
    /// Aplica los cambios: copia de seguridad en <paramref name="backupRoot"/>, escritura y verificación releyendo el archivo.
    /// </summary>
    public static SaveUpdateResult Apply(string savePath, GameData rom, string backupRoot) => Run(savePath, rom, backupRoot, write: true);

    private static SaveUpdateResult Run(string savePath, GameData rom, string? backupRoot, bool write)
    {
        var sav = Load(savePath);
        if (sav is not SAV6XY)
            throw new SaveUpdateException($"La partida no es de Pokémon X/Y ({sav.GetType().Name}).");
        if (!sav.ChecksumsValid)
            throw new SaveUpdateException("Las sumas de control de la partida no son válidas; no se modifica.");

        var changes = new List<PokemonChange>();
        int checkedCount = 0;

        for (int i = 0; i < sav.PartyCount; i++)
        {
            var pk = sav.GetPartySlotAtIndex(i);
            if (pk.Species == 0)
                continue;
            checkedCount++;
            if (UpdatePokemon(pk, rom, isParty: true, $"Equipo {i + 1}", changes))
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
                if (UpdatePokemon(pk, rom, isParty: false, $"Caja {box + 1}, hueco {slot + 1}", changes))
                    sav.SetBoxSlotAtIndex(pk, box, slot, EntityImportSettings.None);
            }
        }

        if (!write || changes.Count == 0)
            return new SaveUpdateResult(savePath, null, checkedCount, changes);

        byte[] original = File.ReadAllBytes(savePath);
        byte[] updated = sav.Write().ToArray();
        Verify(updated, rom);

        string backupDir = Path.Combine(backupRoot!, DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(backupDir);
        string backup = Path.Combine(backupDir, Path.GetFileName(savePath));
        File.WriteAllBytes(backup, original);

        string tmp = savePath + ".pokemanager.tmp";
        File.WriteAllBytes(tmp, updated);
        File.Move(tmp, savePath, overwrite: true);

        // Verificación final sobre lo que ha quedado en disco.
        if (!File.ReadAllBytes(savePath).AsSpan().SequenceEqual(updated))
            throw new SaveUpdateException($"La partida escrita no coincide con la esperada. Copia de seguridad: {backup}");

        return new SaveUpdateResult(savePath, backup, checkedCount, changes);
    }

    private static bool UpdatePokemon(PKM pk, GameData rom, bool isParty, string location, List<PokemonChange> changes)
    {
        var personal = Personal(rom, pk);
        if (personal is null)
            return false;

        bool changed = false;
        int abilityIndex = AbilityIndex(pk.AbilityNumber);
        int newAbility = personal.Abilities[abilityIndex];
        if (newAbility != 0 && pk.Ability != newAbility)
        {
            changes.Add(new PokemonChange(location, pk.Species, $"habilidad {pk.Ability} → {newAbility}"));
            pk.Ability = newAbility;
            changed = true;
        }

        if (isParty)
        {
            int[] stats = CalculateStats(pk, personal);
            int[] old = [pk.Stat_HPMax, pk.Stat_ATK, pk.Stat_DEF, pk.Stat_SPE, pk.Stat_SPA, pk.Stat_SPD];
            if (!stats.AsSpan().SequenceEqual(old))
            {
                bool fullHp = pk.Stat_HPCurrent >= pk.Stat_HPMax;
                int hp = fullHp ? stats[0] : Math.Clamp(pk.Stat_HPCurrent, pk.Stat_HPCurrent > 0 ? 1 : 0, stats[0]);
                changes.Add(new PokemonChange(location, pk.Species,
                    $"stats {string.Join('/', old)} → {string.Join('/', stats)}"));
                (pk.Stat_HPMax, pk.Stat_ATK, pk.Stat_DEF, pk.Stat_SPE, pk.Stat_SPA, pk.Stat_SPD) = (stats[0], stats[1], stats[2], stats[3], stats[4], stats[5]);
                pk.Stat_HPCurrent = hp;
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>Entrada de personal de la especie y forma del Pokémon en la ROM.</summary>
    internal static pk3DS.Core.Structures.PersonalInfo.PersonalInfoXY? Personal(GameData rom, PKM pk)
    {
        if (pk.Species >= rom.Personal.Length)
            return null;
        int index = rom.Personal[pk.Species].FormeIndex(pk.Species, pk.Form);
        return index < rom.Personal.Length ? rom.Personal[index] : null;
    }

    /// <summary>AbilityNumber de PKHeX: 1 = primera, 2 = segunda, 4 = oculta.</summary>
    internal static int AbilityIndex(int abilityNumber) => abilityNumber switch { 2 => 1, 4 => 2, _ => 0 };

    /// <summary>
    /// Fórmula de la 6.ª generación. PS = ⌊(2B + IV + ⌊EV/4⌋)·N/100⌋ + N + 10;
    /// resto = ⌊(⌊(2B + IV + ⌊EV/4⌋)·N/100⌋ + 5)·naturaleza⌋. Orden: PS, Atq, Def, Vel, AtE, DfE.
    /// </summary>
    internal static int[] CalculateStats(PKM pk, pk3DS.Core.Structures.PersonalInfo.PersonalInfoXY personal)
    {
        int level = pk.CurrentLevel;
        int[] baseStats = [personal.HP, personal.ATK, personal.DEF, personal.SPE, personal.SPA, personal.SPD];
        int[] ivs = [pk.IV_HP, pk.IV_ATK, pk.IV_DEF, pk.IV_SPE, pk.IV_SPA, pk.IV_SPD];
        int[] evs = [pk.EV_HP, pk.EV_ATK, pk.EV_DEF, pk.EV_SPE, pk.EV_SPA, pk.EV_SPD];

        var stats = new int[6];
        // Shedinja (292) siempre tiene 1 PS.
        stats[0] = pk.Species == 292 ? 1 : ((2 * baseStats[0] + ivs[0] + evs[0] / 4) * level / 100) + level + 10;

        // Naturaleza: índice n → sube (n / 5) y baja (n % 5), en el orden Atq, Def, Vel, AtE, DfE.
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

    /// <summary>Relee los bytes nuevos: tienen que ser una partida válida con las habilidades esperadas.</summary>
    private static void Verify(byte[] updated, GameData rom)
    {
        var reread = (SaveUtil.GetSaveFile(updated) ?? (updated.Length == SizeXY ? new SAV6XY(updated) : null)) as SAV6XY
                     ?? throw new SaveUpdateException("La partida modificada no se puede releer.");
        if (!reread.ChecksumsValid)
            throw new SaveUpdateException("La partida modificada tiene sumas de control inválidas; no se escribe.");

        foreach (var pk in reread.PartyData.Concat(reread.BoxData).Where(p => p.Species != 0))
        {
            if (Personal(rom, pk) is { } personal
                && personal.Abilities[AbilityIndex(pk.AbilityNumber)] is var expected and not 0
                && pk.Ability != expected)
                throw new SaveUpdateException($"Verificación fallida: la especie {pk.Species} no quedó con la habilidad {expected}.");
        }
    }
}
