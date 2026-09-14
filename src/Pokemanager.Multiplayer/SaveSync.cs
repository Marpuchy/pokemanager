using PKHeX.Core;

namespace Pokemanager.Multiplayer;

/// <summary>
/// Turns what changed in a player's save between two synchronizations into room events. Only facts are reported
/// (captured, evolved, fainted, milestone); the rules decide later what they mean.
/// </summary>
public static class SaveSync
{
    /// <param name="previous">The snapshot of the last synchronization, or null the first time: then everything already
    /// in the save is reported, in capture order.</param>
    /// <param name="locationName">Name of a met location in the save's game; defaults to PKHeX's names.</param>
    public static List<RoomEvent> Detect(LinkRules rules, string playerId, SaveSnapshot? previous, SaveSnapshot current,
        Func<SaveSnapshot, SnapshotPokemon, string>? locationName = null)
    {
        locationName ??= DefaultLocationName;
        var when = current.Taken;
        var events = new List<RoomEvent>();
        var before = (previous?.Pokemon ?? []).Where(p => p.Owned).ToDictionary(p => p.Key);
        var now = current.Pokemon.Where(p => p.Owned && !p.IsEgg).ToList();

        // Captures: the first time the save may already hold a run; its order is the met date, then party before boxes.
        var captured = now.Where(p => !before.ContainsKey(p.Key));
        if (previous is null)
            captured = captured.OrderBy(p => p.MetDate ?? DateOnly.MaxValue).ThenBy(p => p.Box.HasValue);
        foreach (var p in captured)
            events.Add(new PokemonCaptured(p.Key, p.Species, p.Form, p.Nickname, p.Level, p.MetLocation, locationName(current, p),
                p.WasEgg, p.IsShiny, p.PrimaryType, Area(current, p)) { PlayerId = playerId, When = when });

        foreach (var p in now)
        {
            if (before.TryGetValue(p.Key, out var old)
                && (old.Species, old.Form, old.Nickname, old.Level, old.PrimaryType) != (p.Species, p.Form, p.Nickname, p.Level, p.PrimaryType))
                events.Add(new PokemonUpdated(p.Key, p.Species, p.Form, p.Nickname, p.Level, p.PrimaryType)
                    { PlayerId = playerId, When = when });
        }

        // Deaths: only conditions that became true since the last synchronization.
        int graveyard = rules.GraveyardBox < 0 ? current.BoxCount - 1 : rules.GraveyardBox;
        foreach (var p in now)
        {
            before.TryGetValue(p.Key, out var old);
            if (rules.DeathDetection.HasFlag(DeathDetection.FaintedInParty) && IsFainted(p) && (old is null || !IsFainted(old)))
                events.Add(new PokemonDied(p.Key, DeathCause.FaintedInParty) { PlayerId = playerId, When = when });
            else if (rules.DeathDetection.HasFlag(DeathDetection.GraveyardBox) && p.Box == graveyard && (old is null || old.Box != graveyard))
                events.Add(new PokemonDied(p.Key, DeathCause.GraveyardBox) { PlayerId = playerId, When = when });
        }
        if (rules.DeathDetection.HasFlag(DeathDetection.Released))
        {
            var present = current.Pokemon.Select(p => p.Key).ToHashSet();
            foreach (var old in before.Values.Where(p => !p.IsEgg && !present.Contains(p.Key)))
                events.Add(new PokemonDied(old.Key, DeathCause.Released) { PlayerId = playerId, When = when });
        }

        for (int i = 0; i < current.Milestones.Count; i++)
        {
            bool had = previous is not null && i < previous.Milestones.Count && previous.Milestones[i];
            if (current.Milestones[i] && !had)
                events.Add(new MilestoneEarned(i) { PlayerId = playerId, When = when });
        }

        var party = current.Pokemon.Where(p => p.Box is null && p.Owned && !p.IsEgg).Select(p => p.Key).ToList();
        events.Add(new SaveSynced(party, current.MilestonesEarned) { PlayerId = playerId, When = when });
        return events;
    }

    private static bool IsFainted(SnapshotPokemon p) => p.Box is null && p.Hp == 0;

    private static string DefaultLocationName(SaveSnapshot save, SnapshotPokemon p) =>
        LocationName(GameInfo.Strings, save, p) is { Length: > 0 } name ? name : $"#{p.MetLocation}";

    /// <summary>
    /// The English location name without its sub-area: "Route 1 (Trainers' School)" → "Route 1". English whatever the
    /// app language, so players with different languages agree.
    /// </summary>
    internal static string Area(SaveSnapshot save, SnapshotPokemon p)
    {
        string name = LocationName(GameInfo.GetStrings("en"), save, p);
        int paren = name.IndexOf(" (", StringComparison.Ordinal);
        return (paren > 0 ? name[..paren] : name).Trim();
    }

    private static string LocationName(GameStrings strings, SaveSnapshot save, SnapshotPokemon p)
    {
        var version = Enum.TryParse<GameVersion>(save.Game, out var v) ? v : GameVersion.Any;
        byte generation = (byte)save.Generation;
        return strings.GetLocationName(false, (ushort)p.MetLocation, generation, generation, version) ?? "";
    }
}
