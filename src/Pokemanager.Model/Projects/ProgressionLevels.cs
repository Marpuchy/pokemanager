namespace Pokemanager.Model.Projects;

/// <summary>
/// The levels and chances of the evolution progression (<see cref="RandomizationSettings.EvolutionProgression"/>), as
/// the patched randomizer reads them: up to <paramref name="FirstUntil"/> a Pokémon that is not a first stage becomes
/// its first stage <paramref name="FirstPercent"/> % of the time; up to <paramref name="DropUntil"/> a fully evolved one
/// goes back one stage <paramref name="DropPercent"/> % of the time; from <paramref name="FinalFrom"/> everything is
/// fully evolved.
/// </summary>
public sealed record ProgressionLevels(int FirstUntil = 19, int FirstPercent = 90, int DropUntil = 34, int DropPercent = 50, int FinalFrom = 55)
{
    public static ProgressionLevels Default { get; } = new();

    /// <summary>Within range and in order (first ≤ drop &lt; final).</summary>
    public ProgressionLevels Valid()
    {
        int first = Math.Clamp(FirstUntil, 0, 100);
        int drop = Math.Clamp(DropUntil, first, 100);
        int final = Math.Clamp(FinalFrom, Math.Min(100, drop + 1), 101);
        return new(first, Math.Clamp(FirstPercent, 0, 100), drop, Math.Clamp(DropPercent, 0, 100), final);
    }
}
