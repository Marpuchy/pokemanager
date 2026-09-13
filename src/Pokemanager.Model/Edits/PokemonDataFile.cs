using System.Text.Json;
using System.Text.Json.Nodes;
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
    public const string Extension = "pkdata";
    public const int CurrentFormat = 1;
    private const string Magic = "pokemanager-pokemon-data";

    public PokemonDataScope Scope { get; init; }
    public GameTitle? Game { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.Now;

    /// <summary>Free text shown when importing (for example the seed and preset the data came from).</summary>
    public string? Description { get; init; }

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

    /// <summary>The session's manual edits.</summary>
    public static PokemonDataFile FromEdits(EditorSession session, GameTitle game, string? description = null)
    {
        var file = new PokemonDataFile { Scope = PokemonDataScope.Edits, Game = game, Description = description };
        foreach (var edit in session.Project.Edits.All)
            file.Add(edit.Table, edit.Id, edit.Field, edit.Value);
        return file;
    }

    /// <summary>Every current value (base + edits) of every table the editor knows.</summary>
    public static PokemonDataFile FromCurrent(EditorSession session, GameTitle game, string? description = null)
    {
        var file = new PokemonDataFile { Scope = PokemonDataScope.All, Game = game, Description = description };
        foreach (var table in GameTables.All)
        {
            int count = table.Count(session.Current);
            for (int id = 0; id < count; id++)
                foreach (string field in table.Fields)
                    file.Add(table.Name, id, field, table.Get(session.Current, id, field));
        }
        return file;
    }

    /// <summary>Sets the file's values in the session.</summary>
    /// <param name="replaceEdits">Undo the session's current edits first, so the result is the base plus this file only.</param>
    public PokemonDataImport ApplyTo(EditorSession session, GameTitle game, bool replaceEdits)
    {
        if (replaceEdits)
            session.RevertAll();

        int applied = 0, same = 0;
        var skipped = new List<string>();
        foreach (var (table, id, field, value) in Values())
        {
            try
            {
                session.Set(table, id, field, value);
                if (session.IsModified(table, id, field))
                    applied++;
                else
                    same++;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException or NullReferenceException)
            {
                skipped.Add($"{table}[{id}].{field}: {ex.Message}");
            }
        }
        return new PokemonDataImport(applied, same, skipped, Game is { } g && g != game);
    }

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

        var root = new JsonObject
        {
            ["magic"] = Magic,
            ["format"] = CurrentFormat,
            ["scope"] = Scope.ToString(),
            ["game"] = Game?.ToString(),
            ["createdAt"] = CreatedAt,
            ["description"] = Description,
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
            Game = Enum.TryParse<GameTitle>(root["game"]?.GetValue<string>(), out var game) ? game : null,
            CreatedAt = root["createdAt"]?.GetValue<DateTime>() ?? File.GetLastWriteTime(path),
            Description = root["description"]?.GetValue<string>(),
        };

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
