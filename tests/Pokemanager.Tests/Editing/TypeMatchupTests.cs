using Pokemanager.Model.Data;

namespace Pokemanager.Tests.Editing;

/// <summary>The Pokédex's damage-taken card (2026-11-11, user): the Gen 6/7 chart, in the game's type order.</summary>
public sealed class TypeMatchupTests
{
    private const int Normal = 0, Fighting = 1, Flying = 2, Poison = 3, Ground = 4, Rock = 5, Bug = 6, Ghost = 7, Steel = 8,
        Fire = 9, Water = 10, Grass = 11, Electric = 12, Psychic = 13, Ice = 14, Dragon = 15, Dark = 16, Fairy = 17;

    [Fact]
    public void Charizard_FireFlying()
    {
        int[] charizard = [Fire, Flying];
        Assert.Equal(4, TypeMatchups.Against(Rock, charizard));
        Assert.Equal(2, TypeMatchups.Against(Water, charizard));
        Assert.Equal(0.25, TypeMatchups.Against(Grass, charizard));
        Assert.Equal(0.25, TypeMatchups.Against(Bug, charizard));
        Assert.Equal(0, TypeMatchups.Against(Ground, charizard));
        Assert.Equal(1, TypeMatchups.Against(Normal, charizard));
    }

    [Fact]
    public void Immunities_AndFairy()
    {
        Assert.Equal(0, TypeMatchups.Against(Normal, [Ghost]));
        Assert.Equal(0, TypeMatchups.Against(Fighting, [Ghost]));
        Assert.Equal(0, TypeMatchups.Against(Electric, [Ground]));
        Assert.Equal(0, TypeMatchups.Against(Poison, [Steel]));
        Assert.Equal(0, TypeMatchups.Against(Psychic, [Dark]));
        Assert.Equal(0, TypeMatchups.Against(Dragon, [Fairy]));
        Assert.Equal(2, TypeMatchups.Against(Steel, [Fairy]));
        Assert.Equal(4, TypeMatchups.Against(Ice, [Dragon, Flying]));   // Dragonite
    }

    [Fact]
    public void ARepeatedTypeCountsOnce_AndGroupsAreStrongestFirst()
    {
        Assert.Equal(2, TypeMatchups.Against(Fire, [Grass, Grass]));
        var groups = TypeMatchups.Groups([Fire, Flying]);
        Assert.Equal([4.0, 2.0, 1.0, 0.5, 0.25, 0.0], groups.Select(g => g.Multiplier));
        Assert.Equal(18, groups.Sum(g => g.Types.Count));
        // Every row of the chart: the 18 attacking types are all there for any defending pair.
        foreach (int a in Enumerable.Range(0, 18))
            foreach (int b in Enumerable.Range(0, 18))
                Assert.Equal(18, TypeMatchups.Groups([a, b]).Sum(g => g.Types.Count));
    }
}
