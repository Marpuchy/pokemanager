using Pokemanager.Model.Data;
using Pokemanager.Model.Dump;

namespace Pokemanager.Model.Projects;

public enum LockeMilestoneKind
{
    /// <summary>The game's own milestone <see cref="LockeMilestone.Value"/> (a Generation 6 badge).</summary>
    Badge,

    /// <summary>A Z-crystal of the story: <see cref="LockeMilestone.Value"/> is its held piece (the bag keeps piece + 31).</summary>
    Crystal,

    /// <summary>The Hall of Fame.</summary>
    League,
}

public sealed record LockeMilestone(LockeMilestoneKind Kind, int Value);

/// <summary>
/// What the roulette is spun after. Generation 6: the eight badges. Sun/Moon and Ultra Sun/Ultra Moon (2026-11-10,
/// user: "after every trial"): each of the twelve Z-crystals of the story — every trial and every grand trial —, then
/// the league; the five island stamps were too few.
/// </summary>
public static class LockeMilestones
{
    public static bool ByCrystals(GameTitle title) => title.Generation() == 7;

    public static IReadOnlyList<LockeMilestone> Of(GameTitle title) => ByCrystals(title)
        ? [.. LevelCaps.StoryCrystals.Select(c => new LockeMilestone(LockeMilestoneKind.Crystal, c)), new(LockeMilestoneKind.League, 0)]
        : [.. Enumerable.Range(0, title.Milestones().Count).Select(i => new LockeMilestone(LockeMilestoneKind.Badge, i))];

    /// <summary>
    /// The milestones whose roulette is the major one (2026-11-10, user: "ruletas mayores, las pruebas finales de la isla,
    /// y menores, las pruebas normales"): the four grand trials and the league. The other eight crystals are captains' trials.
    /// </summary>
    public static bool IsMajor(int index) => index is 1 or 5 or 8 or 10 or 12;

    /// <summary>Where an island stamp (0-4: four grand trials, then the challenge) falls among the crystals and the league.</summary>
    public static int FromStamp(int stamp) => stamp switch
    {
        0 => 1,   // Melemele: Hala, Fightinium Z
        1 => 5,   // Akala: Olivia, Rockium Z
        2 => 8,   // Ula'ula: Nanu, Darkinium Z
        3 => 10,  // Poni: Hapu, Groundium Z
        _ => 12,  // the island challenge: the league
    };
}
