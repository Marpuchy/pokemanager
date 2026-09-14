using System.Text.Json.Serialization;

namespace Pokemanager.Multiplayer;

/// <summary>How the captures of different players are tied together.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<LinkMatching>))]
public enum LinkMatching
{
    /// <summary>Captures are not linked: only lives, milestones and the event log are shared.</summary>
    None,

    /// <summary>Captures with the same met location are linked (only meaningful when everyone plays the same game family).</summary>
    ByLocation,

    /// <summary>The n-th capture of each player is linked with the n-th capture of the others (works across different games).</summary>
    InOrder,

    /// <summary>Players link captures by hand.</summary>
    Manual,
}

/// <summary>What happens to the rest of a link when one of its Pokémon dies.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<DeathSpread>))]
public enum DeathSpread
{
    /// <summary>The linked Pokémon die too: their owners must retire them.</summary>
    KillLinked,

    /// <summary>The other players are only told.</summary>
    Notify,

    /// <summary>Nothing happens to the others.</summary>
    None,
}

/// <summary>Who shares the pool of lives.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<LivesMode>))]
public enum LivesMode
{
    Off,
    Individual,

    /// <summary>One pool for the whole team.</summary>
    Shared,
}

/// <summary>When the badge roulette spins for multiplayer rooms.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RouletteMode>))]
public enum RouletteMode
{
    /// <summary>Each player spins for their own milestones (the single-player behaviour).</summary>
    PerPlayer,

    /// <summary>When anyone earns a milestone, every player of the team spins.</summary>
    EveryoneOnAnyMilestone,

    /// <summary>One spin for the team, the first time any member earns each milestone; the prize goes to everyone.</summary>
    TeamSpin,
}

/// <summary>How the level cap follows the players' progress.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<LevelCapMode>))]
public enum LevelCapMode
{
    Off,

    /// <summary>Each player's cap comes from their own milestones.</summary>
    OwnProgress,

    /// <summary>Everyone's cap comes from the team member with the fewest milestones.</summary>
    SlowestPlayer,
}

/// <summary>Captures that are not normal wild encounters.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SpecialCaptures>))]
public enum SpecialCaptures
{
    /// <summary>Hatched eggs count as encounters of the location where they hatched.</summary>
    Count,

    /// <summary>Hatched eggs are ignored: not linked, not counted for the location.</summary>
    Ignore,
}

/// <summary>Ways a death is noticed when a save is synchronized. Several can be on at once.</summary>
[Flags]
[JsonConverter(typeof(JsonStringEnumConverter<DeathDetection>))]
public enum DeathDetection
{
    None = 0,

    /// <summary>A party Pokémon with 0 HP.</summary>
    FaintedInParty = 1,

    /// <summary>A Pokémon placed in the graveyard box (<see cref="LinkRules.GraveyardBox"/>).</summary>
    GraveyardBox = 2,

    /// <summary>A Pokémon that disappeared from the save (released).</summary>
    Released = 4,
}

/// <summary>A group of players whose captures are linked with each other. Players can only be in one team.</summary>
public sealed record LinkTeam(string Name, List<string> PlayerIds);

/// <summary>The ruleset of a multiplayer room. Chosen by the host; the same for every player.</summary>
public sealed class LinkRules
{
    public LinkMatching Matching { get; set; } = LinkMatching.ByLocation;

    /// <summary>
    /// The first player to capture on a location claims it: later captures of the team there break the rules. Makes
    /// the room a race instead of a link.
    /// </summary>
    public bool RaceForLocations { get; set; }

    /// <summary>
    /// Sub-areas are one location ("Route 1 (Trainers' School)" is Route 1). Otherwise every met location id of the
    /// game is a location of its own.
    /// </summary>
    public bool MergeSubAreas { get; set; } = true;

    /// <summary>Only the first capture of each player on a location counts (the classic Nuzlocke rule).</summary>
    public bool FirstEncounterOnly { get; set; } = true;

    /// <summary>A capture of a species the player already has (or had) does not use up the location.</summary>
    public bool DupesClause { get; set; } = true;

    /// <summary>Shiny captures are always allowed and never linked.</summary>
    public bool ShinyClause { get; set; } = true;

    public SpecialCaptures Eggs { get; set; } = SpecialCaptures.Ignore;

    public DeathSpread OnDeath { get; set; } = DeathSpread.KillLinked;

    public DeathDetection DeathDetection { get; set; } = DeathDetection.FaintedInParty | DeathDetection.GraveyardBox;

    /// <summary>
    /// 0-based box where dead Pokémon are kept, or -1 for the last box of the save; used when
    /// <see cref="DeathDetection.GraveyardBox"/> is on.
    /// </summary>
    public int GraveyardBox { get; set; } = -1;

    /// <summary>No two Pokémon of a player's party may share a primary type, and neither may their linked Pokémon.</summary>
    public bool UniquePrimaryTypes { get; set; }

    public LivesMode Lives { get; set; } = LivesMode.Shared;

    /// <summary>Lives per pool (per player or for the team, following <see cref="Lives"/>).</summary>
    public int MaxLives { get; set; } = 3;

    /// <summary>Each death costs a life (otherwise lives are only lost by hand, for example on a whiteout).</summary>
    public bool DeathCostsLife { get; set; }

    public RouletteMode Roulette { get; set; } = RouletteMode.PerPlayer;

    public LevelCapMode LevelCap { get; set; } = LevelCapMode.Off;

    /// <summary>Cap before the first milestone, then after each one (index = milestones earned).</summary>
    public List<int> LevelCaps { get; set; } = [];

    /// <summary>
    /// Teams of linked players. Empty = everyone in one team. With more than two players this allows pairs or any other
    /// split; players missing from every team play alone.
    /// </summary>
    public List<LinkTeam> Teams { get; set; } = [];

    public int CapFor(int milestones) =>
        LevelCaps.Count == 0 ? 100 : LevelCaps[Math.Clamp(milestones, 0, LevelCaps.Count - 1)];

    /// <summary>A preset to start from; every value stays editable.</summary>
    public static LinkRules Preset(LinkPreset preset) => preset switch
    {
        LinkPreset.SoulLink => new LinkRules
        {
            Matching = LinkMatching.ByLocation,
            OnDeath = DeathSpread.KillLinked,
            UniquePrimaryTypes = true,
            Lives = LivesMode.Off,
        },
        LinkPreset.SoulLinkDifferentGames => new LinkRules
        {
            Matching = LinkMatching.InOrder,
            OnDeath = DeathSpread.KillLinked,
            UniquePrimaryTypes = true,
            Lives = LivesMode.Off,
        },
        LinkPreset.SharedNuzlocke => new LinkRules
        {
            Matching = LinkMatching.None,
            OnDeath = DeathSpread.Notify,
            Lives = LivesMode.Shared,
            DeathCostsLife = true,
            Roulette = RouletteMode.TeamSpin,
        },
        LinkPreset.Race => new LinkRules
        {
            Matching = LinkMatching.None,
            RaceForLocations = true,
            OnDeath = DeathSpread.None,
            Lives = LivesMode.Individual,
        },
        _ => new LinkRules(),
    };
}

[JsonConverter(typeof(JsonStringEnumConverter<LinkPreset>))]
public enum LinkPreset
{
    Custom,
    SoulLink,
    SoulLinkDifferentGames,
    SharedNuzlocke,
    Race,
}
