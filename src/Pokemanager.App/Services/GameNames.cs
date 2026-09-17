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

    /// <summary>Trainer names, by trainer id; the game leaves a placeholder for the unnamed ones.</summary>
    public IReadOnlyList<string> TrainerNames { get; }

    /// <summary>Trainer class names ("Leader", "Youngster"), by class index.</summary>
    public IReadOnlyList<string> TrainerClasses { get; }

    /// <summary>Nature names in the game's index order (PKHeX's list, in the project's text language).</summary>
    public IReadOnlyList<string> Natures { get; }

    /// <summary>Items with "none" in slot 0, for the pickers where 0 means the trainer carries nothing.</summary>
    public IReadOnlyList<string> ItemChoices { get; }

    /// <summary>Moves with "none" in slot 0 (the game's own entry there is a row of dashes).</summary>
    public IReadOnlyList<string> MoveChoices { get; }

    /// <summary>The bag's description of each item, in the project's text language (empty when the game has none).</summary>
    public IReadOnlyList<string> ItemDescriptions { get; }

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

    /// <summary>Species classification ("Seed Pokémon") by species, in the project's text language; empty when unknown.</summary>
    public IReadOnlyList<string> Classifications { get; }

    /// <summary>This version's Pokédex entry by species (plain text with line breaks); empty when the game has none.</summary>
    public IReadOnlyList<string> PokedexEntries { get; }

    public GameNames(GameDump dump, GameData original)
    {
        var config = dump.Config;
        Species = config.GetText(TextName.SpeciesNames);
        Types = config.GetText(TextName.Types);
        Abilities = config.GetText(TextName.AbilityNames);
        Items = config.GetText(TextName.ItemNames);
        Moves = config.GetText(TextName.MoveNames);
        TrainerNames = config.GetText(TextName.TrainerNames);
        TrainerClasses = config.GetText(TextName.TrainerClasses);
        Natures = [.. PKHeX.Core.GameInfo.GetStrings(SaveNames.PkhexLanguage(dump.Language)).natures.Take(25)];
        ItemChoices = [Strings.Pkm_None, .. Items.Skip(1)];
        MoveChoices = [Strings.Pkm_None, .. Moves.Skip(1)];
        ItemDescriptions = [.. config.GetText(TextName.ItemFlavor).Select(line => GameText.ToEditable(line).Replace("\\c", "\n").Replace("\\r", "\n").Trim())];
        PersonalEntries = BuildPersonalEntryNames(original, Species, config.MaxSpeciesID);
        var (classifications, entries) = GameText.PokedexFiles(dump.Title);
        Classifications = TextFile(config, classifications);
        PokedexEntries = TextFile(config, entries);
    }

    /// <summary>"Leader Korrina" — the class as the game names it plus the trainer's name, both in the project's language.</summary>
    public string TrainerLabel(int id, int trainerClass)
    {
        string cls = trainerClass >= 0 && trainerClass < TrainerClasses.Count ? TrainerClasses[trainerClass] : "";
        string name = id >= 0 && id < TrainerNames.Count ? TrainerNames[id] : "";
        string label = string.Join(' ', new[] { cls, name }.Where(p => !string.IsNullOrWhiteSpace(p)));
        return label.Length == 0 ? string.Format(Strings.Trainer_Unnamed, id) : label;
    }

    private static string[] TextFile(pk3DS.Core.GameConfig config, int file) =>
        file >= 0 && file < config.GameTextStrings.Length
            ? [.. config.GameTextStrings[file].Select(line => GameText.ToEditable(line).Replace("\\c", "\n").Replace("\\r", "\n").Trim())]
            : [];

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
