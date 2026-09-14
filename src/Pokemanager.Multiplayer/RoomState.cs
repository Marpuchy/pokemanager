using Pokemanager.Model.Projects;

namespace Pokemanager.Multiplayer;

public enum PokemonStatus
{
    Alive,

    /// <summary>Alive in the save, but a linked Pokémon died: the owner has to retire it.</summary>
    Doomed,

    Dead,
}

public enum NoticeKind
{
    /// <summary><see cref="RoomNotice.Other"/> died and this linked Pokémon must be retired.</summary>
    MustRetire,

    /// <summary><see cref="RoomNotice.Other"/> died (rules only notify).</summary>
    LinkedDied,

    /// <summary>A second capture on a location that was already used.</summary>
    SecondEncounter,

    /// <summary>A capture on a location another team member claimed first (<see cref="RoomNotice.Other"/>).</summary>
    LocationClaimed,

    /// <summary>The party has another Pokémon with this primary type (<see cref="RoomNotice.Value"/>).</summary>
    DuplicatePrimaryType,

    /// <summary>A party Pokémon above the level cap (<see cref="RoomNotice.Value"/>).</summary>
    OverLevelCap,

    /// <summary>The lives pool <see cref="RoomNotice.Pool"/> is empty.</summary>
    NoLivesLeft,
}

/// <summary>Something the players should see: a rule broken or an action they have to take.</summary>
/// <param name="PlayerId">Player it concerns (for <see cref="NoticeKind.NoLivesLeft"/> on a shared pool, the first member).</param>
public sealed record RoomNotice(NoticeKind Kind, string PlayerId, uint Key = 0, LinkMember? Other = null, int Value = 0,
    string? Pool = null);

public sealed class PlayerState(string id)
{
    public string Id { get; } = id;
    public string Name { get; internal set; } = id;
    public string Game { get; internal set; } = "";
    public int Generation { get; internal set; }
    public int MilestoneCount { get; internal set; }
    public string Team { get; internal set; } = "";
    public SortedSet<int> Milestones { get; } = [];
    public List<uint> Party { get; internal set; } = [];
    public DateTime? LastSync { get; internal set; }
    public List<RouletteSpun> Spins { get; } = [];
}

public sealed class PokemonState(string playerId, uint key)
{
    public string PlayerId { get; } = playerId;
    public uint Key { get; } = key;
    public LinkMember Member => new(PlayerId, Key);

    public ushort OriginalSpecies { get; internal set; }
    public ushort Species { get; internal set; }
    public byte Form { get; internal set; }
    public string Nickname { get; internal set; } = "";
    public int Level { get; internal set; }
    public int Location { get; internal set; }
    public string LocationName { get; internal set; } = "";

    /// <summary>What the rules compare as "the same location" (the area or the location id, see <see cref="LinkRules.MergeSubAreas"/>).</summary>
    public string LocationKey { get; internal set; } = "";

    public bool WasEgg { get; internal set; }
    public bool IsShiny { get; internal set; }
    public int PrimaryType { get; internal set; } = -1;

    /// <summary>The capture uses up its location and can be linked (not a shiny, dupe, ignored egg or broken rule).</summary>
    public bool Counts { get; internal set; }

    /// <summary>Position among the player's counting captures (0-based), -1 when it does not count.</summary>
    public int Order { get; internal set; } = -1;

    public PokemonStatus Status { get; internal set; }
    public DeathCause? Cause { get; internal set; }
    public string? LinkId { get; internal set; }
}

public sealed class LinkGroup(string id, string team)
{
    public string Id { get; } = id;
    public string Team { get; } = team;
    public List<LinkMember> Members { get; } = [];
}

public sealed class LivesPool(string id, int max)
{
    public string Id { get; } = id;
    public int Max { get; internal set; } = max;
    public int Lost { get; internal set; }
    public int Left => Math.Max(0, Max - Lost);
}

/// <summary>
/// A room's situation, built by replaying its events under its rules. Nothing here is stored: changing the rules and
/// rebuilding re-judges the whole run, identically for every player.
/// </summary>
public sealed class RoomState
{
    private const string EveryoneTeam = "team";

    private readonly LinkRules rules;
    private readonly Dictionary<(string Player, uint Key), PokemonState> pokemon = [];
    private readonly Dictionary<string, (string Player, uint Key)> claims = [];
    private readonly HashSet<uint> unlinked = [];
    private readonly List<RoomNotice> violations = [];

