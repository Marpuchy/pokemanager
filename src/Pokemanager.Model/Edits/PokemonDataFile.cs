using System.Text.Json;
using System.Text.Json.Nodes;
using Pokemanager.Model.Data;
using Pokemanager.Model.Dump;
using Pokemanager.Model.Editing;
using Pokemanager.Model.Resources;

namespace Pokemanager.Model.Edits;

/// <summary>What a <see cref="PokemonDataFile"/> contains.</summary>
public enum PokemonDataScope
{
    /// <summary>Only the manual edits: applied on another project, the rest keeps that project's randomization.</summary>
    Edits,

    /// <summary>Every value of every Pokémon, learnset and move: applied on another project, it pins all of them.</summary>
    All,
}

/// <summary>Which data a <see cref="PokemonDataFile"/> holds.</summary>
public enum PokemonDataKind
{
    /// <summary>Pokémon and moves (files made before the two were exported apart).</summary>
    All,

    /// <summary>Pokémon: base stats, types, abilities and the rest of the personal data, and learnsets.</summary>
    Pokemon,

    /// <summary>Moves: type, category, power, accuracy, PP, priority…</summary>
    Moves,

    /// <summary>Trainers: their record (AI, battle type, money, bag) and their teams.</summary>
    Trainers,
}

/// <summary>Result of <see cref="PokemonDataFile.ApplyTo"/>.</summary>
/// <param name="Applied">Values that now differ from the base and are stored as edits.</param>
/// <param name="SameAsBase">Values equal to the base, so no edit was needed.</param>
/// <param name="Skipped">Values that do not fit this game's tables (unknown field, id out of range…).</param>
public sealed record PokemonDataImport(int Applied, int SameAsBase, IReadOnlyList<string> Skipped, bool OtherGame);

/// <summary>
/// Shareable file with Pokémon data (base stats, types, abilities, learnsets, moves…), the counterpart of a UPR
/// <c>.rnqs</c> for the data the advanced editor changes. It holds values, not bytes of the game.
/// </summary>
/// <remarks>
/// Values are addressed like edits: <c>table → id → field → value</c>. Importing sets each value through the editor
/// session, so values equal to the project's base do not become edits.
/// </remarks>
public sealed class PokemonDataFile
{
    /// <summary>Pokémon data (and older files with Pokémon and moves).</summary>
    public const string Extension = "pkdata";

    /// <summary>Move data: another extension so the two files are not mixed up.</summary>
    public const string MovesExtension = "mvdata";

    /// <summary>Trainer data: the record and the team of every trainer.</summary>
    public const string TrainersExtension = "trdata";

    public static string ExtensionOf(PokemonDataKind kind) => kind switch
    {
        PokemonDataKind.Moves => MovesExtension,
        PokemonDataKind.Trainers => TrainersExtension,
        _ => Extension,
    };
    public const int CurrentFormat = 1;
    private const string Magic = "pokemanager-pokemon-data";

