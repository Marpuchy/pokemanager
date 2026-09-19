using System.Text;
using pk3DS.Core;
using Pokemanager.Model.Dump;

namespace Pokemanager.Model.Data;

/// <summary>
/// The game's text archives (one GARC per language, a text file per list) and the conversion between pk3DS's line syntax
/// (<c>\n</c> for a line break, <c>\\</c>, <c>\[</c>, <c>[VAR …]</c>) and plain text to edit.
/// </summary>
public static class GameText
{
    /// <summary>Text archive of a language: <c>a/0/7/2</c> + language in X/Y.</summary>
    public static string Archive(GameTitle title, GameLanguage language) => Archive(title, (int)language);

    public static string Archive(GameTitle title, int language) => GameImporter.GarcPath(title.Layout().GameText + language);

    /// <summary>
    /// Files of the species classifications ("Seed Pokémon") and of this version's Pokédex entries. pk3DS only lists them
    /// for Gen 7; the Gen 6 ones were found in the real X and Alpha Sapphire dumps (2; entries 7 for X / Omega Ruby and 6
    /// for Y / Alpha Sapphire).
    /// </summary>
    public static (int Classifications, int Entries) PokedexFiles(GameTitle title) => title switch
    {
        GameTitle.X or GameTitle.OmegaRuby => (2, 7),
        GameTitle.Y or GameTitle.AlphaSapphire => (2, 6),
        GameTitle.Sun => (116, 119),
        GameTitle.Moon => (116, 120),
        GameTitle.UltraSun => (121, 124),
        _ => (121, 125),
    };

    /// <summary>File of the move descriptions inside a text archive.</summary>
    public static int MoveDescriptionFile(GameTitle title) => References(title).First(r => r.Name == TextName.MoveFlavor).Index;

    /// <summary>File of the type names inside a text archive, or -1 when the game has no such file listed.</summary>
    public static int TypeNameFile(GameTitle title) =>
        References(title).FirstOrDefault(r => r.Name == TextName.Types)?.Index ?? -1;

    /// <summary>File of the species names inside a text archive.</summary>
    public static int SpeciesNameFile(GameTitle title) => References(title).First(r => r.Name == TextName.SpeciesNames).Index;

    /// <summary>Path of a per-language story text archive (the scenes people talk in), or null when the game has none known.</summary>
    public static string? StoryArchive(GameTitle title, int language) =>
        title.Layout().StoryText is var first and > 0 ? GameImporter.GarcPath(first + language) : null;

    private static TextReference[] References(GameTitle title) => title.Family() switch
    {
        GameFamily.XY => TextReference.GameText_XY,
        GameFamily.ORAS => TextReference.GameText_AO,
        GameFamily.SM => TextReference.GameText_SM,
        _ => TextReference.GameText_USUM,
    };

    /// <summary>The text file's lines in pk3DS syntax.</summary>
    /// <exception cref="InvalidDataException">Not a text file.</exception>
    public static string[] Read(GameTitle title, byte[] file)
    {
        try
        {
            return new TextFile(Config(title), file).Lines;
        }
        catch (Exception ex) when (ex is not InvalidDataException)
        {
            throw new InvalidDataException("Not a game text file.", ex);
        }
    }

    public static byte[] Write(GameTitle title, string[] lines) => TextFile.GetBytes(Config(title), lines);

    /// <summary>Only the variable names matter to the text codec; files without variables need nothing else.</summary>
    private static GameConfig Config(GameTitle title) => new(title.Pk3dsVersion());

    /// <summary>pk3DS line → text to edit: real line breaks, no escapes. Variables (<c>[VAR …]</c>) stay as they are.</summary>
    public static string ToEditable(string line)
    {
        var s = new StringBuilder(line.Length);
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '\\' && i + 1 < line.Length && line[i + 1] is 'n' or '\\' or '[')
            {
                s.Append(line[i + 1] == 'n' ? '\n' : line[i + 1]);
                i++;
            }
            else
            {
                s.Append(line[i]);
            }
        }
        return s.ToString();
    }

    /// <summary>Text edited in the app → pk3DS line: line breaks as <c>\n</c>, a literal <c>\</c> or <c>[</c> escaped.</summary>
    public static string FromEditable(string text)
    {
        var s = new StringBuilder(text.Length + 8);
        string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        for (int i = 0; i < normalized.Length; i++)
        {
            char c = normalized[i];
            switch (c)
            {
                case '\n': s.Append(@"\n"); break;
                case '\\': s.Append(@"\\"); break;
                // A game variable keeps its brackets; any other '[' is text.
                case '[' when !IsVariable(normalized, i): s.Append(@"\["); break;
                default: s.Append(c); break;
            }
        }
        return s.ToString();
    }

    private static bool IsVariable(string text, int index) =>
        text.IndexOf(']', index) > index
        && (text.AsSpan(index).StartsWith("[VAR ") || text.AsSpan(index).StartsWith("[WAIT ") || text.AsSpan(index).StartsWith("[~ "));
}
