using Pokemanager.Model.Data;

namespace Pokemanager.Model.Projects;

/// <summary>
/// Who mega evolves and who carries a Z-crystal, by kind of trainer and from what level (2026-11-09, user: "options for
/// trainers X with a mega from level X and a crystal from level X; I want them to mega evolve from 35"). The level is
/// the trainer's last Pokémon's. Applied at build by <see cref="BattleSafety"/>, last, over the randomization, the
/// difficulty profile and the hand edits: below the level, or with the switch off, the trainer carries neither; at or
/// above it, it always does — a team where nobody can mega evolve gets one that can as its last Pokémon, and a team
/// without a crystal gets one on the Pokémon before the last.
/// </summary>
public sealed class TrainerBattleItems
{
    public TrainerItemRule Boss { get; set; } = new() { Mega = true, MegaFromLevel = 35, Z = true, ZFromLevel = 1 };
    public TrainerItemRule Important { get; set; } = new() { Mega = true, MegaFromLevel = 35, Z = true, ZFromLevel = 1 };
    public TrainerItemRule Regular { get; set; } = new();

    public TrainerItemRule For(TrainerDifficulty kind) => kind switch
    {
        TrainerDifficulty.Boss => Boss,
        TrainerDifficulty.Important => Important,
        _ => Regular,
    };

    public TrainerBattleItems Clone() => new() { Boss = Boss.Clone(), Important = Important.Clone(), Regular = Regular.Clone() };
}

/// <summary>One kind of trainer's rule: mega and Z-crystal, each a switch and a level.</summary>
public sealed class TrainerItemRule
{
    public bool Mega { get; set; }
    public int MegaFromLevel { get; set; } = 35;
    public bool Z { get; set; }
    public int ZFromLevel { get; set; } = 1;

    public bool MegaAt(int level) => Mega && level >= MegaFromLevel;
    public bool ZAt(int level) => Z && level >= ZFromLevel;

    public TrainerItemRule Clone() => new() { Mega = Mega, MegaFromLevel = MegaFromLevel, Z = Z, ZFromLevel = ZFromLevel };
}
