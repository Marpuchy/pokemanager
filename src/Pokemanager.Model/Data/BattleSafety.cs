using Pokemanager.Model.Dump;

namespace Pokemanager.Model.Data;

/// <summary>
/// What a battle cannot start with, taken out of the built ROM whatever put it there (2026-11-04, user: Hala's last
/// Pokémon, a Staraptor, never came out and the battle hung).
/// </summary>
/// <remarks>
/// <para>
/// <b>Measured with the USUM Expansion Mod and UPR ZX</b>: the mod puts the forms of every species after all of them in an
/// order of its own (Mega Staraptor is row 1140, a mega the game never had), so UPR ZX, which tells megas apart by the
/// game's own rows, takes them for ordinary alternate forms. With a preset that allows them it put <b>27 mega forms
/// straight into trainer teams</b> (Mega Abomasnow, Mega Banette, Mega Absol…) and 24 into the fixed encounters and
/// totems — Mega Staraptor among them. A Pokémon that is a mega before mega evolving is not something the game ever
/// sends out.
/// </para>
/// <para>
/// <b>Measured on Ultra Sun as it ships</b>: no trainer Pokémon is in a mega or battle-only form, and of 653 trainers 69
/// carry one Z-crystal, 5 one Mega Stone and <b>none both, nor two of either</b>. The difficulty profiles and the
/// mega card of the trainers tab give the last Pokémon its stone without looking at the rest of the team, so a boss
/// with a Z-crystal could end up with both. Teams are brought back inside what the game itself does.
/// </para>
/// </remarks>
public static class BattleSafety
{
    /// <summary>Forms only reached during a battle that the mega table does not list (primal, weather, school…).</summary>
    private static readonly HashSet<(int Species, int Form)> BattleOnlyForms =
    [
        (382, 1), (383, 1),                 // primal Kyogre and Groudon
        (351, 1), (351, 2), (351, 3),       // Castform's weathers
        (421, 1),                           // Cherrim in the sun
        (555, 1), (555, 3),                 // Darmanitan's Zen Mode (and the Galarian one, with the mod)
        (648, 1),                           // Meloetta Pirouette
        (681, 1),                           // Aegislash Blade
        (658, 2),                           // Ash-Greninja
        (718, 4),                           // Zygarde Complete
        (746, 1),                           // Wishiwashi School
        (778, 1), (778, 3),                 // Mimikyu busted
        (800, 3),                           // Ultra Necrozma
        (845, 1), (845, 2),                 // Cramorant with prey (mod)
        (875, 1),                           // Eiscue Noice (mod)
        (877, 1),                           // Morpeko Hangry (mod)
        (890, 1),                           // Eternamax (mod)
        (964, 1),                           // Palafin Hero (mod)
        (1024, 1), (1024, 2),               // Terapagos Terastal and Stellar (mod)
    ];

    /// <summary>Minior's core forms (7-13): the shell breaks in battle.</summary>
    private const int Minior = 774;

    /// <summary>The held Z-crystals (the pieces a Pokémon holds; the bag keeps the beads, 31 above).</summary>
    public static bool IsZCrystal(int item) => item is >= 776 and <= 806;

    /// <summary>A form a Pokémon only takes inside a battle, which nothing should start a battle in.</summary>
    public static bool IsBattleOnly(GameData data, int species, int form) =>
        form != 0 && (data.MegaForms.Contains((species, form)) || BattleOnlyForms.Contains((species, form))
                      || (species == Minior && form >= 7));

    /// <summary>
    /// Every trainer of the two archives, in place: battle-only forms back to the Pokémon's own form, and at most one
    /// special item a team — one Mega Stone or one Z-crystal, the one held furthest back in the team (where the game puts
    /// its ace) wins and the others hold nothing.
    /// </summary>
    /// <returns>How many Pokémon changed.</returns>
    public static int FixTrainers(GameData data, GameTitle title, byte[][] dataFiles, byte[][] teamFiles)
    {
        var stones = data.MegaStones.ToHashSet();
        int changed = 0;
        for (int id = 0; id < Math.Min(dataFiles.Length, teamFiles.Length); id++)
        {
            var trainer = Trainer.Read(title, dataFiles[id], teamFiles[id]);
            if (trainer.Unreadable)
                continue;
            var team = trainer.Team.ToList();
            int touched = 0;
            foreach (var mon in team)
            {
                if (IsBattleOnly(data, mon.Species, mon.Form))
                {
                    mon.Form = 0;
                    touched++;
                }
            }

            bool Special(TrainerMon m) => stones.Contains(m.Item) || IsZCrystal(m.Item);
            if (team.FindLastIndex(Special) is var keep and >= 0)
            {
                for (int slot = 0; slot < team.Count; slot++)
                {
                    if (slot != keep && Special(team[slot]))
                    {
                        team[slot].Item = 0;
                        touched++;
                    }
                }
            }

            if (touched == 0)
                continue;
            changed += touched;
            dataFiles[id] = trainer.WriteData().ToArray();
            teamFiles[id] = trainer.WriteTeam().ToArray();
        }
        return changed;
    }

    /// <summary>Gift entries of the Generation 7 static archive (file 0) and encounters, totems and allies (file 1).</summary>
    private const int GiftSize = 0x14, EncounterSize = 0x38, SpeciesOffset = 0, FormOffset = 2;

    /// <summary>Battle-only forms of the fixed Pokémon of a Generation 7 static archive, in place.</summary>
    /// <returns>How many entries changed.</returns>
    public static int FixStatics7(GameData data, byte[][] files)
    {
        int changed = 0;
        for (int file = 0; file < Math.Min(2, files.Length); file++)
        {
            int size = file == 0 ? GiftSize : EncounterSize;
            byte[] entries = files[file];
            for (int at = 0; at + size <= entries.Length; at += size)
            {
                int species = BitConverter.ToUInt16(entries, at + SpeciesOffset);
                if (species == 0 || !IsBattleOnly(data, species, entries[at + FormOffset]))
                    continue;
                entries[at + FormOffset] = 0;
                changed++;
            }
        }
        return changed;
    }
}
