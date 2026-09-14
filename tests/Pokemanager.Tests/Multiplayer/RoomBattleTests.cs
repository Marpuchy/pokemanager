using System.Text.Json;
using PKHeX.Core;
using Pokemanager.Battle;
using Pokemanager.Multiplayer;
using Pokemanager.Save;
using Pokemanager.Tests.Editing;
using GameData = Pokemanager.Model.Data.GameData;

namespace Pokemanager.Tests.Multiplayer;

public sealed class RoomBattleTests : IDisposable
{
    private readonly SyntheticRomFs romfs = new();
    private readonly string dir = Directory.CreateTempSubdirectory("pokemanager-roombattle-").FullName;
    private static readonly ShowdownTools? Tools = ShowdownTools.Locate();

    public void Dispose()
    {
        romfs.Dispose();
        Directory.Delete(dir, true);
    }

    private RoomOptions Local(bool battles = false) =>
        new() { MapPort = false, UseStun = false, IncludeLoopback = true, Battles = battles ? Tools : null };

    private BattleTeam Team(string trainer, params ushort[] species)
    {
        var sav = new SAV6XY { OT = trainer, Language = (int)LanguageID.English };
        string path = Path.Combine(dir, trainer);
        File.WriteAllBytes(path, sav.Write().ToArray());
        var doc = SaveDocument.Open(path, GameData.Load(romfs.RomFs));
        for (int i = 0; i < species.Length; i++)
            doc.Set(new SaveSlot(null, i), doc.Create(species[i], 30));
        return BattleTeam.FromParty(doc);
    }

