using Pokemanager.Model.Projects;
using Pokemanager.Multiplayer;

namespace Pokemanager.Tests.Multiplayer;

public sealed class RoomStateTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("pokemanager-room-").FullName;
    private readonly List<RoomEvent> events = [];

    public void Dispose() => Directory.Delete(dir, true);

    private void Join(params string[] players)
    {
        foreach (string p in players)
            events.Add(new PlayerJoined(p.ToUpperInvariant(), "X", 6, 8) { PlayerId = p });
    }

    private void Catch(string player, uint key, int location, ushort species = 0, int type = -1, int level = 5,
        bool shiny = false, bool egg = false, string area = "") =>
        events.Add(new PokemonCaptured(key, species == 0 ? (ushort)key : species, 0, $"mon{key}", level, location,
            $"Route {location}", egg, shiny, type, area) { PlayerId = player });

    private void Die(string player, uint key) => events.Add(new PokemonDied(key, DeathCause.Manual) { PlayerId = player });

    private static PokemonStatus Status(RoomState state, string player, uint key) => state.Find(new(player, key))!.Status;

    [Fact]
    public void SoulLink_SameLocationIsLinked_DeathDoomsThePartner_RetiringIsNotANewDeath()
    {
        var rules = LinkRules.Preset(LinkPreset.SoulLink);
        rules.Lives = LivesMode.Shared;
        rules.DeathCostsLife = true;
        Join("a", "b");
        Catch("a", 1, location: 5);
        Catch("b", 2, location: 5);
        Catch("b", 3, location: 6);

        Die("a", 1);
        var state = RoomState.Build(rules, events);

        Assert.Single(state.Links.Values, l => l.Members.Count == 2);
        Assert.Equal(PokemonStatus.Dead, Status(state, "a", 1));
        Assert.Equal(PokemonStatus.Doomed, Status(state, "b", 2));
        Assert.Equal(PokemonStatus.Alive, Status(state, "b", 3));
        var notice = Assert.Single(state.Notices);
        Assert.Equal((NoticeKind.MustRetire, "b", 2u, new LinkMember("a", 1)), (notice.Kind, notice.PlayerId, notice.Key, notice.Other!.Value));
        Assert.Equal(1, state.PoolOf("a")!.Lost);
        Assert.Same(state.PoolOf("a"), state.PoolOf("b"));

        Die("b", 2);
        state = RoomState.Build(rules, events);

        Assert.Equal(PokemonStatus.Dead, Status(state, "b", 2));
        Assert.Empty(state.Notices);
        Assert.Equal(1, state.PoolOf("b")!.Lost);
    }

    [Fact]
    public void Captures_LinkedAfterThePartnerDied_AreDoomedAtOnce()
    {
        Join("a", "b");
        Catch("a", 1, 5);
        Die("a", 1);
        Catch("b", 2, 5);

        var state = RoomState.Build(LinkRules.Preset(LinkPreset.SoulLink), events);

        Assert.Equal(PokemonStatus.Doomed, Status(state, "b", 2));
    }

    [Fact]
    public void Clauses_SecondEncounterBreaksTheRules_DupesShiniesAndEggsDoNotUseTheLocation()
    {
        Join("a", "b");
        Catch("a", 1, 5, species: 10);
        Catch("a", 2, 5, species: 10);          // dupe: allowed, not linked
        Catch("a", 3, 5, species: 11, shiny: true);
        Catch("a", 4, 5, species: 12, egg: true);
        Catch("a", 5, 5, species: 13);          // second encounter

        var state = RoomState.Build(new LinkRules(), events);

        Assert.True(state.Find(new("a", 1))!.Counts);
        Assert.All(new uint[] { 2, 3, 4, 5 }, k => Assert.False(state.Find(new("a", k))!.Counts));
        var notice = Assert.Single(state.Notices);
        Assert.Equal((NoticeKind.SecondEncounter, 5u), (notice.Kind, notice.Key));
        Assert.Equal([new LinkMember("a", 1)], state.Links.Values.Single().Members);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SubAreas_AreOneLocation_UnlessTheRulesSplitThem(bool merge)
    {
        // Ultra Sun: "Route 1" is met location 8 and "Route 1 (Trainers' School)" is 230; both have area "Route 1".
        Join("a", "b");
        Catch("a", 1, 8, area: "Route 1");
        Catch("a", 2, 230, area: "Route 1");
        Catch("b", 3, 230, area: "Route 1");

        var state = RoomState.Build(new LinkRules { MergeSubAreas = merge }, events);

        Assert.Equal(merge, state.Notices.Any(n => n is { Kind: NoticeKind.SecondEncounter, Key: 2 }));
        Assert.Equal(merge ? state.Find(new("a", 1))!.LinkId : state.Find(new("a", 2))!.LinkId, state.Find(new("b", 3))!.LinkId);
    }

    [Fact]
    public void SaveSync_Area_IsTheEnglishNameWithoutTheSubArea()
    {
        var save = new SaveSnapshot(7, "US", 32, 5, [], [], DateTime.Now);
        SnapshotPokemon At(int location) => new(1, 1, 0, "", 5, null, 10, location, false, false, false, 0, true, null);

        Assert.Equal("Route 1", SaveSync.Area(save, At(8)));
        Assert.Equal("Route 1", SaveSync.Area(save, At(230)));
        Assert.Equal("Hau’oli City", SaveSync.Area(save, At(20)));
    }

    [Fact]
    public void InOrder_LinksTheNthCaptures_WhateverTheLocation()
    {
        Join("a", "b", "c");
        Catch("a", 1, 5); Catch("a", 2, 6);
        Catch("b", 11, 100); Catch("b", 12, 200);
        Catch("c", 21, 7);

        var state = RoomState.Build(LinkRules.Preset(LinkPreset.SoulLinkDifferentGames), events);

        Assert.Equal(state.Find(new("a", 1))!.LinkId, state.Find(new("b", 11))!.LinkId);
        Assert.Equal(state.Find(new("a", 1))!.LinkId, state.Find(new("c", 21))!.LinkId);
        Assert.Equal(state.Find(new("a", 2))!.LinkId, state.Find(new("b", 12))!.LinkId);
        Assert.NotEqual(state.Find(new("a", 1))!.LinkId, state.Find(new("a", 2))!.LinkId);
    }

    [Fact]
    public void Teams_LinkOnlyInsideEachTeam_AndPlayersOutsideTeamsPlayAlone()
    {
        var rules = LinkRules.Preset(LinkPreset.SoulLink);
        rules.Teams = [new("red", ["a", "b"]), new("blue", ["c", "d"])];
        Join("a", "b", "c", "d", "e");
        foreach (var (p, k) in new[] { ("a", 1u), ("b", 2u), ("c", 3u), ("d", 4u), ("e", 5u) })
            Catch(p, k, 5);
        Die("a", 1);

        var state = RoomState.Build(rules, events);

        Assert.Equal(PokemonStatus.Doomed, Status(state, "b", 2));
        Assert.Equal(PokemonStatus.Alive, Status(state, "c", 3));
        Assert.Equal(2, state.Links.Count);
        Assert.Null(state.Find(new("e", 5))!.LinkId);
    }

    [Fact]
    public void Race_FirstCaptureClaimsTheLocation()
    {
        Join("a", "b");
        Catch("a", 1, 5);
        Catch("b", 2, 5);
        Catch("b", 3, 6);

        var state = RoomState.Build(LinkRules.Preset(LinkPreset.Race), events);

        var notice = Assert.Single(state.Notices);
        Assert.Equal((NoticeKind.LocationClaimed, "b", 2u, new LinkMember("a", 1)), (notice.Kind, notice.PlayerId, notice.Key, notice.Other!.Value));
        Assert.True(state.Find(new("b", 3))!.Counts);
        Assert.Empty(state.Links);
    }

    [Fact]
    public void UndoAndManualLinks_AreReplayedAsIfTheCancelledEventNeverHappened()
    {
        var rules = LinkRules.Preset(LinkPreset.SoulLink);
        Join("a", "b");
        Catch("a", 1, 5); Catch("b", 2, 5); Catch("b", 3, 6);
        Die("a", 1);
        events.Add(new DeathUndone(1) { PlayerId = "a" });

        var state = RoomState.Build(rules, events);
        Assert.Equal(PokemonStatus.Alive, Status(state, "a", 1));
        Assert.Equal(PokemonStatus.Alive, Status(state, "b", 2));

        events.Add(new ManualLink(1, [new("b", 3)]) { PlayerId = "a" });
        Die("a", 1);
        state = RoomState.Build(rules, events);
        Assert.Equal(PokemonStatus.Alive, Status(state, "b", 2));
        Assert.Equal(PokemonStatus.Doomed, Status(state, "b", 3));

        events.Add(new ManualUnlink(3) { PlayerId = "b" });
        state = RoomState.Build(rules, events);
        Assert.Equal(PokemonStatus.Alive, Status(state, "b", 3));
        Assert.Null(state.Find(new("b", 3))!.LinkId);
    }

    [Fact]
    public void PartyChecks_DuplicatePrimaryTypesAndTheSlowestPlayersLevelCap()
    {
        var rules = new LinkRules { UniquePrimaryTypes = true, LevelCap = LevelCapMode.SlowestPlayer, LevelCaps = [12, 20, 30] };
        Join("a", "b");
        Catch("a", 1, 5, type: 3, level: 15);
        Catch("a", 2, 6, type: 3, level: 10);
        Catch("b", 3, 5, type: 4, level: 25);
        events.Add(new MilestoneEarned(0) { PlayerId = "a" });
        events.Add(new MilestoneEarned(1) { PlayerId = "a" });
        events.Add(new MilestoneEarned(0) { PlayerId = "b" });
        events.Add(new SaveSynced([1, 2], 2) { PlayerId = "a" });
        events.Add(new SaveSynced([3], 1) { PlayerId = "b" });

        var state = RoomState.Build(rules, events);

        Assert.Contains(state.Notices, n => n is { Kind: NoticeKind.DuplicatePrimaryType, PlayerId: "a", Key: 2, Value: 3 });
        Assert.Contains(state.Notices, n => n is { Kind: NoticeKind.OverLevelCap, PlayerId: "b", Key: 3, Value: 20 });
        Assert.DoesNotContain(state.Notices, n => n is { Kind: NoticeKind.OverLevelCap, PlayerId: "a" });
    }

    [Fact]
    public void Lives_ManualChangesRoulettePrizesAndAnEmptyPool()
    {
        var rules = new LinkRules { Lives = LivesMode.Individual, MaxLives = 2 };
        Join("a", "b");
        events.Add(new LivesChanged(-2, "whiteout") { PlayerId = "a" });

        var state = RoomState.Build(rules, events);
        Assert.Equal(0, state.PoolOf("a")!.Left);
        Assert.Equal(2, state.PoolOf("b")!.Left);
        Assert.Contains(state.Notices, n => n is { Kind: NoticeKind.NoLivesLeft, PlayerId: "a" });

        events.Add(new RouletteSpun(0, new LockePrize(LockePrizeKind.Life), false) { PlayerId = "a" });
        state = RoomState.Build(rules, events);
        Assert.Equal((2, 1, 1), (state.PoolOf("a")!.Max, state.PoolOf("a")!.Lost, state.PoolOf("a")!.Left));
    }

    [Theory]
    [InlineData(RouletteMode.PerPlayer, new[] { 0 }, new int[0])]
    [InlineData(RouletteMode.EveryoneOnAnyMilestone, new[] { 0, 1 }, new[] { 1 })]
    [InlineData(RouletteMode.TeamSpin, new[] { 0, 1 }, new int[0])]
    public void PendingRoulette_FollowsTheMode(RouletteMode mode, int[] beforeSpin, int[] afterSpin)
    {
        var rules = new LinkRules { Roulette = mode };
        Join("a", "b");
        events.Add(new MilestoneEarned(0) { PlayerId = "a" });
        events.Add(new MilestoneEarned(1) { PlayerId = "b" });

        Assert.Equal(beforeSpin, RoomState.Build(rules, events).PendingRoulette("a", _ => true));

        var prize = new LockePrize(LockePrizeKind.Nothing);
        if (mode == RouletteMode.TeamSpin)
        {
            events.Add(new RouletteSpun(0, prize, true) { PlayerId = "a" });
            events.Add(new RouletteSpun(1, prize, true) { PlayerId = "b" });
        }
        else
        {
            events.Add(new RouletteSpun(0, prize, false) { PlayerId = "a" });
        }
        Assert.Equal(afterSpin, RoomState.Build(rules, events).PendingRoulette("a", _ => true));
    }

    [Fact]
    public void Room_RoundTripsEventsAndRules_AndIgnoresRepeatedEvents()
    {
        var room = new LinkRoom { Name = "Duo", Rules = LinkRules.Preset(LinkPreset.SoulLink), LocalPlayerId = "a" };
        room.Rules.Teams = [new("red", ["a", "b"])];
        Join("a", "b");
        Catch("a", 1, 5); Catch("b", 2, 5);
        Die("a", 1);
        events.Add(new RouletteSpun(0, new LockePrize(LockePrizeKind.Item, 50, 3), false) { PlayerId = "a" });
        events.Add(new ManualLink(1, [new("b", 2)]) { PlayerId = "a" });
        Assert.Equal(events.Count, room.Append(events));
        Assert.Equal(0, room.Append(events));
        string path = Path.Combine(dir, "room.json");

        room.Save(path);
        var loaded = LinkRoom.Load(path);

        Assert.Equal(room.Events, loaded.Events, new EventComparer());
        Assert.Equal(LinkMatching.ByLocation, loaded.Rules.Matching);
        Assert.Equal(["a", "b"], loaded.Rules.Teams.Single().PlayerIds);
        Assert.Equal(PokemonStatus.Doomed, loaded.State().Find(new("b", 2))!.Status);
        Assert.Throws<InvalidDataException>(() =>
        {
            File.WriteAllText(path, """{"format":"pokemanager-project"}""");
            LinkRoom.Load(path);
        });
    }

    /// <summary>Records holding lists compare them by reference: compare the serialized form instead.</summary>
    private sealed class EventComparer : IEqualityComparer<RoomEvent>
    {
        private static string Text(RoomEvent e) => System.Text.Json.JsonSerializer.Serialize(e);
        public bool Equals(RoomEvent? x, RoomEvent? y) => x is not null && y is not null && Text(x) == Text(y);
        public int GetHashCode(RoomEvent obj) => Text(obj).GetHashCode();
    }
}
