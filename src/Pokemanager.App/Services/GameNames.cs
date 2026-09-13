using pk3DS.Core;
using Pokemanager.App.Resources;
using Pokemanager.Model.Data;
using Pokemanager.Model.Dump;

namespace Pokemanager.App.Services;

/// <summary>Display names: game texts in the project's language, and UI lists in the UI language.</summary>
public sealed class GameNames
{
    public IReadOnlyList<string> Species { get; }
    public IReadOnlyList<string> PersonalEntries { get; }
    public IReadOnlyList<string> Types { get; }
    public IReadOnlyList<string> Abilities { get; }
    public IReadOnlyList<string> Items { get; }
    public IReadOnlyList<string> Moves { get; }

    /// <summary>Growth rates in the game's index order.</summary>
    public static IReadOnlyList<string> ExpGrowth =>
        [Strings.Growth_MediumFast, Strings.Growth_Erratic, Strings.Growth_Fluctuating, Strings.Growth_MediumSlow, Strings.Growth_Fast, Strings.Growth_Slow];

    /// <summary>Egg groups in the game's index order (0 = none).</summary>
    public static IReadOnlyList<string> EggGroups =>
    [
        Strings.Egg_None, Strings.Egg_Monster, Strings.Egg_Water1, Strings.Egg_Bug, Strings.Egg_Flying, Strings.Egg_Field,
        Strings.Egg_Fairy, Strings.Egg_Grass, Strings.Egg_HumanLike, Strings.Egg_Water3, Strings.Egg_Mineral,
        Strings.Egg_Amorphous, Strings.Egg_Water2, Strings.Egg_Ditto, Strings.Egg_Dragon, Strings.Egg_Undiscovered,
    ];

    public static IReadOnlyList<string> MoveCategories => [Strings.Category_Status, Strings.Category_Physical, Strings.Category_Special];

    public GameNames(GameDump dump, GameData original)
    {
        var config = dump.Config;
        Species = config.GetText(TextName.SpeciesNames);
        Types = config.GetText(TextName.Types);
        Abilities = config.GetText(TextName.AbilityNames);
        Items = config.GetText(TextName.ItemNames);
        Moves = config.GetText(TextName.MoveNames);
        PersonalEntries = BuildPersonalEntryNames(original, Species, config.MaxSpeciesID);
    }

    /// <summary>
    /// Entries above the species count are alternate forms: the base species tells with <c>FormStatsIndex</c> where
    /// they start and with <c>FormeCount</c> how many forms it has.
    /// </summary>
    private static string[] BuildPersonalEntryNames(GameData data, IReadOnlyList<string> species, int maxSpecies)
    {
        var names = new string[data.Personal.Length];
        for (int i = 0; i < names.Length; i++)
            names[i] = i < species.Count ? species[i] : string.Format(Strings.Species_Entry, i);

        for (int s = 1; s <= maxSpecies && s < data.Personal.Length; s++)
        {
            var p = data.Personal[s];
            if (p.FormStatsIndex <= 0)
                continue;
            for (int form = 1; form < p.FormeCount; form++)
            {
                int index = p.FormStatsIndex + form - 1;
                if (index < names.Length)
                    names[index] = string.Format(Strings.Species_Form, species[s], form);
            }
        }

        return names;
    }
}