    public Dictionary<string, PlayerState> Players { get; } = [];
    public Dictionary<string, LinkGroup> Links { get; } = [];
    public Dictionary<string, LivesPool> Lives { get; } = [];
    public IReadOnlyCollection<PokemonState> Pokemon => pokemon.Values;
    public List<RoomNotice> Notices { get; } = [];

    private RoomState(LinkRules rules) => this.rules = rules;

    public PokemonState? Find(LinkMember member) => pokemon.GetValueOrDefault((member.PlayerId, member.Key));

    public IEnumerable<PokemonState> PokemonOf(string playerId) => pokemon.Values.Where(p => p.PlayerId == playerId);

    public LivesPool? PoolOf(string playerId) => rules.Lives switch
    {
        LivesMode.Individual => Lives.GetValueOrDefault(playerId),
        LivesMode.Shared => Lives.GetValueOrDefault(TeamOf(playerId)),
        _ => null,
    };

    public static RoomState Build(LinkRules rules, IEnumerable<RoomEvent> events)
    {
        var state = new RoomState(rules);
        foreach (var e in Preprocess(events))
            state.Apply(e);
        state.Judge();
        return state;
    }

    /// <summary>Undo and unlink events cancel what they refer to, so the replay never has to take anything back.</summary>
    private static List<RoomEvent> Preprocess(IEnumerable<RoomEvent> events)
    {
        var list = new List<RoomEvent>();
        var seen = new HashSet<Guid>();
        foreach (var e in events)
        {
            if (!seen.Add(e.Id))
                continue;
            switch (e)
            {
                case DeathUndone undo:
                    int died = list.FindLastIndex(x => x is PokemonDied d && d.PlayerId == undo.PlayerId && d.Key == undo.Key);
                    if (died >= 0)
                        list.RemoveAt(died);
                    break;
                case ManualUnlink unlink:
                    list.RemoveAll(x => x is ManualLink l && Involves(l, new LinkMember(unlink.PlayerId, unlink.Key)));
                    list.Add(unlink);
                    break;
                default:
                    list.Add(e);
                    break;
            }
        }
        return list;
    }

    private static bool Involves(ManualLink link, LinkMember member) =>
        (link.PlayerId == member.PlayerId && link.Key == member.Key) || link.With.Contains(member);

    private string TeamOf(string playerId)
    {
        if (rules.Teams.Count == 0)
            return EveryoneTeam;
        var team = rules.Teams.FirstOrDefault(t => t.PlayerIds.Contains(playerId));
        return team is null ? "solo:" + playerId : "team:" + team.Name;
    }

    /// <summary>Players outside every team play alone; in a team, links are made even before the others join.</summary>
    private static bool IsSolo(string team) => team.StartsWith("solo:", StringComparison.Ordinal);

    private PlayerState Player(string id)
    {
        if (!Players.TryGetValue(id, out var player))
        {
            player = new PlayerState(id) { Team = TeamOf(id) };
            Players[id] = player;
            if (rules.Lives == LivesMode.Individual)
                Lives[id] = new LivesPool(id, rules.MaxLives);
            else if (rules.Lives == LivesMode.Shared && !Lives.ContainsKey(player.Team))
                Lives[player.Team] = new LivesPool(player.Team, rules.MaxLives);
        }
        return player;
    }

    private void Apply(RoomEvent e)
    {
        var player = Player(e.PlayerId);
        switch (e)
        {
            case PlayerJoined joined:
                (player.Name, player.Game, player.Generation, player.MilestoneCount) =
                    (joined.Name, joined.Game, joined.Generation, joined.MilestoneCount);
                break;
            case PokemonCaptured captured:
                Capture(player, captured);
                break;
            case PokemonUpdated updated when pokemon.TryGetValue((e.PlayerId, updated.Key), out var mon):
                (mon.Species, mon.Form, mon.Nickname, mon.Level, mon.PrimaryType) =
                    (updated.Species, updated.Form, updated.Nickname, updated.Level, updated.PrimaryType);
                break;
            case PokemonDied died when pokemon.TryGetValue((e.PlayerId, died.Key), out var mon):
                Die(mon, died.Cause);
                break;
            case MilestoneEarned milestone:
                player.Milestones.Add(milestone.Index);
                break;
            case SaveSynced synced:
                player.Party = synced.Party;
                player.LastSync = synced.When;
                break;
            case LivesChanged lives when PoolOf(e.PlayerId) is { } pool:
                ChangeLives(pool, lives.Delta);
                break;
            case ManualLink link:
                Link(link);
                break;
            case ManualUnlink unlink when pokemon.TryGetValue((e.PlayerId, unlink.Key), out var mon):
                unlinked.Add(unlink.Key);
                Unlink(mon);
                break;
            case RouletteSpun spun:
                player.Spins.Add(spun);
                if (spun.Prize.Kind == LockePrizeKind.Life && PoolOf(e.PlayerId) is { } lifePool)
                    ChangeLives(lifePool, 1);
                break;
        }
    }

