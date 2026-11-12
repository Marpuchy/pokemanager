namespace Pokemanager.Model.Projects;

/// <summary>
/// What the player changed in the encounters tab (2026-11-12): over the ROM as the randomizer left it, applied at build.
/// A Pokémon is written <c>"species:form"</c>. A change made over one randomization that the next one no longer has
/// (the route has other Pokémon now) simply finds nothing to change.
/// </summary>
public sealed class EncounterEdits
{
    /// <summary>Area → the Pokémon of that area → the one it becomes, in every slot of the area.</summary>
    public Dictionary<int, Dictionary<string, string>> Wild { get; set; } = [];

    /// <summary>Area → levels added to every table of the area (negative takes them off).</summary>
    public Dictionary<int, int> WildLevels { get; set; } = [];

    /// <summary>Gift (starters first) → the Pokémon given.</summary>
    public Dictionary<int, string> Gifts { get; set; } = [];

    /// <summary>Fixed encounter (legendaries, totems, allies) → the Pokémon met.</summary>
    public Dictionary<int, string> Statics { get; set; } = [];

    /// <summary>In-game trade → the Pokémon received.</summary>
    public Dictionary<int, string> Trades { get; set; } = [];

    public bool IsEmpty => Wild.Count == 0 && WildLevels.Count == 0 && Gifts.Count == 0 && Statics.Count == 0 && Trades.Count == 0;

    public static string Key(int species, int form) => $"{species}:{form}";

    public static (int Species, int Form) Parse(string value)
    {
        var parts = value.Split(':');
        return (int.Parse(parts[0]), parts.Length > 1 ? int.Parse(parts[1]) : 0);
    }

    public EncounterEdits Clone() => new()
    {
        Wild = Wild.ToDictionary(p => p.Key, p => new Dictionary<string, string>(p.Value)),
        WildLevels = new(WildLevels),
        Gifts = new(Gifts),
        Statics = new(Statics),
        Trades = new(Trades),
    };

    /// <summary>A stable text of every change, for the history's fingerprint.</summary>
    public string Fingerprint() =>
        string.Join(';', Wild.OrderBy(p => p.Key).SelectMany(p => p.Value.OrderBy(s => s.Key, StringComparer.Ordinal).Select(s => $"w{p.Key}:{s.Key}>{s.Value}")))
        + "|" + string.Join(';', WildLevels.OrderBy(p => p.Key).Select(p => $"l{p.Key}:{p.Value}"))
        + "|" + string.Join(';', Gifts.OrderBy(p => p.Key).Select(p => $"g{p.Key}:{p.Value}"))
        + "|" + string.Join(';', Statics.OrderBy(p => p.Key).Select(p => $"s{p.Key}:{p.Value}"))
        + "|" + string.Join(';', Trades.OrderBy(p => p.Key).Select(p => $"t{p.Key}:{p.Value}"));
}
