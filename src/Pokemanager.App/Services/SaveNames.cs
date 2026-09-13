using PKHeX.Core;
using Pokemanager.App.Resources;
using GameLanguage = Pokemanager.Model.Dump.GameLanguage;

namespace Pokemanager.App.Services;

/// <summary>
/// Names for the save editor. Species, moves, items and abilities come from the game text (the project's language, as
/// the game shows them); natures and balls from PKHeX in the same language.
/// </summary>
public sealed class SaveNames
{
    public IReadOnlyList<string> Species { get; }

    /// <summary>Species list for pickers: index = species ID, entry 0 is a placeholder.</summary>
    public IReadOnlyList<string> SpeciesChoices { get; }

    /// <summary>Index = move ID; entry 0 is "(none)".</summary>
    public IReadOnlyList<string> Moves { get; }

    /// <summary>Index = item ID; entry 0 is "(none)".</summary>
    public IReadOnlyList<string> Items { get; }

    public IReadOnlyList<string> Abilities { get; }

    /// <summary>Type names from the game text, in the game's type order.</summary>
    public IReadOnlyList<string> Types { get; }
    public IReadOnlyList<string> Natures { get; }

    /// <summary>Index = ball ID.</summary>
    public IReadOnlyList<string> Balls { get; }

    /// <summary>Vivillon patterns (the player's region pattern).</summary>
    public IReadOnlyList<string> VivillonPatterns { get; }

    public SaveNames(GameNames game, GameLanguage language, int maxSpecies)
    {
        Species = game.Species;
        SpeciesChoices = [.. game.Species.Take(maxSpecies + 1).Select((name, i) => i == 0 ? "—" : $"#{i:000} {name}")];
        Moves = [Strings.Pkm_None, .. game.Moves.Skip(1)];
        Items = [Strings.Pkm_None, .. game.Items.Skip(1).Select((name, i) => string.IsNullOrWhiteSpace(name) ? $"#{i + 1}" : name)];
        Abilities = game.Abilities;
        Types = game.Types;

        var pkhex = GameInfo.GetStrings(PkhexLanguage(language));
        Natures = [.. pkhex.natures.Take(25)];
        Balls = [.. pkhex.balllist];
        VivillonPatterns = [.. FormConverter.GetFormList((ushort)PKHeX.Core.Species.Vivillon, pkhex.types, pkhex.forms, GameInfo.GenderSymbolUnicode, EntityContext.Gen6)
            .Take(Pokemanager.Save.SaveDocument.VivillonPatterns)];
    }

    public string SpeciesName(ushort species) => species == 0 ? Strings.Save_Empty : species < Species.Count ? Species[species] : $"#{species}";

    public string ItemName(int item) => item >= 0 && item < Items.Count ? Items[item] : $"#{item}";

    public string AbilityName(int ability) => ability > 0 && ability < Abilities.Count ? Abilities[ability] : "—";

    private static string PkhexLanguage(GameLanguage language) => language switch
    {
        GameLanguage.JapaneseKana or GameLanguage.JapaneseKanji => "ja",
        GameLanguage.French => "fr",
        GameLanguage.Italian => "it",
        GameLanguage.German => "de",
        GameLanguage.Spanish => "es",
        GameLanguage.Korean => "ko",
        _ => "en",
    };
}