    public PokemonDataScope Scope { get; init; }
    public PokemonDataKind Kind { get; init; }
    public GameTitle? Game { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.Now;

    /// <summary>Free text shown when importing (for example the seed and preset the data came from).</summary>
    public string? Description { get; init; }

    /// <summary>
    /// What Pokémon each row of the personal and learnset tables holds, as species and form. **The row number means
    /// another Pokémon in another game** (760 is Mega Venusaur in X and Bewear in Ultra Moon), so without this the data
    /// lands on the wrong entries.
    /// </summary>
    public SortedDictionary<int, int[]> Rows { get; } = [];

    /// <summary>table → id → field → value.</summary>
    public SortedDictionary<string, SortedDictionary<int, SortedDictionary<string, JsonNode>>> Tables { get; } = new(StringComparer.Ordinal);

    public int ValueCount => Tables.Values.Sum(t => t.Values.Sum(e => e.Count));

    public void Add(string table, int id, string field, JsonNode value)
    {
        if (!Tables.TryGetValue(table, out var entries))
            Tables[table] = entries = [];
        if (!entries.TryGetValue(id, out var fields))
            entries[id] = fields = new SortedDictionary<string, JsonNode>(StringComparer.Ordinal);
        fields[field] = value.DeepClone();
    }

    public IEnumerable<(string Table, int Id, string Field, JsonNode Value)> Values() =>
        from t in Tables
        from e in t.Value
        from f in e.Value
        select (t.Key, e.Key, f.Key, f.Value);

    /// <summary>Tables of the editor that hold each kind of data.</summary>
    public static IReadOnlyList<string> TablesOf(PokemonDataKind kind) => kind switch
    {
        PokemonDataKind.Pokemon => [GameTables.Personal, GameTables.Learnsets],
        PokemonDataKind.Moves => [GameTables.Moves, GameTables.MoveTexts],
        PokemonDataKind.Trainers => [GameTables.Trainers],
        _ => [.. GameTables.All.Select(t => t.Name)],
    };

    /// <summary>Tables whose row number is a Pokémon of the game, not a national id.</summary>
    private static bool ByPokemon(string table) => table is GameTables.Personal or GameTables.Learnsets;

    /// <summary>Records what Pokémon every exported row holds, so another game can be given the same data.</summary>
    private void AddRows(EditorSession session)
    {
        var index = PersonalIndex.Of(session.Current);
        foreach (var (table, id, _, _) in Values().Where(v => ByPokemon(v.Table)))
            if (index.Pokemon(id) is { } mon)
                Rows[id] = [mon.Species, mon.Form];
    }

    /// <summary>The session's manual edits of that kind of data.</summary>
    public static PokemonDataFile FromEdits(EditorSession session, GameTitle game, string? description = null,
        PokemonDataKind kind = PokemonDataKind.All)
    {
        var file = new PokemonDataFile { Scope = PokemonDataScope.Edits, Kind = kind, Game = game, Description = description };
        var tables = TablesOf(kind);
        foreach (var edit in session.Project.Edits.All.Where(e => tables.Contains(e.Table)))
            file.Add(edit.Table, edit.Id, edit.Field, edit.Value);
        file.AddRows(session);
        return file;
    }

    /// <summary>Every current value (base + edits) of the tables of that kind of data.</summary>
    public static PokemonDataFile FromCurrent(EditorSession session, GameTitle game, string? description = null,
        PokemonDataKind kind = PokemonDataKind.All)
    {
        var file = new PokemonDataFile { Scope = PokemonDataScope.All, Kind = kind, Game = game, Description = description };
        var tables = TablesOf(kind);
        foreach (var table in GameTables.All.Where(t => tables.Contains(t.Name)))
        {
            int count = table.Count(session.Current);
            for (int id = 0; id < count; id++)
                foreach (string field in table.Fields)
                    file.Add(table.Name, id, field, table.Get(session.Current, id, field));
        }
        file.AddRows(session);
        return file;
    }

    /// <summary>Sets the file's values in the session.</summary>
    /// <param name="replaceEdits">
    /// Undo the session's current edits of this file's kind of data first (a moves file leaves the Pokémon edits alone), so
    /// that data is the base plus this file only.
    /// </param>
    public PokemonDataImport ApplyTo(EditorSession session, GameTitle game, bool replaceEdits)
    {
        if (replaceEdits)
        {
            var tables = TablesOf(Kind);
            session.RevertAll(table => tables.Contains(table));
        }

        int applied = 0, same = 0;
        var skipped = new List<string>();
        var limits = DataLimits.Of(session.Original);
        var index = PersonalIndex.Of(session.Original);
        bool otherGame = Game is { } from && from != game;
        foreach (var (table, id, field, value) in Values())
        {
            try
            {
                // The row number is a Pokémon of the game it came from, so it is translated by species and form: what a
                // file calls 760 is Mega Venusaur in Pokémon X and Bewear in Ultra Moon.
                int target = id;
                if (ByPokemon(table) && !Row(id, index, game, out target))
                {
                    skipped.Add($"{table}[{id}].{field}: {Missing(id)}");
                    continue;
                }

                // A trainer number is a different trainer in another game, and so is its class.
                if (otherGame && table == GameTables.Trainers)
                {
                    skipped.Add($"{table}[{id}].{field}: {Strings.Data_OtherGameTrainers}");
                    continue;
                }

                // A file from another game names things this one does not have (Ultra Sun's Pokémon in Pokémon X, a
                // move or an ability that does not exist here). Those are skipped, not written as a number the game
                // cannot look up.
                if (!limits.Accepts(table, field, value, out string reason))
                {
                    skipped.Add($"{table}[{id}].{field}: {reason}");
                    continue;
                }

                session.Set(table, target, field, value);
                if (session.IsModified(table, target, field))
                    applied++;
                else
                    same++;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException
                                           or NullReferenceException or IndexOutOfRangeException or KeyNotFoundException
                                           or OverflowException or JsonException)
            {
                // One line: the report is shown in the status bar, and an out-of-range message carries a second line.
                skipped.Add($"{table}[{id}].{field}: {ex.Message.ReplaceLineEndings(" ")}");
            }
        }
        return new PokemonDataImport(applied, same, skipped, Game is { } g && g != game);
    }

    /// <summary>
    /// Where this row goes in the game being imported into. With the Pokémon the file records, by species and form;
    /// without it (a file made before this was written) only rows that are species in both games are used, because a
    /// row above them holds a form of a different Pokémon in each game.
    /// </summary>
    private bool Row(int id, PersonalIndex index, GameTitle game, out int target)
    {
        target = id;
        if (Rows.TryGetValue(id, out int[]? mon) && mon.Length == 2)
        {
            target = index.Row(mon[0], mon[1]);
            return target >= 0;
        }
        if (Game is not { } from || from == game)
            return true;
        int shared = Math.Min(PersonalIndex.LastSpecies(from), PersonalIndex.LastSpecies(game));
        return id <= shared && index.Pokemon(id) is { Form: 0 };
    }

    private string Missing(int id) =>
        Rows.TryGetValue(id, out int[]? mon) && mon.Length == 2
            ? string.Format(Strings.Data_MissingPokemon, mon[0], mon[1])
            : string.Format(Strings.Data_RowMeansAnother, id);

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public void Save(string path)
    {
        var tables = new JsonObject();
        foreach (var (table, entries) in Tables)
        {
            var byId = new JsonObject();
            foreach (var (id, fields) in entries)
            {
                var values = new JsonObject();
                foreach (var (field, value) in fields)
                    values[field] = value.DeepClone();
                byId[id.ToString()] = values;
            }
            tables[table] = byId;
        }

        var rows = new JsonObject();
        foreach (var (id, mon) in Rows)
            rows[id.ToString()] = new JsonArray(mon[0], mon[1]);

        var root = new JsonObject
        {
            ["magic"] = Magic,
            ["format"] = CurrentFormat,
            ["scope"] = Scope.ToString(),
            ["kind"] = Kind.ToString(),
            ["game"] = Game?.ToString(),
            ["createdAt"] = CreatedAt,
            ["description"] = Description,
            ["rows"] = rows,
            ["tables"] = tables,
        };

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, root.ToJsonString(JsonOptions));
        File.Move(tmp, path, overwrite: true);
    }

