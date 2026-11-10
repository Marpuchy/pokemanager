using pk3DS.Core.Structures;
using Pokemanager.App.Resources;
using Pokemanager.Model.Data;

namespace Pokemanager.App.Services;

/// <summary>Evolutions in words: the method names and a sentence for one evolution ("Level 16", "Use an item: Fire Stone").</summary>
public static class EvolutionText
{
    private static IReadOnlyList<string>? methods;

    /// <summary>The 43 methods, by number. Built once: bound to an <c>ItemsSource</c> next to a two-way selection.</summary>
    public static IReadOnlyList<string> Methods =>
        methods ??= [.. Enumerable.Range(0, EvolutionSlots.MethodCount)
            .Select(i => Strings.ResourceManager.GetString($"Evo_Method_{i}", Strings.Culture) ?? $"#{i}")];

    public static string MethodName(int method) => method >= 0 && method < Methods.Count ? Methods[method] : $"#{method}";

    /// <summary>How the evolution happens, with what it needs.</summary>
    public static string How(EvolutionMethod e, bool generation7, GameNames names)
    {
        int level = EvolutionSlots.LevelOf(e, generation7);
        string text = e.Method == 4 && level > 0 ? string.Format(Strings.Evo_AtLevel, level) : MethodName(e.Method);
        if (e.Method != 4 && level > 0)
            text += " " + string.Format(Strings.Evo_LevelSuffix, level);
        string? need = EvolutionSlots.ArgumentOf(e.Method) switch
        {
            EvolutionArgument.Item => Name(names.Items, e.Argument),
            EvolutionArgument.Move => Name(names.Moves, e.Argument),
            EvolutionArgument.Species => Name(names.Species, e.Argument),
            EvolutionArgument.Type => Name(names.Types, e.Argument),
            _ => null,
        };
        // "Use an item" says nothing the item's name does not (issue #2): just "Water Stone", and what the method adds
        // after those words stays ("Dawn Stone, male", "Thunder Stone in Ultra Space").
        string useItem = MethodName(8);
        if (need is not null && EvolutionSlots.ArgumentOf(e.Method) == EvolutionArgument.Item
            && MethodName(e.Method).StartsWith(useItem, StringComparison.Ordinal))
        {
            string rest = MethodName(e.Method)[useItem.Length..];
            string item = need + rest;
            return level > 0 ? item + " " + string.Format(Strings.Evo_LevelSuffix, level) : item;
        }
        return need is null ? text : $"{text}: {need}";
    }

    /// <summary>The Pokémon it becomes, with the form when Generation 7 names one.</summary>
    public static string Target(EvolutionMethod e, bool generation7, GameNames names)
    {
        string name = Name(names.Species, e.Species);
        return generation7 && e.Form > 0 ? string.Format(Strings.Evo_FormSuffix, name, e.Form) : name;
    }

    private static string Name(IReadOnlyList<string> list, int index) => index >= 0 && index < list.Count ? list[index] : $"#{index}";
}
