using System.Text.Json.Nodes;

namespace Pokemanager.Model.Edits;

/// <summary>
/// A manual edit: field <see cref="Field"/> of entry <see cref="Id"/> in table <see cref="Table"/> has value
/// <see cref="Value"/>. Independent of the on-disk format.
/// </summary>
public sealed record Edit(string Table, int Id, string Field, JsonNode Value)
{
    public EditKey Key => new(Table, Id, Field);
}

public readonly record struct EditKey(string Table, int Id, string Field);
