using System.Text.Json.Nodes;
using Pokemanager.Model.Data;
using Pokemanager.Model.Edits;
using Pokemanager.Model.Projects;

namespace Pokemanager.Model.Editing;

/// <summary>
/// Estado de edición: los datos originales del volcado, los datos actuales (original + ediciones)
/// y el proyecto donde se registran las ediciones.
/// </summary>
/// <remarks>
/// Cambiar un valor al que ya tenía el original elimina la edición en lugar de guardarla, así el
/// proyecto solo contiene diferencias reales.
/// </remarks>
public sealed class EditorSession
{
    public Project Project { get; }

    /// <summary>Datos tal como están en el volcado. No se modifican nunca.</summary>
    public GameData Original { get; }

    /// <summary>Datos con las ediciones aplicadas.</summary>
    public GameData Current { get; }

    public event EventHandler<EditKey>? Changed;

    private EditorSession(Project project, GameData original, GameData current)
    {
        Project = project;
        Original = original;
        Current = current;
    }

    /// <summary>Carga el romfs dos veces (original y actual) y aplica las ediciones del proyecto.</summary>
    /// <exception cref="InvalidDataException">Alguna edición del proyecto no encaja en las tablas.</exception>
    public static EditorSession Open(Project project)
    {
        var session = new EditorSession(project, GameData.Load(project.RomFsPath), GameData.Load(project.RomFsPath));
        var errors = new List<string>();
        foreach (var edit in project.Edits.All)
        {
            try
            {
                GameTables.Get(edit.Table).Set(session.Current, edit.Id, edit.Field, edit.Value);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException)
            {
                errors.Add($"{edit.Table}[{edit.Id}].{edit.Field}: {ex.Message}");
            }
        }

        if (errors.Count > 0)
            throw new InvalidDataException("Ediciones no válidas en el proyecto:" + Environment.NewLine + string.Join(Environment.NewLine, errors));
        return session;
    }

    public JsonNode Get(string table, int id, string field) => GameTables.Get(table).Get(Current, id, field);

    public JsonNode GetOriginal(string table, int id, string field) => GameTables.Get(table).Get(Original, id, field);

    public int GetInt(string table, int id, string field) => Get(table, id, field).GetValue<int>();

    public void Set(string table, int id, string field, JsonNode value)
    {
        var def = GameTables.Get(table);
        var key = new EditKey(table, id, field);

        def.Set(Current, id, field, value);
        // Se relee el valor ya aplicado: normaliza (p. ej. learnset ordenado, bytes truncados).
        JsonNode applied = def.Get(Current, id, field);

        if (JsonNode.DeepEquals(applied, def.Get(Original, id, field)))
            Project.Edits.Remove(key);
        else
            Project.Edits.Set(table, id, field, applied);

        Changed?.Invoke(this, key);
    }

    public void SetInt(string table, int id, string field, int value) => Set(table, id, field, JsonValue.Create(value));

    public bool IsModified(string table, int id, string field) => Project.Edits.Contains(new EditKey(table, id, field));

    public bool IsModified(string table, int id) => Project.Edits.Any(table, id);

    /// <summary>Deshace todas las ediciones de una entrada.</summary>
    public void Revert(string table, int id)
    {
        var def = GameTables.Get(table);
        foreach (string field in def.Fields)
        {
            if (IsModified(table, id, field))
                Set(table, id, field, def.Get(Original, id, field));
        }
    }
}
