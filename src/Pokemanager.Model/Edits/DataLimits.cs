using System.Text.Json.Nodes;
using Pokemanager.Model.Data;
using Pokemanager.Model.Dump;

namespace Pokemanager.Model.Edits;

/// <summary>
/// What a game can name: how many species, moves, items and abilities it has. A data file exported from another game
/// carries ids this one has no entry for — Ultra Sun's Pokémon in Pokémon X, a move or an ability that does not exist
/// here — and writing them would put a number in the ROM that the game cannot look up.
/// </summary>
/// <remarks>
/// The entry counts come from the game's own tables, so a randomized ROM is measured by what it really holds; the
/// ability count is the game's, since nothing in the data read here lists the abilities.
/// </remarks>
public sealed record DataLimits(int Species, int Moves, int Items, int Abilities)
{
    /// <summary>Types, egg groups, growth rates and natures: the same in both generations.</summary>
    private const int Types = 17;
    private const int EggGroups = 15;
    private const int Growths = 5;
    private const int Natures = 24;

    public static DataLimits Of(GameData data) => new(
        Species: Last(data.Personal.Length),
        Moves: Last(data.Moves.Length),
        Items: Last(data.Items.Length),
        Abilities: data.Title switch
        {
            GameTitle.X or GameTitle.Y => 188,
            GameTitle.OmegaRuby or GameTitle.AlphaSapphire => 191,
            GameTitle.Sun or GameTitle.Moon => 232,
            _ => 233,
        });

    /// <summary>
    /// Whether a value can be written to this game. Only the fields that name something are checked: a base stat or a
    /// price is a number and any number is a number, but an ability, a move, an item or a species is a row of a table.
    /// </summary>
    /// <param name="reason">What does not exist here, for the report of what was skipped.</param>
    public bool Accepts(string table, string field, JsonNode value, out string reason)
    {
        reason = "";
        if (table == GameTables.Learnsets)
        {
            foreach (var pair in value as JsonArray ?? [])
            {
                if (pair is not JsonArray { Count: 2 } p || p[1] is null)
                    continue;
                if (!Fits(p[1]!.GetValue<int>(), Moves, "move", out reason))
                    return false;
            }
            return true;
        }

        if (value is not JsonValue || value.GetValueKind() is not System.Text.Json.JsonValueKind.Number)
            return true;
        int number = value.GetValue<int>();
        string name = table == GameTables.Trainers && field.Contains('.') ? field[3..] : field;
        return name switch
        {
            "ability1" or "ability2" or "abilityHidden" => Fits(number, Abilities, "ability", out reason),
            "item1" or "item2" or "item3" or "item4" or "item" => Fits(number, Items, "item", out reason),
            "species" => Fits(number, Species, "species", out reason),
            "move" or "move1" or "move2" or "move3" or "move4" => Fits(number, Moves, "move", out reason),
            "type" or "type1" or "type2" => Fits(number, Types, "type", out reason),
            "eggGroup1" or "eggGroup2" => Fits(number, EggGroups, "egg group", out reason),
            "expGrowth" => Fits(number, Growths, "growth rate", out reason),
            "nature" => Fits(number, Natures, "nature", out reason),
            GameTables.ZCrystal => Fits(number, Items, "item", out reason),
            GameTables.ZBaseMove or GameTables.ZMove => Fits(number, Moves, "move", out reason),
            _ => true,
        };
    }

    /// <summary>The last id of a table. A table this game folder does not carry sets no limit: nothing is skipped by it.</summary>
    private static int Last(int count) => count > 0 ? count - 1 : int.MaxValue;

    /// <summary>
    /// A level-up list without the moves this game does not have. Dropping the whole list for one move would leave that
    /// Pokémon with the learnset of the ROM instead of the one being imported, so only the missing moves go.
    /// </summary>
    /// <returns>How many moves were left out.</returns>
    public int TrimLearnset(JsonNode value, out JsonNode trimmed)
    {
        var kept = new JsonArray();
        int dropped = 0;
        foreach (var pair in value as JsonArray ?? [])
        {
            if (pair is not JsonArray { Count: 2 } p || p[0] is null || p[1] is null)
                continue;
            int level = p[0]!.GetValue<int>(), move = p[1]!.GetValue<int>();
            if (move > Moves || move < 0)
                dropped++;
            else
                kept.Add(new JsonArray(level, move));
        }
        trimmed = kept;
        return dropped;
    }

    private static bool Fits(int value, int max, string what, out string reason)
    {
        reason = value >= 0 && value <= max ? "" : $"{what} {value} (this game has up to {max})";
        return reason.Length == 0;
    }
}
