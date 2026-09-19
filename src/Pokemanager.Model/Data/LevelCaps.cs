using pk3DS.Core.CTR;
using Pokemanager.Model.Dump;
using Pokemanager.Model.Editing;
using Pokemanager.Model.Edits;

namespace Pokemanager.Model.Data;

/// <summary>What unlocks the next level cap: a gym badge, a Z-crystal, or entering the Hall of Fame.</summary>
public enum LevelCapMilestone
{
    /// <summary>Gym badge <see cref="LevelCapStep.Value"/> (0-7), Generation 6.</summary>
    Badge,

    /// <summary>The Z-crystal whose held piece is item <see cref="LevelCapStep.Value"/> (the bag keeps it as piece + 31).</summary>
    Crystal,

    /// <summary>The Hall of Fame: after it, no cap.</summary>
    League,
}

/// <summary>One step of the plan: the cap that holds until the player gets <see cref="Milestone"/>.</summary>
/// <param name="Level">The cap: the player's own level for this step, or <paramref name="Computed"/>.</param>
/// <param name="Computed">The level of the boss that gives the milestone, as the built ROM will have it.</param>
public sealed record LevelCapStep(LevelCapMilestone Milestone, int Value, int Level, int Computed);

/// <summary>
/// The level cap by milestones: until the player gets a badge (Generation 6) or a Z-crystal (Ultra Sun/Ultra Moon),
/// nothing can go above the level of the boss that gives it; after the last one, the league's level until the Hall of
/// Fame; then no cap. The levels are read from the project's own ROM data **with the level modifiers applied**, so they
/// follow a randomization and the "Levels (%)" options.
/// </summary>
/// <remarks>
/// Generation 6: the gym leaders are UPR ZX's <c>GYMn-LEADER</c> tags (<see cref="TrainerRoles"/>), the league its first
/// Elite Four and Champion. Ultra Sun/Ultra Moon: the story order of the twelve crystals and who gives each, checked on
/// the user's Ultra Moon — the levels of the totems (static encounters, UPR's totem indices) and of the kahunas (tags
/// <c>ELITE1-4</c> at their first battle) go up in that order, and each one is the game's own level × 1.2 under that
/// ROM's +20 %. Sun/Moon is not offered: its totems and kahunas have not been checked on a real ROM.
/// </remarks>
public static class LevelCaps
{
    /// <summary>A boss: a trainer, or a static encounter (a totem) of the static archive.</summary>
    private sealed record Boss(bool Totem, int[] Ids);

    /// <summary>Ultra Sun/Ultra Moon, in story order: the crystal (held piece id) and who gives it.</summary>
    private static readonly (int Crystal, Boss Boss)[] Usum =
    [
        (776, new Boss(true, [4, 9])),      // Normalium Z — Ilima's trial (the totem differs by version)
        (782, new Boss(false, [23])),       // Fightinium Z — Hala's grand trial
        (778, new Boss(true, [137])),       // Waterium Z — Lana's trial
        (777, new Boss(true, [249])),       // Firium Z — Kiawe's trial
        (780, new Boss(true, [24])),        // Grassium Z — Mallow's trial
        (788, new Boss(false, [90])),       // Rockium Z — Olivia's grand trial
        (779, new Boss(true, [146])),       // Electrium Z — Sophocles's trial
        (789, new Boss(true, [39])),        // Ghostium Z — Acerola's trial
        (791, new Boss(false, [154])),      // Darkinium Z — Nanu's grand trial
        (790, new Boss(true, [45])),        // Dragonium Z — Vast Poni Canyon
        (784, new Boss(false, [497])),      // Groundium Z — Hapu's grand trial
        (793, new Boss(true, [162])),       // Fairium Z — Mina's trial
    ];

    /// <summary>The league: the first Elite Four and the Champion (in Ultra Sun/Ultra Moon, the title match).</summary>
    private static int[] League(GameFamily family) => family switch
    {
        GameFamily.XY => [269, 271, 187, 270, 276],
        GameFamily.ORAS => [553, 554, 555, 556, 557],
        GameFamily.USUM => [149, 153, 156, 489, 494, 495, 496],
        _ => [],
    };

