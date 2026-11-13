using Pokemanager.Model.Dump;

namespace Pokemanager.Model.Data;

/// <summary>
/// Starters with two evolutions and a Mega Evolution in their line (2026-11-13, user: "with 2 evolutions and Mega
/// Evolution, counting the mod's megas"). The candidates come from the ROM's own data: a species nothing evolves into,
/// that evolves into one that evolves again, and whose line holds a species the ROM's mega table gives a stone to — so
/// a species-expansion mod's new megas count. Legendaries are left out. The three are drawn from the project's seed,
/// with different first types when there are enough.
/// </summary>
public static class MegaStarters
{
    /// <summary>The line of a species following the first evolution of each stage (the species itself first).</summary>
    public static List<int> Line(GameData data, int species)
    {
        var line = new List<int> { species };
        int current = species;
        for (int stage = 0; stage < 2; stage++)
        {
            if (current >= data.Evolutions.Length || data.Evolutions[current].InUse().Select(e => e.Evolution.Species)
                    .FirstOrDefault(s => s > 0 && s <= data.LastSpecies && !line.Contains(s)) is not (> 0 and var next))
                break;
            line.Add(next);
            current = next;
        }
        return line;
    }

    /// <summary>Every species that could be one: basic, two evolutions, a mega in the line, not legendary.</summary>
    public static List<int> Candidates(GameData data)
    {
        int last = Math.Min(data.LastSpecies, data.Evolutions.Length - 1);
        var evolvedInto = new HashSet<int>();
        for (int s = 1; s <= last; s++)
            foreach (var (_, e) in data.Evolutions[s].InUse())
                evolvedInto.Add(e.Species);
        return [.. Enumerable.Range(1, Math.Max(0, last)).Where(s =>
        {
            if (evolvedInto.Contains(s) || Legendaries.Contains(s))
                return false;
            var line = Line(data, s);
            return line.Count == 3 && line.Any(data.MegaStoneBySpecies.ContainsKey);
        })];
    }

    /// <summary>Three of them from the seed, different first types first; null when the ROM has fewer than three.</summary>
    public static int[]? Pick(GameData data, long seed)
    {
        var candidates = Candidates(data);
        if (candidates.Count < 3)
            return null;
        var random = new Random(unchecked((int)(seed ^ (seed >> 32)) ^ 0x4D454741));
        var shuffled = candidates.OrderBy(_ => random.Next()).ToList();
        var picked = new List<int>();
        var types = new HashSet<int>();
        foreach (int s in shuffled)
            if (picked.Count < 3 && types.Add(data.Personal[s].Types[0]))
                picked.Add(s);
        foreach (int s in shuffled)
            if (picked.Count < 3 && !picked.Contains(s))
                picked.Add(s);
        return [.. picked];
    }

    /// <summary>
    /// The rival and the friend carry the starter that beats the player's: a member of an old starter's line becomes the
    /// new starter's at the same stage, so their teams follow. Returns how many Pokémon changed.
    /// </summary>
    public static int FollowInTeams(GameData data, GameTitle title, int[] before, int[] after, byte[][] dataFiles, byte[][] teamFiles)
    {
        var map = new Dictionary<int, int>();
        for (int i = 0; i < 3; i++)
        {
            var from = Line(data, before[i]);
            var to = Line(data, after[i]);
            for (int stage = 0; stage < from.Count; stage++)
                map.TryAdd(from[stage], to[Math.Min(stage, to.Count - 1)]);
        }
        int changed = 0;
        for (int id = 0; id < Math.Min(dataFiles.Length, teamFiles.Length); id++)
        {
            if (TrainerRoles.TagOf(title, id) is not { } tag || !(tag.StartsWith("RIVAL", StringComparison.Ordinal) || tag.StartsWith("FRIEND", StringComparison.Ordinal)))
                continue;
            var trainer = Trainer.Read(title, dataFiles[id], teamFiles[id]);
            if (trainer.Unreadable)
                continue;
            bool touched = false;
            foreach (var mon in trainer.Team)
            {
                if (!map.TryGetValue(mon.Species, out int species) || species == mon.Species)
                    continue;
                mon.Species = species;
                mon.Form = 0;
                touched = true;
                changed++;
            }
            if (touched)
                teamFiles[id] = trainer.WriteTeam().ToArray();
        }
        return changed;
    }
}
