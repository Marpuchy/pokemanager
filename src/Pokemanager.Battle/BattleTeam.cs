using Pokemanager.Save;

namespace Pokemanager.Battle;

/// <summary>A Pokémon ready for battle, with game ids (the simulator names them).</summary>
/// <param name="Pp">Current and maximum PP of each move, as in the save.</param>
/// <param name="Ivs">HP, Atk, Def, Spe, SpA, SpD (game order), as <paramref name="Evs"/>.</param>
/// <param name="Hp">Current HP in the party.</param>
public sealed record BattleMon(
    ushort Species, byte Form, byte Gender, bool Shiny, string Nickname, int Level, int Item, int Ability, int Nature,
    ushort[] Moves, int[][] Pp, int[] Ivs, int[] Evs, int Hp, int Friendship);

/// <summary>Base stats (HP, Atk, Def, Spe, SpA, SpD) and types of a species form in a player's ROM.</summary>
public sealed record BattleSpecies(ushort Species, byte Form, int[] BaseStats, int[] Types);

/// <summary>A move as a player's ROM defines it. Accuracy 101 = never misses; category 0 status, 1 physical, 2 special.</summary>
public sealed record BattleMove(int Id, int Type, int Category, int Power, int Accuracy, int PP, int Priority);

/// <summary>
/// What a player brings to a battle: the party of their save and the data of their ROM for those Pokémon (every form, so
/// mega evolutions and form changes use the ROM too) and their moves.
/// </summary>
public sealed record BattleTeam(string Player, int Generation, List<BattleMon> Mons, List<BattleSpecies> Species, List<BattleMove> Moves)
{
    /// <summary>The party of a save (eggs left out) with the ROM data it needs.</summary>
    public static BattleTeam FromParty(SaveDocument save, string? player = null)
    {
        var mons = new List<BattleMon>();
        var species = new Dictionary<(ushort, byte), BattleSpecies>();
        var moves = new Dictionary<int, BattleMove>();
        for (int i = 0; i < save.PartyCount; i++)
        {
            var pk = save.Get(new SaveSlot(null, i));
            if (pk.Species == 0 || pk.IsEgg)
                continue;
            ushort[] moveIds = pk.Moves.ToArray();
            int[] ppUps = [pk.Move1_PPUps, pk.Move2_PPUps, pk.Move3_PPUps, pk.Move4_PPUps];
            int[] pp = [pk.Move1_PP, pk.Move2_PP, pk.Move3_PP, pk.Move4_PP];
            mons.Add(new BattleMon(
                pk.Species, pk.Form, pk.Gender, pk.IsShiny, pk.IsNicknamed ? pk.Nickname : "", save.Level(pk), pk.HeldItem, pk.Ability,
                (int)pk.Nature, moveIds, [.. moveIds.Select((m, s) => new[] { pp[s], save.MaxPP(m, ppUps[s]) })],
                [pk.IV_HP, pk.IV_ATK, pk.IV_DEF, pk.IV_SPE, pk.IV_SPA, pk.IV_SPD],
                [pk.EV_HP, pk.EV_ATK, pk.EV_DEF, pk.EV_SPE, pk.EV_SPA, pk.EV_SPD],
                pk.Stat_HPCurrent, pk.CurrentFriendship));

            for (byte form = 0; form < save.FormCount(pk.Species); form++)
            {
                if (save.Personal(pk.Species, form) is { } p)
                    species.TryAdd((pk.Species, form), new BattleSpecies(pk.Species, form, [p.HP, p.ATK, p.DEF, p.SPE, p.SPA, p.SPD], [.. p.Types]));
            }
            foreach (ushort id in moveIds.Where(m => m != 0 && m < save.Rom.Moves.Length))
            {
                var m = save.Rom.Moves[id];
                moves.TryAdd(id, new BattleMove(id, m.Type, m.Category, m.Power, m.Accuracy, m.PP, (sbyte)m.Priority));
            }
        }
        return new BattleTeam(player ?? save.TrainerName, save.Generation, mons, [.. species.Values], [.. moves.Values]);
    }
}

/// <summary>What the players agree on before a battle. Everything is optional.</summary>
/// <param name="Generation">Mechanics: 6 (X/Y, ORAS) or 7 (Sun/Moon, Ultra Sun/Ultra Moon).</param>
/// <param name="Level">0 = the levels of the saves; otherwise every Pokémon is set to this level.</param>
/// <param name="HealBefore">Start with full HP and PP (otherwise as they are in the saves).</param>
public sealed record BattleRules(
    int Generation = 7, int Level = 0, bool HealBefore = true, bool TeamPreview = true,
    bool SleepClause = true, bool SpeciesClause = false, bool ItemClause = false, bool OhkoClause = true,
    bool EvasionClause = true, bool BatonPassClause = false, bool NoMegas = false, bool NoZMoves = false);
