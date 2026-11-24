using Pokemanager.Model.Dump;

namespace Pokemanager.Model.Data;

/// <summary>
/// The randomizer's trainer options that a reroll of one team follows, read from the project's preset.
/// </summary>
/// <param name="SimilarStrength">Each new Pokémon close in base stats to the one it replaces.</param>
/// <param name="NoLegendaries">No legendaries.</param>
/// <param name="KeepTheme">A team whose Pokémon all share a type keeps that type (gym leaders, type-themed trainers).</param>
/// <param name="FullyEvolvedFrom">From this level on, only Pokémon that do not evolve; null when the option is off.</param>
/// <param name="NoEarlyWonderGuard">No Pokémon with Wonder Guard as an ability.</param>
public sealed record TrainerRerollRules(bool SimilarStrength, bool NoLegendaries, bool KeepTheme, int? FullyEvolvedFrom,
    bool NoEarlyWonderGuard);

/// <summary>
/// A whole trainer team drawn again (2026-11-13, user: "al azar" for trainers as for the wild Pokémon, with the team
/// still hidden, by the ROM's rules): every Pokémon another of this ROM by the preset's trainer rules, at the same level,
/// with the moves it knows at that level and a random held item of the randomizer's pool (no Mega Stone or Z-crystal) — the per-kind mega and crystal rules are then
/// applied by <see cref="BattleSafety.FixOne"/>, as the build does for the teams it does not leave alone.
/// </summary>
public static class TrainerReroll
{
    private const int WonderGuard = 25;

    /// <summary>
    /// The held items the randomizer gives trainers' Pokémon (UPR ZX's <c>Gen6Constants.allHeldItems</c>, read out of
    /// the bundled jar; no Mega Stones or Z-crystals, those are the per-kind rule's). Generation 7 adds the ones of
    /// <see cref="Gen7Extra"/>.
    /// </summary>
    private static readonly int[] HeldItems =
    [
        213, 217, 220, 221, 222, 230, 232, 233, 234, 237, 238, 239, 240, 241, 242, 243, 244, 245, 246, 247, 248, 249, 250,
        251, 253, 254, 255, 265, 266, 267, 268, 269, 270, 272, 273, 276, 277, 278, 279, 280, 281, 282, 283, 284, 285, 286,
        287, 288, 295, 296, 297, 298, 299, 300, 301, 302, 303, 304, 305, 306, 307, 308, 309, 310, 311, 312, 313, 314, 315,
        316, 317, 318, 326, 327, 149, 150, 151, 152, 153, 154, 155, 156, 157, 158, 159, 160, 161, 162, 163, 184, 185, 186,
        187, 188, 189, 190, 191, 192, 193, 194, 195, 196, 197, 198, 199, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209,
        210, 211, 212, 43, 214, 219, 271, 275, 541, 542, 545, 546, 547, 548, 549, 550, 551, 552, 553, 554, 555, 556, 557,
        558, 559, 560, 561, 562, 563, 564, 538, 539, 540, 543, 544, 639, 649, 686, 687, 688, 715, 640, 644, 650,
    ];

    /// <summary>UPR ZX's <c>Gen7Constants.allHeldItems</c> past Generation 6's (Adrenaline Orb, the seeds, Terrain Extender, Protective Pads).</summary>
    private static readonly int[] Gen7Extra = [846, 881, 882, 883, 884, 879, 880];

    /// <returns>How many Pokémon changed (0 when no species fits).</returns>
    public static int Reroll(GameData data, Trainer trainer, TrainerRerollRules rules, Random random, int lastSpecies)
    {
        var personal = data.Personal;
        int last = Math.Min(lastSpecies, personal.Length - 1);
        var team = trainer.Team.ToList();
        if (team.Count == 0)
            return 0;
        int Row(int species, int form) => PersonalIndex.Of(data).Row(species, form) is var r && r > 0 && r < personal.Length ? r : species;

        // The type every Pokémon of the team shares, when there is one.
        int? theme = null;
        if (rules.KeepTheme)
        {
            var shared = team.Select(m => personal[Row(m.Species, m.Form)].Types.ToHashSet())
                .Aggregate((a, b) => { a.IntersectWith(b); return a; });
            if (shared.Count > 0)
                theme = shared.First();
        }

        int[] items = data.Title.Generation() == 7 ? [.. HeldItems, .. Gen7Extra] : HeldItems;
        items = [.. items.Where(i => i < data.Items.Length)];
        var taken = new HashSet<int>();
        int changed = 0;
        foreach (var mon in team)
        {
            bool fullyEvolved = rules.FullyEvolvedFrom is { } from && mon.Level >= from;
            var pool = Enumerable.Range(1, Math.Max(0, last)).Where(s =>
                personal[s].BST > 0 && s != mon.Species && !taken.Contains(s)
                && !(rules.NoLegendaries && Legendaries.Contains(s))
                && !(theme is { } t && !personal[s].Types.Contains(t))
                && !(fullyEvolved && s < data.Evolutions.Length && data.Evolutions[s].InUse().Any())
                && !(rules.NoEarlyWonderGuard && personal[s].Abilities.Contains(WonderGuard))).ToList();
            if (rules.SimilarStrength)
            {
                int bst = personal[Row(mon.Species, mon.Form)].BST;
                for (double margin = 0.1; margin < 1; margin += 0.05)
                {
                    var near = pool.Where(s => Math.Abs(personal[s].BST - bst) <= bst * margin).ToList();
                    if (near.Count >= 3)
                    {
                        pool = near;
                        break;
                    }
                }
            }
            if (pool.Count == 0)
                continue;

            int species = pool[random.Next(pool.Count)];
            taken.Add(species);
            mon.Species = species;
            mon.Form = 0;
            mon.Ability = 0;
            int[] moves = species < data.Learnsets.Length ? data.Learnsets[species].GetEncounterMoves(mon.Level) : [];
            for (int i = 0; i < 4; i++)
                mon.SetMove(i, i < moves.Length ? moves[i] : 0);
            // Every held item drawn again too (2026-11-13, user: "not only the Z-crystal, all of them").
            mon.Item = items.Length == 0 ? 0 : items[random.Next(items.Length)];
            changed++;
        }
        // Generation 6 ignores what the entries hold unless the record says it carries items.
        if (changed > 0 && data.Title.Generation() == 6)
            trainer.HeldItems = true;
        return changed;
    }
}
