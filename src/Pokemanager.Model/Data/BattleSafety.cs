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

    /// <summary>
    /// Totem forms (2026-11-04, user): the game only uses them in its totem battles, never in a trainer's team, and
    /// UPR ZX puts them there as ordinary forms — Hala's team in the user's Ultra Moon carried a Totem Lurantis in the
    /// battle that hung. In trainer teams they go back to the Pokémon's own form; the totem battles keep theirs.
    /// </summary>
    private static readonly HashSet<(int Species, int Form)> TotemForms =
    [
        (20, 2), (105, 2), (735, 1), (738, 1), (743, 1), (752, 1), (754, 1), (758, 1), (777, 1), (778, 2), (778, 3), (784, 1),
    ];

    /// <summary>A totem form, which only a totem battle should have.</summary>
    public static bool IsTotem(int species, int form) => form != 0 && TotemForms.Contains((species, form));

    /// <summary>Minior's core forms (7-13): the shell breaks in battle.</summary>
    private const int Minior = 774;

    /// <summary>The held Z-crystals (the pieces a Pokémon holds; the bag keeps the beads, 31 above).</summary>
    public static bool IsZCrystal(int item) => item is >= 776 and <= 806;

    /// <summary>A form a Pokémon only takes inside a battle, which nothing should start a battle in.</summary>
    public static bool IsBattleOnly(GameData data, int species, int form) =>
        form != 0 && (data.MegaForms.Contains((species, form)) || BattleOnlyForms.Contains((species, form))
                      || (species == Minior && form >= 7));

    /// <summary>The held Z-crystal of a type in game order (the crystals 776-793 follow the Pokédex's order of types).</summary>
    private static readonly int[] ZCrystalByType =
        // Normal Fighting Flying Poison Ground Rock Bug Ghost Steel Fire Water Grass Electric Psychic Ice Dragon Dark Fairy
        [776, 782, 785, 783, 784, 788, 787, 789, 792, 777, 778, 780, 779, 786, 781, 790, 791, 793];

    /// <summary>
    /// Every trainer of the two archives, in place, the way the user asked for it (2026-11-04): a team carries at most
    /// one Z-crystal and one Mega Stone; <b>the mega is the last Pokémon</b>, sent out as itself and mega evolving in
    /// battle with its own stone; <b>the Z-crystal is never on the last one</b>. In detail:
    /// <list type="bullet">
    /// <item>A Pokémon in a battle-only form goes back to its own form; one that was a mega keeps its mega as its stone.</item>
    /// <item>Of the Pokémon holding their own stone, the one furthest back stays the mega and is moved to the last slot;
    /// any other Mega Stone is taken away.</item>
    /// <item>Of the Z-crystals, the one furthest back stays; if it is on the last Pokémon it moves to the one before (a
    /// team of one keeps none). The crystal is the type of the strongest damaging move of whoever holds it, so it can
    /// be used.</item>
    /// </list>
    /// </summary>
    /// <returns>How many things changed.</returns>
    /// <param name="handEdited">
    /// Trainers whose team the player changed by hand (species, form or held item): they keep their items as set — only
    /// the battle-only forms are put back (2026-11-10, user: "a hand edit wins").
    /// </param>
    public static int FixTrainers(GameData data, GameTitle title, byte[][] dataFiles, byte[][] teamFiles,
        Projects.TrainerBattleItems? rules = null, Func<int, bool>? handEdited = null)
    {
        rules ??= new Projects.TrainerBattleItems();
        var stones = data.MegaStones.ToHashSet();
        var ownStones = data.MegaStoneByForm.Where(p => p.Value != 0)
            .GroupBy(p => p.Key.Species).ToDictionary(g => g.Key, g => g.Select(p => p.Value).ToHashSet());
        int changed = 0;
        var starters = StarterFamilies(data, title, dataFiles, teamFiles);
        for (int id = 0; id < Math.Min(dataFiles.Length, teamFiles.Length); id++)
        {
            var trainer = Trainer.Read(title, dataFiles[id], teamFiles[id]);
            if (trainer.Unreadable || trainer.Count == 0)
                continue;
            int touched = FixTeam(data, trainer, stones, ownStones, rules.For(TrainerRoles.DifficultyOf(title, id)), id,
                handEdited?.Invoke(id) == true, starters.TryGetValue(id, out int family) ? family : null);
            if (touched == 0)
                continue;
            changed += touched;
            dataFiles[id] = trainer.WriteData().ToArray();
            teamFiles[id] = trainer.WriteTeam().ToArray();
        }
        return changed;
    }

    /// <summary>
    /// The first stage of each species, from the evolution table: species → the species its line starts with.
    /// </summary>
    internal static Dictionary<int, int> FirstStages(GameData data)
    {
        var parent = new Dictionary<int, int>();
        for (int row = 1; row < Math.Min(data.SpeciesCount, data.Evolutions.Length); row++)
            foreach (var (_, evolution) in data.Evolutions[row].InUse())
                if (evolution.Species > 0 && evolution.Species != row)
                    parent.TryAdd(evolution.Species, row);
        var first = new Dictionary<int, int>();
        for (int species = 1; species < data.SpeciesCount; species++)
        {
            int at = species;
            for (int guard = 0; guard < 8 && parent.TryGetValue(at, out int up); guard++)
                at = up;
            first[species] = at;
        }
        return first;
    }

    /// <summary>
    /// The rivals' and friends' starter (2026-11-12, user: "for the rival, in every game, if the starter has a mega it is
    /// always the one that mega evolves in the fight"): trainer id → the first stage of the line that is in <b>every</b>
    /// battle of that character with that starter (the tags <c>RIVALn-k</c> / <c>FRIENDn-k</c>: same character and k,
    /// any n). That is the starter whatever the randomizer made of it, so no table of the ROM's starters is needed (in
    /// Generation 6 they live in a CRO module). A character with one battle, or no line in all of them, gets none.
    /// </summary>
    internal static Dictionary<int, int> StarterFamilies(GameData data, GameTitle title, byte[][] dataFiles, byte[][] teamFiles)
    {
        var first = FirstStages(data);
        var groups = new Dictionary<string, List<(int Id, HashSet<int> Lines)>>();
        foreach (var (id, tag) in TrainerRoles.Of(title))
        {
            var match = System.Text.RegularExpressions.Regex.Match(tag, @"^(RIVAL|FRIEND)\d+-(\d+)$");
            if (!match.Success || id >= dataFiles.Length || id >= teamFiles.Length)
                continue;
            var trainer = Trainer.Read(title, dataFiles[id], teamFiles[id]);
            if (trainer.Unreadable || trainer.Count == 0)
                continue;
            var lines = trainer.Team.Select(m => first.GetValueOrDefault(m.Species, m.Species)).ToHashSet();
            string key = match.Groups[1].Value + "-" + match.Groups[2].Value;
            if (!groups.TryGetValue(key, out var list))
                groups[key] = list = [];
            list.Add((id, lines));
        }
        var result = new Dictionary<int, int>();
        foreach (var battles in groups.Values.Where(b => b.Count >= 2))
        {
            var common = battles.Skip(1).Aggregate(new HashSet<int>(battles[0].Lines), (all, b) => { all.IntersectWith(b.Lines); return all; });
            if (common.Count != 1)
                continue; // none, or two lines in every battle: not telling which is the starter
            foreach (var (id, _) in battles)
                result[id] = common.First();
        }
        return result;
    }

    private static int FixTeam(GameData data, Trainer trainer, HashSet<int> stones, Dictionary<int, HashSet<int>> ownStones,
        Projects.TrainerItemRule rule, int id, bool byHand = false, int? starterLine = null)
    {
        int touched = 0;
        List<TrainerMon> Team() => trainer.Team.ToList();
        // Who mega evolves and who carries a crystal: the rule of this kind of trainer at its last Pokémon's level
        // (2026-11-09, user: a random kid's Camerupt mega evolved; bosses always; megas from level 35).
        int level = trainer.Team.Last().Level;
        bool mega = rule.MegaAt(level);
        bool zCrystal = rule.ZAt(level) && trainer.Count >= 2;

        // 1. Out of battle-only and totem forms; a mega form becomes its Pokémon, holding its stone when it may mega evolve.
        foreach (var mon in Team())
        {
            if (!IsBattleOnly(data, mon.Species, mon.Form) && !IsTotem(mon.Species, mon.Form))
                continue;
            if (!byHand && mega && data.MegaStoneByForm.TryGetValue((mon.Species, mon.Form), out int stone) && stone != 0)
                mon.Item = stone;
            mon.Form = 0;
            touched++;
        }
        if (byHand)
            return touched;

        // 2. One mega, the last Pokémon, always there when the rule says so: one of the team that can mega evolve gets
        // its stone, or else the last Pokémon becomes one that can. Otherwise no Mega Stone at all.
        bool OwnStone(TrainerMon m) => ownStones.TryGetValue(m.Species, out var own) && own.Contains(m.Item);
        var team = Team();
        int megaSlot = mega ? team.FindLastIndex(OwnStone) : -1;
        // A rival's or a friend's starter that can mega evolve is the mega, over anyone else holding a stone.
        if (mega && starterLine is { } line)
        {
            var first = FirstStages(data);
            int starter = team.FindIndex(m => first.GetValueOrDefault(m.Species, m.Species) == line && ownStones.ContainsKey(m.Species));
            if (starter >= 0)
            {
                if (!OwnStone(team[starter]))
                {
                    team[starter].Item = ownStones[team[starter].Species].Min();
                    touched++;
                }
                megaSlot = starter;
            }
        }
        if (mega && megaSlot < 0)
        {
            megaSlot = team.FindLastIndex(m => ownStones.ContainsKey(m.Species) && !Legendaries.Contains(m.Species));
            if (megaSlot >= 0)
                team[megaSlot].Item = ownStones[team[megaSlot].Species].Min();
            else if (MegaReplacement(data, team, ownStones, id) is { } species)
            {
                megaSlot = team.Count - 1;
                Become(data, trainer, team[megaSlot], species, ownStones[species].Min());
            }
            if (megaSlot >= 0)
                touched++;
        }
        for (int slot = 0; slot < team.Count; slot++)
        {
            if (slot != megaSlot && stones.Contains(team[slot].Item))
            {
                team[slot].Item = 0;
                touched++;
            }
        }
        int last = team.Count - 1;
        if (megaSlot >= 0 && megaSlot != last)
        {
            trainer.Swap(megaSlot, last);
            touched++;
        }

        // 3. One Z-crystal when the rule says so, never on the last Pokémon, and one its holder can use: a type crystal
        // is the type of the holder's strongest damaging move of its own type, or of any type; a holder with no damaging
        // move hands it to one before it that has one. A team without one gets it on the Pokémon before the last.
        team = Team();
        int z = zCrystal ? team.FindLastIndex(m => IsZCrystal(m.Item)) : -1;
        for (int slot = 0; slot < team.Count; slot++)
        {
            if (slot != z && IsZCrystal(team[slot].Item))
            {
                team[slot].Item = 0;
                touched++;
            }
        }
        if (zCrystal)
        {
            int crystal = z >= 0 ? team[z].Item : 0;
            bool typeCrystal = crystal is 0 or (>= 776 and <= 793);
            // Looking back from where it was first, then forward; never the last Pokémon.
            int start = Math.Min(z < 0 ? last - 1 : z, last - 1);
            int holder = -1;
            foreach (int slot in Enumerable.Range(0, start + 1).Reverse().Concat(Enumerable.Range(start + 1, Math.Max(0, last - 1 - start))))
                if (!typeCrystal || UsableCrystal(data, team[slot]) is not null)
                {
                    holder = slot;
                    break;
                }
            int newCrystal = holder < 0 ? 0 : typeCrystal ? UsableCrystal(data, team[holder])!.Value : crystal;
            if (holder != z || newCrystal != crystal)
            {
                if (z >= 0)
                    team[z].Item = 0;
                if (holder >= 0)
                    team[holder].Item = newCrystal;
                touched++;
            }
        }

        // 4. A team that keeps either has the record say it carries items (Generation 6 ignores them otherwise).
        team = Team();
        if (touched > 0 && team.Any(m => stones.Contains(m.Item) || IsZCrystal(m.Item)))
        {
            trainer.HeldItems = true;
            if (data.Title.Generation() == 7)
                trainer.Flag = true;
        }
        return touched;
    }

    /// <summary>
    /// The Z-crystal for the Pokémon's strongest damaging move of one of its own types, or of its strongest damaging
    /// move when none is; null when it has no damaging move.
    /// </summary>
    private static int? UsableCrystal(GameData data, TrainerMon mon)
    {
        int row = PersonalIndex.Of(data).Row(mon.Species, mon.Form);
        var own = row >= 0 && row < data.Personal.Length ? data.Personal[row].Types : [];
        int best = -1, type = -1;
        bool bestOwn = false;
        for (int i = 0; i < 4; i++)
        {
            int move = mon.GetMove(i);
            if (move <= 0 || move >= data.Moves.Length)
                continue;
            var m = data.Moves[move];
            if (m.Category == 0)
                continue;
            bool isOwn = own.Contains(m.Type);
            if ((isOwn, m.Power).CompareTo((bestOwn, best)) <= 0)
                continue;
            best = m.Power;
            bestOwn = isOwn;
            type = m.Type;
        }
        return (uint)type < ZCrystalByType.Length ? ZCrystalByType[type] : null;
    }

    /// <summary>
    /// A Pokémon that can mega evolve to put in a boss's last slot when none of its team can: not a legendary, not in
    /// the team already, and as strong as the one it replaces (the closest base stat totals; the trainer's number picks
    /// among the five closest, so not every boss gets the same one).
    /// </summary>
    private static int? MegaReplacement(GameData data, List<TrainerMon> team, Dictionary<int, HashSet<int>> ownStones, int id)
    {
        var last = team[^1];
        var index = PersonalIndex.Of(data);
        int Bst(int species, int form)
        {
            int row = index.Row(species, form);
            return row >= 0 && row < data.Personal.Length ? data.Personal[row].BST : 0;
        }
        int target = Bst(last.Species, last.Form);
        var candidates = ownStones.Keys
            .Where(s => s > 0 && s < data.Personal.Length && s <= data.LastSpecies && !Legendaries.Contains(s)
                        && team.All(m => m.Species != s))
            .OrderBy(s => Math.Abs(Bst(s, 0) - target)).ThenBy(s => s)
            .Take(5).ToList();
        return candidates.Count == 0 ? null : candidates[id % candidates.Count];
    }

    /// <summary>Turns a team member into another Pokémon at the same level, holding its stone, with its own moves.</summary>
    private static void Become(GameData data, Trainer trainer, TrainerMon mon, int species, int stone)
    {
        mon.Species = species;
        mon.Form = 0;
        mon.Item = stone;
        mon.Ability = 0;
        mon.Gender = 0;
        if (data.Title.Generation() == 7 || trainer.CustomMoves)
        {
            int[] moves = species < data.Learnsets.Length ? data.Learnsets[species].GetEncounterMoves(mon.Level) : [];
            for (int i = 0; i < 4; i++)
                mon.SetMove(i, i < moves.Length ? moves[i] : 0);
        }
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
                int form = entries[at + FormOffset];
                // A gift is never a totem; the encounters file holds the real totem battles, which keep theirs.
                if (species == 0 || !(IsBattleOnly(data, species, form) || (file == 0 && IsTotem(species, form))))
                    continue;
                entries[at + FormOffset] = 0;
                changed++;
            }
        }
        return changed;
    }
}