    /// <exception cref="InvalidDataException">Not a Pokemanager data file, or a newer format.</exception>
    public static PokemonDataFile Load(string path)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? throw new InvalidDataException(string.Format(Strings.Data_NotDataFile, path));
        }
        catch (JsonException)
        {
            throw new InvalidDataException(string.Format(Strings.Data_NotDataFile, path));
        }

        if (root["magic"]?.GetValue<string>() != Magic)
            throw new InvalidDataException(string.Format(Strings.Data_NotDataFile, path));
        int format = root["format"]?.GetValue<int>() ?? 0;
        if (format > CurrentFormat)
            throw new InvalidDataException(string.Format(Strings.Data_NewerFormat, format, CurrentFormat));

        var file = new PokemonDataFile
        {
            Scope = Enum.TryParse<PokemonDataScope>(root["scope"]?.GetValue<string>(), out var scope) ? scope : PokemonDataScope.Edits,
            Kind = Enum.TryParse<PokemonDataKind>(root["kind"]?.GetValue<string>(), out var kind) ? kind : PokemonDataKind.All,
            Game = Enum.TryParse<GameTitle>(root["game"]?.GetValue<string>(), out var game) ? game : null,
            CreatedAt = root["createdAt"]?.GetValue<DateTime>() ?? File.GetLastWriteTime(path),
            Description = root["description"]?.GetValue<string>(),
        };

        if (root["rows"] is JsonObject rows)
        {
            foreach (var (idText, mon) in rows)
                if (int.TryParse(idText, out int id) && mon is JsonArray { Count: 2 } pair)
                    file.Rows[id] = [pair[0]!.GetValue<int>(), pair[1]!.GetValue<int>()];
        }

        if (root["tables"] is JsonObject tables)
        {
            foreach (var (table, entries) in tables)
            {
                if (entries is not JsonObject byId)
                    continue;
                foreach (var (idText, fields) in byId)
                {
                    if (!int.TryParse(idText, out int id) || fields is not JsonObject values)
                        continue;
                    foreach (var (field, value) in values)
                        if (value is not null)
                            file.Add(table, id, field, value);
                }
            }
        }
        return file;
    }
}
