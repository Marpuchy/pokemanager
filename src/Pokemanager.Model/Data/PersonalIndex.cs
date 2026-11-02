using pk3DS.Core.Structures.PersonalInfo;
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

    /// <summary>
    /// The last species the game ships with (the rows above the species are forms). This is the constant: it is the
    /// answer for a game whose ROM cannot be inspected (a data file exported from another game names only its title),
    /// the fallback of <see cref="DeriveLastSpecies"/> and what tells an expanded ROM from a stock one. The value a
    /// loaded ROM actually has is <see cref="GameData.LastSpecies"/>.
    /// </summary>
    public static int LastSpecies(GameTitle title) => title switch
    {
        GameTitle.X or GameTitle.Y or GameTitle.OmegaRuby or GameTitle.AlphaSapphire => 721,
        GameTitle.Sun or GameTitle.Moon => 802,
        _ => 807,
    };

    /// <summary>
    /// The last species of a personal table, read from the table itself instead of from the constant, so a ROM with
    /// more species than the game ships with (the USUM Expansion Mod adds the Pokémon of Generations 8 and 9) is not
    /// cut short.
    /// </summary>
    /// <remarks>
    /// The species come first and their alternate forms after all of them, so the lowest row any entry points at with
    /// <c>FormStatsIndex</c> is the first form row, and the species end just below it. Only rows still believed to be
    /// species are read — the search stops where the forms begin — and a pointer that does not point forward is
    /// ignored, so a form entry that carries a copy of its species' pointer cannot move the boundary.
    /// <para>
    /// A table with no alternate forms at all, or one whose pointers are out of range, says nothing about where the
    /// species end: that falls back to <see cref="LastSpecies"/>, clamped to the rows that are there.
    /// </para>
    /// </remarks>
    /// <param name="personal">The game's personal entries, row 0 included and without the final index file.</param>
    /// <param name="title">The game, for the fallback.</param>
    public static int DeriveLastSpecies(IReadOnlyList<PersonalInfoXY> personal, GameTitle title)
    {
        int firstForm = personal.Count;
        try
        {
            // The bound tightens as a lower pointer is found, so no row above the forms' start is ever read.
            for (int row = 1; row < firstForm; row++)
            {
                int pointer = personal[row].FormStatsIndex;
                if (pointer > row && pointer < firstForm)
                    firstForm = pointer;
            }
        }
        catch (ArgumentNullException)
        {
            // An entry of a size this application does not know keeps no data at all (pk3DS's readers leave it
            // empty): there is nothing to read, so the constant answers, as it does for a table with no forms.
            firstForm = personal.Count;
        }
        return firstForm > 1 && firstForm < personal.Count
            ? firstForm - 1
            : Math.Min(LastSpecies(title), Math.Max(personal.Count - 1, 0));
    }

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
        int last = Math.Min(data.LastSpecies, rows.Length - 1);
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
