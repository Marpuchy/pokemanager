using Pokemanager.Model.Dump;

namespace Pokemanager.Model.Data;

/// <summary>
/// What every row of the personal (and learnset) table means: a species, or a form of one. The row number is **not**
/// the same thing in two games.
/// </summary>
/// <remarks>
/// **Measured on the real games (2026-09-21)**: row 760 is Mega Venusaur in Pokémon X and **Bewear** in Ultra Moon,
/// row 722 is a Deoxys form in X and **Rowlet** in Ultra Moon. A file imported row by row therefore made Mega Venusaur
/// Normal/Fighting, which is what the user saw in game. The rows of a game are read from its own table: the species
/// come first, and each entry says where its forms start (<c>FormStatsIndex</c>) and how many it has.
/// </remarks>
public sealed class PersonalIndex
{
    private readonly Dictionary<(int Species, int Form), int> byPokemon = [];

    /// <summary>Row → the species and form it holds. Rows the game does not use are (0, 0).</summary>
    public IReadOnlyList<(int Species, int Form)> Rows { get; }

    /// <summary>The last species of the game (the rows above the species are forms).</summary>
    public static int LastSpecies(GameTitle title) => title switch
    {
        GameTitle.X or GameTitle.Y or GameTitle.OmegaRuby or GameTitle.AlphaSapphire => 721,
        GameTitle.Sun or GameTitle.Moon => 802,
        _ => 807,
    };

    private PersonalIndex((int, int)[] rows)
    {
        Rows = rows;
        for (int id = 0; id < rows.Length; id++)
        {
            var key = rows[id];
            if (key != (0, 0))
                byPokemon.TryAdd(key, id);
        }
    }

    public static PersonalIndex Of(GameData data)
    {
        var rows = new (int, int)[data.Personal.Length];
        int last = Math.Min(LastSpecies(data.Title), rows.Length - 1);
        for (int species = 1; species <= last; species++)
        {
            rows[species] = (species, 0);
            var entry = data.Personal[species];
            for (int form = 1; form < entry.FormeCount; form++)
            {
                int id = entry.FormStatsIndex + form - 1;
                if (entry.FormStatsIndex > 0 && id < rows.Length && rows[id] == (0, 0))
                    rows[id] = (species, form);
            }
        }
        return new PersonalIndex(rows);
    }

    /// <summary>The row this game keeps that Pokémon in, or -1 when the game does not have it.</summary>
    public int Row(int species, int form) => byPokemon.TryGetValue((species, form), out int id) ? id : -1;

    /// <summary>What a row holds, or null when it is one the game does not use.</summary>
    public (int Species, int Form)? Pokemon(int id) =>
        (uint)id < Rows.Count && Rows[id] != (0, 0) ? Rows[id] : null;
}
