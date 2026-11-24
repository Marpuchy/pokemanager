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
/// with the moves it knows at that level and no Mega Stone or Z-crystal — the per-kind mega and crystal rules are then
/// applied by <see cref="BattleSafety.FixOne"/>, as the build does for the teams it does not leave alone.
/// </summary>
public static class TrainerReroll
{
    private const int WonderGuard = 25;

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

        var stones = data.MegaStones.ToHashSet();
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
            if (stones.Contains(mon.Item) || BattleSafety.IsZCrystal(mon.Item))
                mon.Item = 0;
            changed++;
        }
        return changed;
    }
}