    private static async Task Until(Func<bool> condition, string what, int seconds = 20)
    {
        var limit = DateTime.UtcNow.AddSeconds(seconds);
        while (!condition())
        {
            if (DateTime.UtcNow > limit)
                Assert.Fail("Timed out waiting for: " + what);
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>Answers every request of a player with a random legal choice until the battle is over.</summary>
    private static async Task Play(RoomSession player, string battleId, Random rng)
    {
        var limit = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < limit)
        {
            var battle = player.Battles.FirstOrDefault(b => b.Id == battleId);
            if (battle is null || !battle.IsActive)
                return;
            if (battle.Request is { } json)
            {
                var req = JsonDocument.Parse(json).RootElement;
                string? choice = null;
                if (req.TryGetProperty("teamPreview", out _))
                    choice = "team 1";
                else if (req.TryGetProperty("forceSwitch", out _))
                {
                    var alive = req.GetProperty("side").GetProperty("pokemon").EnumerateArray().Select((p, i) => (p, i))
                        .Where(x => !x.p.GetProperty("active").GetBoolean() && !x.p.GetProperty("condition").GetString()!.EndsWith("fnt")).ToList();
                    choice = alive.Count > 0 ? $"switch {alive[rng.Next(alive.Count)].i + 1}" : "pass";
                }
                else if (req.TryGetProperty("active", out var active))
                {
                    var moves = active[0].GetProperty("moves").EnumerateArray().Select((m, i) => (m, i))
                        .Where(x => !(x.m.TryGetProperty("disabled", out var d) && d.ValueKind == JsonValueKind.True)).ToList();
                    choice = moves.Count > 0 ? $"move {moves[rng.Next(moves.Count)].i + 1}" : "move 1";
                }
                if (choice is not null)
                    player.ChooseInBattle(battleId, choice);
            }
            await Task.Delay(30);
        }
    }

    [Fact]
    public async Task TwoGuests_AgreeOnRules_GetReady_AndBattleThroughTheHost()
    {
        Assert.SkipWhen(Tools is null, "Node or battle/node_modules is not installed (npm install in battle/).");
        var cancel = TestContext.Current.CancellationToken;
        await using var host = await RoomSession.HostAsync("Arena", "host", new PlayerProfile("Oak"), new RoomRules(), Local(battles: true), cancel: cancel);
        await using var misty = await RoomSession.JoinAsync(host.InviteText!, "misty", new PlayerProfile("Misty"), Local(), cancel);
        await using var brock = await RoomSession.JoinAsync(host.InviteText!, "brock", new PlayerProfile("Brock"), Local(), cancel);
        await Until(() => misty.Players.Any(p => p.Id == "brock" && p.Online) && brock.Players.Any(p => p.Id == "misty" && p.Online), "both in the room");

        // Challenge, both see the proposal; Brock gets ready, Misty changes the rules: nobody is ready any more.
        string id = misty.Challenge("brock", new BattleRules(Level: 50));
        await Until(() => brock.Battles.Any(b => b.Id == id && b.Phase == BattlePhase.Proposed), "Brock sees the challenge");
        brock.SetBattleReady(id, Team("Brock", 1, 2));
        await Until(() => misty.Battles.Single(b => b.Id == id).Ready.Contains("brock"), "Misty sees Brock ready");
        misty.SetBattleRules(id, new BattleRules(Level: 50, SpeciesClause: true));
        await Until(() => brock.Battles.Single(b => b.Id == id) is { Rules.SpeciesClause: true, Ready.Count: 0 }, "rules changed, readiness reset");

        // Misty's team breaks Species Clause: back to the proposal with the reason.
        brock.SetBattleReady(id, Team("Brock", 1, 2));
        misty.SetBattleReady(id, Team("Misty", 3, 3));
        await Until(() => misty.Battles.Single(b => b.Id == id).Problems.Any(p => p.Contains("Species Clause")), "clause problem reported");
        Assert.Contains("Misty", misty.Battles.Single(b => b.Id == id).Problems[0]);
        Assert.Equal(BattlePhase.Proposed, brock.Battles.Single(b => b.Id == id).Phase);

        // Rules fixed: both ready, the host runs it, both play to the end and see the same result.
        // Real levels (30): no Pokémon has exactly 100 max HP, so exact HP and percentages cannot be confused below.
        misty.SetBattleRules(id, new BattleRules(Level: 0, SpeciesClause: false));
        await Until(() => misty.Battles.Single(b => b.Id == id) is { Rules.SpeciesClause: false }, "clause off");
        brock.SetBattleReady(id, Team("Brock", 1, 2));
        misty.SetBattleReady(id, Team("Misty", 3, 4));
        await Until(() => brock.Battles.Single(b => b.Id == id).Phase == BattlePhase.Running, "battle running");
        Assert.Equal("p1", misty.Battles.Single(b => b.Id == id).MySide);
        Assert.Equal("p2", brock.Battles.Single(b => b.Id == id).MySide);

        await Task.WhenAll(Play(misty, id, new Random(1)), Play(brock, id, new Random(2)));
        await Until(() => misty.Battles.Single(b => b.Id == id).Phase == BattlePhase.Finished
                          && brock.Battles.Single(b => b.Id == id).Phase == BattlePhase.Finished, "finished for both", 60);

        var mine = misty.Battles.Single(b => b.Id == id);
        var theirs = brock.Battles.Single(b => b.Id == id);
        Assert.NotNull(mine.Winner);
        Assert.Equal(mine.Winner, theirs.Winner);
        Assert.Contains(mine.Log, l => l.StartsWith("|win|"));
        Assert.Equal(mine.Log.Count(l => l.StartsWith("|turn|")), theirs.Log.Count(l => l.StartsWith("|turn|")));
        // Each player sees exact HP for their own side only.
        Assert.Contains(mine.Log, l => l.StartsWith("|switch|p1a:") && System.Text.RegularExpressions.Regex.IsMatch(l, @"\|\d+/\d+$") && !l.EndsWith("/100"));
        Assert.Contains(mine.Log, l => l.StartsWith("|switch|p2a:") && l.EndsWith("/100"));
        Assert.Contains(theirs.Log, l => l.StartsWith("|switch|p2a:") && !l.EndsWith("/100"));
        Assert.DoesNotContain(mine.Log, l => l.StartsWith("|split|"));
        Assert.Empty(host.Battles);
    }

    [Fact]
    public async Task Host_CanBattle_AndAGuestLeavingLosesTheBattle()
    {
        Assert.SkipWhen(Tools is null, "Node or battle/node_modules is not installed (npm install in battle/).");
        var cancel = TestContext.Current.CancellationToken;
        await using var host = await RoomSession.HostAsync("Arena", "host", new PlayerProfile("Oak"), new RoomRules(), Local(battles: true), cancel: cancel);
        var gary = await RoomSession.JoinAsync(host.InviteText!, "gary", new PlayerProfile("Gary"), Local(), cancel);
        await Until(() => gary.Status == RoomStatus.Online && host.Players.Any(p => p.Id == "gary" && p.Online), "Gary in");

        string id = host.Challenge("gary", new BattleRules(TeamPreview: false));
        await Until(() => gary.Battles.Any(b => b.Id == id), "Gary challenged");
        host.SetBattleReady(id, Team("Oak", 1));
        gary.SetBattleReady(id, Team("Gary", 2));
        await Until(() => host.Battles.Single(b => b.Id == id).Phase == BattlePhase.Running && host.Battles.Single(b => b.Id == id).Request is not null, "host has a request");

        await gary.DisposeAsync();
        await Until(() => host.Battles.Single(b => b.Id == id).Phase == BattlePhase.Finished, "forfeit on leaving", 30);
        Assert.Equal("host", host.Battles.Single(b => b.Id == id).Winner);

        // A room without the simulator refuses battles with a reason.
        await using var plain = await RoomSession.HostAsync("Plain", "h2", new PlayerProfile("Plain"), new RoomRules(), Local(), cancel: cancel);
        await using var guest = await RoomSession.JoinAsync(plain.InviteText!, "g2", new PlayerProfile("G"), Local(), cancel);
        await Until(() => plain.Players.Any(p => p.Id == "g2" && p.Online), "guest in plain room");
        string refused = guest.Challenge("h2", new BattleRules());
        await Until(() => guest.Battles.Any(b => b.Id == refused && b.Phase == BattlePhase.Cancelled), "refused");
        Assert.Contains("simulator", guest.Battles.Single(b => b.Id == refused).Problems[0]);
    }
}