    private static void ChangeLives(LivesPool pool, int delta)
    {
        for (; delta > 0; delta--)
        {
            if (pool.Lost > 0)
                pool.Lost--;
            else
                pool.Max++;
        }
        pool.Lost -= delta; // delta is now 0 or negative
    }

    private void Capture(PlayerState player, PokemonCaptured e)
    {
        if (pokemon.ContainsKey((player.Id, e.Key)))
            return;
        var mine = PokemonOf(player.Id).ToList();
        var mon = new PokemonState(player.Id, e.Key)
        {
            OriginalSpecies = e.Species, Species = e.Species, Form = e.Form, Nickname = e.Nickname, Level = e.Level,
            Location = e.Location, LocationName = e.LocationName, WasEgg = e.WasEgg, IsShiny = e.IsShiny,
            PrimaryType = e.PrimaryType,
            LocationKey = rules.MergeSubAreas && e.Area.Length > 0 ? "area:" + e.Area : "id:" + e.Location,
        };
        pokemon[(player.Id, e.Key)] = mon;

        bool counts = !(rules.ShinyClause && e.IsShiny) && !(e.WasEgg && rules.Eggs == SpecialCaptures.Ignore);
        if (counts && rules.DupesClause && mine.Any(m => m.OriginalSpecies == e.Species || m.Species == e.Species))
            counts = false;
        if (counts && rules.FirstEncounterOnly && mine.Any(m => m.Counts && m.LocationKey == mon.LocationKey))
        {
            counts = false;
            violations.Add(new RoomNotice(NoticeKind.SecondEncounter, player.Id, e.Key, Value: e.Location));
        }
        string claim = player.Team + "/" + mon.LocationKey;
        if (counts && rules.RaceForLocations)
        {
            if (claims.TryGetValue(claim, out var owner) && owner.Player != player.Id)
            {
                counts = false;
                violations.Add(new RoomNotice(NoticeKind.LocationClaimed, player.Id, e.Key, new LinkMember(owner.Player, owner.Key),
                    e.Location));
            }
            else
            {
                claims.TryAdd(claim, (player.Id, e.Key));
            }
        }
        if (!counts)
            return;

        mon.Counts = true;
        mon.Order = mine.Count(m => m.Counts);
        if (IsSolo(player.Team))
            return;
        string? groupId = rules.Matching switch
        {
            LinkMatching.ByLocation => $"{player.Team}/location/{mon.LocationKey}",
            LinkMatching.InOrder => $"{player.Team}/order/{mon.Order}",
            _ => null,
        };
        if (groupId is null)
            return;
        if (!Links.TryGetValue(groupId, out var group))
            Links[groupId] = group = new LinkGroup(groupId, player.Team);
        if (group.Members.Any(m => m.PlayerId == player.Id))
            return;
        group.Members.Add(mon.Member);
        mon.LinkId = groupId;
    }

    private void Die(PokemonState mon, DeathCause cause)
    {
        if (mon.Status == PokemonStatus.Dead)
            return;
        // Retiring a Pokémon whose link already died is following the rules, not a new death.
        bool linkedAlreadyDead = DeadMembers(mon).Any();
        mon.Status = PokemonStatus.Dead;
        mon.Cause = cause;
        if (!linkedAlreadyDead && rules.DeathCostsLife && PoolOf(mon.PlayerId) is { } pool)
            pool.Lost++;
    }

    private IEnumerable<PokemonState> DeadMembers(PokemonState mon) =>
        mon.LinkId is { } id && Links.TryGetValue(id, out var group)
            ? group.Members.Where(m => m != mon.Member).Select(Find).OfType<PokemonState>()
                .Where(m => m.Status == PokemonStatus.Dead)
            : [];

    private void Unlink(PokemonState mon)
    {
        if (mon.LinkId is { } id && Links.TryGetValue(id, out var group))
        {
            group.Members.Remove(mon.Member);
            if (group.Members.Count == 0)
                Links.Remove(id);
        }
        mon.LinkId = null;
    }

