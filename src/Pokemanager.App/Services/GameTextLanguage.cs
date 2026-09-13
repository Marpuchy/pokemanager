using System.Globalization;
using Pokemanager.Model.Dump;

namespace Pokemanager.App.Services;

/// <summary>
/// Language of the game texts shown in the app (species, moves, items, abilities): the interface language, so an English
/// interface never shows Spanish move names. X/Y cartridges contain every language, so any dump has them.
/// </summary>
public static class GameTextLanguage
{
    public static GameLanguage Current => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName switch
    {
        "es" => GameLanguage.Spanish,
        "fr" => GameLanguage.French,
        "it" => GameLanguage.Italian,
        "de" => GameLanguage.German,
        "ja" => GameLanguage.JapaneseKanji,
        "ko" => GameLanguage.Korean,
        _ => GameLanguage.English,
    };
}
