using Pokemanager.App.Resources;
using Pokemanager.Model.Edits;

namespace Pokemanager.App.Services;

/// <summary>
/// What an entry has changed, in the words the player uses. A plain "Modified" tag says nothing: an entry whose stats
/// are the ROM's but whose abilities were imported from another ROM looked exactly like one with new stats.
/// </summary>
public static class EditSummary
{
    /// <summary>The kinds, in the order they are shown: the first one is what the tag says.</summary>
    private static readonly string[] Order =
    [
        Strings.Modified_Stats, Strings.Modified_Types, Strings.Modified_Abilities, Strings.Modified_Items,
        Strings.Modified_Ev, Strings.Modified_Moves, Strings.Modified_Text, Strings.Modified_Ai,
        Strings.Modified_Team, Strings.Modified_Data, Strings.Modified_Other,
    ];

    /// <summary>Which kind of change a field of a table is.</summary>
    public static string Kind(string table, string field) => table switch
    {
        GameTables.Learnsets => Strings.Modified_Moves,
        GameTables.MoveTexts => Strings.Modified_Text,
        GameTables.Trainers => field.Contains('.') ? Strings.Modified_Team
            : field is "ai" ? Strings.Modified_Ai
            : Strings.Modified_Data,
        GameTables.Moves => field is "type" ? Strings.Modified_Types : Strings.Modified_Data,
        _ => field switch
        {
            "hp" or "atk" or "def" or "spa" or "spd" or "spe" => Strings.Modified_Stats,
            "type1" or "type2" => Strings.Modified_Types,
            "ability1" or "ability2" or "abilityHidden" => Strings.Modified_Abilities,
            "item1" or "item2" or "item3" => Strings.Modified_Items,
            "zCrystal" or "zBaseMove" or "zMove" => Strings.Modified_Moves,
            _ when field.StartsWith("ev", StringComparison.Ordinal) => Strings.Modified_Ev,
            _ => Strings.Modified_Other,
        },
    };

    /// <summary>The kinds an entry has, most telling first.</summary>
    public static IReadOnlyList<string> Kinds(IEnumerable<(string Table, string Field)> changes) =>
        [.. changes.Select(c => Kind(c.Table, c.Field)).Distinct().OrderBy(k => Array.IndexOf(Order, k))];

    /// <summary>
    /// What the tag says: the first kind, with an ellipsis when there is more. It stays one word because the tag shares
    /// the row with the name, and the tooltip has the whole list anyway.
    /// </summary>
    public static string Tag(IReadOnlyList<string> kinds) => kinds.Count switch
    {
        0 => "",
        1 => kinds[0],
        _ => string.Format(Strings.Modified_AndMore, kinds[0]),
    };
}