    private void Link(ManualLink e)
    {
        var members = new List<PokemonState>();
        foreach (var member in e.With.Prepend(new LinkMember(e.PlayerId, e.Key)))
        {
            if (Find(member) is { } mon && members.All(m => m.PlayerId != mon.PlayerId)
                && (members.Count == 0 || Players[mon.PlayerId].Team == Players[members[0].PlayerId].Team))
                members.Add(mon);
        }
        if (members.Count < 2)
            return;
        var group = new LinkGroup("manual/" + e.Id, Players[members[0].PlayerId].Team);
        foreach (var mon in members)
        {
            Unlink(mon);
            group.Members.Add(mon.Member);
            mon.LinkId = group.Id;
        }
        Links[group.Id] = group;
    }

    /// <summary>Works out what follows from the final situation: doomed Pokémon and the notices.</summary>
    private void Judge()
    {
        // Automatic links of Pokémon the players unlinked by hand.
        foreach (var mon in pokemon.Values.Where(m => unlinked.Contains(m.Key) && m.LinkId is { } id && !id.StartsWith("manual/")))
            Unlink(mon);

        Notices.AddRange(violations);
        foreach (var mon in pokemon.Values.Where(m => m.Status == PokemonStatus.Alive))
        {
            if (DeadMembers(mon).FirstOrDefault() is not { } dead)
                continue;
            if (rules.OnDeath == DeathSpread.KillLinked)
            {
                mon.Status = PokemonStatus.Doomed;
                Notices.Add(new RoomNotice(NoticeKind.MustRetire, mon.PlayerId, mon.Key, dead.Member));
            }
            else if (rules.OnDeath == DeathSpread.Notify)
            {
                Notices.Add(new RoomNotice(NoticeKind.LinkedDied, mon.PlayerId, mon.Key, dead.Member));
            }
        }

        foreach (var player in Players.Values)
        {
            var party = player.Party.Select(k => pokemon.GetValueOrDefault((player.Id, k)))
                .OfType<PokemonState>().Where(m => m.Status != PokemonStatus.Dead).ToList();
            if (rules.UniquePrimaryTypes)
            {
                foreach (var repeated in party.Where(m => m.PrimaryType >= 0).GroupBy(m => m.PrimaryType).Where(g => g.Count() > 1))
                    foreach (var mon in repeated.Skip(1))
                        Notices.Add(new RoomNotice(NoticeKind.DuplicatePrimaryType, player.Id, mon.Key, Value: repeated.Key));
            }
            if (rules.LevelCap != LevelCapMode.Off)
            {
                int milestones = rules.LevelCap == LevelCapMode.SlowestPlayer
                    ? Players.Values.Where(p => p.Team == player.Team).Min(p => p.Milestones.Count)
                    : player.Milestones.Count;
                int cap = rules.CapFor(milestones);
                foreach (var mon in party.Where(m => m.Level > cap))
                    Notices.Add(new RoomNotice(NoticeKind.OverLevelCap, player.Id, mon.Key, Value: cap));
            }
        }

        foreach (var pool in Lives.Values.Where(p => p.Left == 0))
        {
            string owner = rules.Lives == LivesMode.Individual
                ? pool.Id
                : Players.Values.Where(p => p.Team == pool.Id).Select(p => p.Id).FirstOrDefault() ?? "";
            Notices.Add(new RoomNotice(NoticeKind.NoLivesLeft, owner, Pool: pool.Id));
        }
    }

    /// <summary>
    /// Milestones (0-based) the player can still spin the roulette for, following <see cref="LinkRules.Roulette"/>.
    /// </summary>
    /// <param name="hasRoulette">The project's roulette interval (<see cref="LockeSettings.HasRoulette"/>).</param>
    public IReadOnlyList<int> PendingRoulette(string playerId, Func<int, bool> hasRoulette)
    {
        if (!Players.TryGetValue(playerId, out var player))
            return [];
        var team = Players.Values.Where(p => p.Team == player.Team).ToList();
        IEnumerable<int> earned = rules.Roulette == RouletteMode.PerPlayer
            ? player.Milestones
            : team.SelectMany(p => p.Milestones).Distinct();
        var spun = rules.Roulette == RouletteMode.TeamSpin
            ? team.SelectMany(p => p.Spins).Where(s => s.ForTeam).Select(s => s.Milestone).ToHashSet()
            : player.Spins.Where(s => !s.ForTeam).Select(s => s.Milestone).ToHashSet();
        return earned.Where(m => hasRoulette(m) && !spun.Contains(m)).Order().ToList();
    }
}
