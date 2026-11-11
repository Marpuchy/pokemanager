namespace Pokemanager.Model.Data;

/// <summary>
/// The type chart of Generations 6 and 7 (Fairy included), in the game's type order: Normal, Fighting, Flying, Poison,
/// Ground, Rock, Bug, Ghost, Steel, Fire, Water, Grass, Electric, Psychic, Ice, Dragon, Dark, Fairy. For the Pokédex
/// (2026-11-11, user: "los tipos que afectan al Pokémon, ×2 ×4 ×0.5 ×0 ×1 ×0.25"): types only — an ability such as
/// Levitate or Thick Fat is not counted.
/// </summary>
public static class TypeMatchups
{
    public const int TypeCount = 18;

    private const int Normal = 0, Fighting = 1, Flying = 2, Poison = 3, Ground = 4, Rock = 5, Bug = 6, Ghost = 7, Steel = 8,
        Fire = 9, Water = 10, Grass = 11, Electric = 12, Psychic = 13, Ice = 14, Dragon = 15, Dark = 16, Fairy = 17;

    /// <summary>Attacking type → what it does against each defending type that is not ×1.</summary>
    private static readonly Dictionary<int, (int[] Double, int[] Half, int[] None)> Chart = new()
    {
        [Normal] = ([], [Rock, Steel], [Ghost]),
        [Fighting] = ([Normal, Rock, Steel, Ice, Dark], [Flying, Poison, Bug, Psychic, Fairy], [Ghost]),
        [Flying] = ([Fighting, Bug, Grass], [Rock, Steel, Electric], []),
        [Poison] = ([Grass, Fairy], [Poison, Ground, Rock, Ghost], [Steel]),
        [Ground] = ([Poison, Rock, Steel, Fire, Electric], [Bug, Grass], [Flying]),
        [Rock] = ([Flying, Bug, Fire, Ice], [Fighting, Ground, Steel], []),
        [Bug] = ([Grass, Psychic, Dark], [Fighting, Flying, Poison, Ghost, Steel, Fire, Fairy], []),
        [Ghost] = ([Ghost, Psychic], [Dark], [Normal]),
        [Steel] = ([Rock, Ice, Fairy], [Steel, Fire, Water, Electric], []),
        [Fire] = ([Bug, Steel, Grass, Ice], [Rock, Fire, Water, Dragon], []),
        [Water] = ([Ground, Rock, Fire], [Water, Grass, Dragon], []),
        [Grass] = ([Ground, Rock, Water], [Flying, Poison, Bug, Steel, Fire, Grass, Dragon], []),
        [Electric] = ([Flying, Water], [Grass, Electric, Dragon], [Ground]),
        [Psychic] = ([Fighting, Poison], [Steel, Psychic], [Dark]),
        [Ice] = ([Flying, Ground, Grass, Dragon], [Steel, Fire, Water, Ice], []),
        [Dragon] = ([Dragon], [Steel], [Fairy]),
        [Dark] = ([Ghost, Psychic], [Fighting, Dark, Fairy], []),
        [Fairy] = ([Fighting, Dragon, Dark], [Poison, Steel, Fire], []),
    };

    /// <summary>What a move of type <paramref name="attack"/> does to a Pokémon of type <paramref name="defend"/>.</summary>
    public static double Against(int attack, int defend)
    {
        if (!Chart.TryGetValue(attack, out var row))
            return 1;
        return row.None.Contains(defend) ? 0 : row.Double.Contains(defend) ? 2 : row.Half.Contains(defend) ? 0.5 : 1;
    }

    /// <summary>
    /// What a move of type <paramref name="attack"/> does to a Pokémon of <paramref name="types"/> (one or two; a
    /// repeated type counts once): the product, so 4, 2, 1, 0.5, 0.25 or 0.
    /// </summary>
    public static double Against(int attack, IEnumerable<int> types) =>
        types.Distinct().Aggregate(1.0, (product, type) => product * Against(attack, type));

    /// <summary>Every attacking type grouped by what it does to <paramref name="types"/>, strongest first.</summary>
    public static IReadOnlyList<(double Multiplier, IReadOnlyList<int> Types)> Groups(IEnumerable<int> types)
    {
        int[] defending = [.. types.Distinct()];
        return [.. Enumerable.Range(0, TypeCount)
            .GroupBy(attack => Against(attack, defending))
            .OrderByDescending(g => g.Key == 0 ? -1 : g.Key)
            .Select(g => (g.Key, (IReadOnlyList<int>)[.. g]))];
    }
}
