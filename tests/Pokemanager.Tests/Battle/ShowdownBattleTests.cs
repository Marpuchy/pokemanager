using System.Text.Json;
using PKHeX.Core;
using Pokemanager.Battle;
using Pokemanager.Save;
using Pokemanager.Tests.Editing;
using GameData = Pokemanager.Model.Data.GameData;

namespace Pokemanager.Tests.Battle;

public sealed class ShowdownBattleTests : IDisposable
{
    private readonly SyntheticRomFs romfs = new();
    private readonly string dir = Directory.CreateTempSubdirectory("pokemanager-battle-").FullName;

    /// <summary>Node and <c>battle/node_modules</c> (npm install in battle/); tests that need them are skipped otherwise.</summary>
    private static readonly ShowdownTools? Tools = ShowdownTools.Locate();

    public void Dispose()
    {
        romfs.Dispose();
        Directory.Delete(dir, true);
    }

    private (SaveDocument Save, GameData Rom) Party(string trainer, params (ushort Species, int Level)[] mons)
    {
        var sav = new SAV6XY { OT = trainer, Language = (int)LanguageID.English };
        string path = Path.Combine(dir, trainer);
        File.WriteAllBytes(path, sav.Write().ToArray());
        var rom = GameData.Load(romfs.RomFs);
        var doc = SaveDocument.Open(path, rom);
        for (int i = 0; i < mons.Length; i++)
            doc.Set(new SaveSlot(null, i), doc.Create(mons[i].Species, mons[i].Level));
        return (doc, rom);
    }

    [Fact]
    public void Team_TakesThePartyAndTheRomDataItNeeds()
    {
        var (save, rom) = Party("Ash", (1, 20), (3, 30));
        rom.Moves[2].Power = 77;
        rom.Moves[2].Type = 11;

        var team = BattleTeam.FromParty(save);

        Assert.Equal("Ash", team.Player);
        Assert.Equal([(ushort)1, (ushort)3], team.Mons.Select(m => m.Species));
        Assert.Equal([20, 30], team.Mons.Select(m => m.Level));
        var pk = save.Get(new SaveSlot(null, 0));
        Assert.Equal([pk.IV_HP, pk.IV_ATK, pk.IV_DEF, pk.IV_SPE, pk.IV_SPA, pk.IV_SPD], team.Mons[0].Ivs);
        Assert.Equal(save.MaxPP(2, 0), team.Mons[0].Pp[1][1]);
        var species1 = team.Species.Single(s => s.Species == 1);
        var p = rom.Personal[1];
        Assert.Equal([p.HP, p.ATK, p.DEF, p.SPE, p.SPA, p.SPD], species1.BaseStats);
        Assert.Equal((11, 77), (team.Moves.Single(m => m.Id == 2).Type, team.Moves.Single(m => m.Id == 2).Power));
    }

    public static TheoryData<BattleRules> RuleSets() =>
    [
        new BattleRules(),
        new BattleRules(Generation: 6, Level: 50, TeamPreview: false),
        new BattleRules(Level: 100, HealBefore: false, SleepClause: false, SpeciesClause: true, ItemClause: true, OhkoClause: false,
            EvasionClause: false, BatonPassClause: true, NoMegas: true, NoZMoves: true),
    ];

    [Fact]
    public async Task Simulator_RefusesTeamsThatBreakTheClauses()
    {
        Assert.SkipWhen(Tools is null, "Node or battle/node_modules is not installed (npm install in battle/).");
        var (ash, _) = Party("Ash", (1, 20), (1, 22));
        var (gary, _) = Party("Gary", (2, 20));
        IReadOnlyList<InvalidTeam>? invalid = null;

        await using var battle = ShowdownBattle.Start(Tools!, new BattleRules(SpeciesClause: true), BattleTeam.FromParty(ash), BattleTeam.FromParty(gary));
        battle.Received += m => { if (m.Type == "invalid") invalid = m.Teams; };
        await battle.Exited.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
        await Task.Delay(200, TestContext.Current.CancellationToken);

        var team = Assert.Single(invalid!);
        Assert.Equal("p1", team.Side);
        Assert.Contains(team.Problems, p => p.Contains("Species Clause"));
    }

    [Theory]
    [MemberData(nameof(RuleSets))]
    public async Task Simulator_AcceptsTheRules_AndUsesEachPlayersRomStats(BattleRules rules)
    {
        Assert.SkipWhen(Tools is null, "Node or battle/node_modules is not installed (npm install in battle/).");
        var (ash, ashRom) = Party("Ash", (1, 20), (4, 25));
        var (gary, garyRom) = Party("Gary", (1, 20));
        garyRom.Personal[1].HP = 200; // Gary's ROM gives species 1 other stats than Ash's
        garyRom.Personal[1].SPE = 5;

        var requests = new Dictionary<string, JsonElement>();
        string? error = null;
        await using var battle = ShowdownBattle.Start(Tools!, rules, BattleTeam.FromParty(ash), BattleTeam.FromParty(gary), seed: [1, 2, 3, 4]);
        battle.Received += m =>
        {
            if (m.Type == "error")
                error = m.Message;
            foreach (string line in m.Lines.Where(l => l.StartsWith("|request|")))
            {
                lock (requests)
                    requests[m.Side!] = JsonDocument.Parse(line[9..]).RootElement.Clone();
            }
        };
        var limit = DateTime.UtcNow.AddSeconds(20);
        while (error is null && requests.Count < 2 && DateTime.UtcNow < limit)
            await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.Null(error);
        foreach (var (side, save) in new[] { ("p1", ash), ("p2", gary) })
        {
            var mons = requests[side].GetProperty("side").GetProperty("pokemon").EnumerateArray().ToList();
            for (int i = 0; i < mons.Count; i++)
            {
                var pk = save.Get(new SaveSlot(null, i));
                if (rules.Level > 0)
                    save.SetLevel(pk, rules.Level);
                int[] expected = save.Stats(pk); // HP, Atk, Def, Spe, SpA, SpD
                var stats = mons[i].GetProperty("stats");
                string condition = mons[i].GetProperty("condition").GetString()!;
                int maxHp = int.Parse(condition.Split('/')[1].Split(' ')[0]);
                int[] simulator = [maxHp, stats.GetProperty("atk").GetInt32(), stats.GetProperty("def").GetInt32(),
                    stats.GetProperty("spe").GetInt32(), stats.GetProperty("spa").GetInt32(), stats.GetProperty("spd").GetInt32()];
                Assert.Equal(expected, simulator);
            }
        }
    }
}
