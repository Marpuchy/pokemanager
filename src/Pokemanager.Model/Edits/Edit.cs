using System.Text.Json.Nodes;

namespace Pokemanager.Model.Edits;

/// <summary>
/// Una edición manual: el campo <see cref="Field"/> de la entrada <see cref="Id"/> de la tabla
/// <see cref="Table"/> vale <see cref="Value"/>. Es neutra respecto al formato en disco.
/// </summary>
public sealed record Edit(string Table, int Id, string Field, JsonNode Value)
{
    public EditKey Key => new(Table, Id, Field);
}

public readonly record struct EditKey(string Table, int Id, string Field);
