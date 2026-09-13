using System.Text.Json.Nodes;

namespace Pokemanager.Model.Edits;

/// <summary>Set of edits, at most one per (table, id, field). Keeps insertion order.</summary>
public sealed class EditSet
{
    private readonly Dictionary<EditKey, Edit> edits = [];
    private readonly List<EditKey> order = [];

    public int Count => edits.Count;

    public IEnumerable<Edit> All => order.Select(k => edits[k]);

    public void Set(string table, int id, string field, JsonNode value)
    {
        var key = new EditKey(table, id, field);
        if (!edits.ContainsKey(key))
            order.Add(key);
        edits[key] = new Edit(table, id, field, value.DeepClone());
    }

    public bool Remove(EditKey key)
    {
        if (!edits.Remove(key))
            return false;
        order.Remove(key);
        return true;
    }

    public bool Contains(EditKey key) => edits.ContainsKey(key);

    public bool Any(string table, int id) => edits.Keys.Any(k => k.Table == table && k.Id == id);

    public IEnumerable<string> Tables => edits.Keys.Select(k => k.Table).Distinct();

    public void Clear()
    {
        edits.Clear();
        order.Clear();
    }
}