    /// <summary>Whether this game has a plan.</summary>
    public static bool Supported(GameTitle title) => title.Family() is GameFamily.XY or GameFamily.ORAS or GameFamily.USUM;

    /// <summary>The plan for the project's ROM, or null when this game has none.</summary>
    public static IReadOnlyList<LevelCapStep>? Plan(EditorSession session)
    {
        var title = session.Current.Title;
        if (!Supported(title))
            return null;
        var steps = new List<LevelCapStep>();
        if (title.Generation() == 6)
        {
            for (int badge = 0; badge < 8; badge++)
            {
                string tag = $"GYM{badge + 1}-LEADER";
                int[] leaders = [.. Enumerable.Range(0, session.Current.Trainers.Length).Where(i => TrainerRoles.TagOf(title, i) == tag)];
                steps.Add(Step(LevelCapMilestone.Badge, badge, FirstBattle(session, new Boss(false, leaders))));
            }
        }
        else
        {
            byte[]? statics = StaticEncounters(session);
            foreach (var (crystal, boss) in Usum)
                steps.Add(Step(LevelCapMilestone.Crystal, crystal, boss.Totem ? TotemLevel(session, statics, boss.Ids) : FirstBattle(session, boss)));
        }
        int league = League(title.Family()).Select(id => TrainerLevel(session, id)).DefaultIfEmpty(0).Max();
        steps.Add(Step(LevelCapMilestone.League, 0, league));

        var overrides = session.Project.Tweaks.LevelCapOverrides;
        return [.. steps.Select((s, i) => overrides.TryGetValue(i, out int level) ? s with { Level = Math.Clamp(level, 1, 100) } : s)];
    }

    private static LevelCapStep Step(LevelCapMilestone milestone, int value, int level) =>
        new(milestone, value, Math.Clamp(level, 1, 100), Math.Clamp(level, 1, 100));

    /// <summary>
    /// The step the player is on: the first whose milestone is not earned yet, or -1 when every one is (no cap).
    /// </summary>
    public static int Current(IReadOnlyList<LevelCapStep> plan, Func<LevelCapStep, bool> earned)
    {
        for (int i = 0; i < plan.Count; i++)
        {
            if (!earned(plan[i]))
                return i;
        }
        return -1;
    }

    /// <summary>A boss with several entries (one per starter, or a rematch): its first battle, the lowest of them.</summary>
    private static int FirstBattle(EditorSession session, Boss boss) =>
        boss.Ids.Select(id => TrainerLevel(session, id)).Where(l => l > 0).DefaultIfEmpty(0).Min();

    /// <summary>The highest level of a trainer's team as the build will write it (hand edits kept, the modifier applied).</summary>
    private static int TrainerLevel(EditorSession session, int id)
    {
        if (id >= session.Current.Trainers.Length || session.Current.Trainers[id].Unreadable)
            return 0;
        double percent = session.Project.Tweaks.TrainerLevelPercent;
        return session.Current.Trainers[id].Team
            .Select((mon, slot) => !session.Project.Edits.Contains(new EditKey(GameTables.Trainers, id, $"p{slot + 1}.level"))
                ? GameLevels.Scale(mon.Level, percent)
                : mon.Level)
            .DefaultIfEmpty(0).Max();
    }

    private static int TotemLevel(EditorSession session, byte[]? statics, int[] ids)
    {
        if (statics is null)
            return 0;
        return ids.Where(id => (id + 1) * GameLevels.EncounterSize <= statics.Length)
            .Select(id => GameLevels.Scale(statics[(id * GameLevels.EncounterSize) + 3], session.Project.Tweaks.StaticLevelPercent))
            .DefaultIfEmpty(0).Min();
    }

    /// <summary>File 1 of the static archive (imported with the game), or null when the game folder does not have it.</summary>
    private static byte[]? StaticEncounters(EditorSession session)
    {
        string path = session.Current.Title.Layout().Statics;
        try
        {
            var files = new GARC.MemGARC(File.ReadAllBytes(session.Layers.Resolve(path))).Files;
            return files.Length > 1 ? files[1] : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return null;
        }
    }
}
