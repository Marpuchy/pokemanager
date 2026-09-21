using System.Text.Json;
using System.Text.Json.Serialization;
using Pokemanager.Model.Dump;
using Pokemanager.Model.Resources;

namespace Pokemanager.Model.Projects;

/// <summary>
/// Shareable file with the rules of the built ROM — the game options (shiny, level percentages, level cap) and the shops
/// — so the same set can be put on another project without repeating it by hand. The counterpart of a <c>.pkdata</c> for
/// what is not a table of the game.
/// </summary>
/// <remarks>
/// It holds settings, never bytes of the game, so it is a few lines of JSON and works between games: a value a game
/// cannot use (the wild levels of a Generation 6 ROM) is simply never applied at build time.
/// </remarks>
public sealed class GameRulesFile
{
    public const string Extension = "gmdata";
    public const string Magic = "pokemanager-game-rules";
    public const int CurrentFormat = 1;

    [JsonPropertyName("magic")]
    public string MagicValue { get; init; } = Magic;

    public int Format { get; init; } = CurrentFormat;

    /// <summary>The game it was exported from, to warn when it is another one.</summary>
    public GameTitle? Game { get; init; }

    /// <summary>What the project was, in words ("Pokémon Ultra Moon · 5054 changes").</summary>
    public string? Description { get; init; }

    public GameSettings Tweaks { get; init; } = new();

    public ShopSettings Shops { get; init; } = new();

    /// <summary>What it will change, for the question before importing.</summary>
    public int ValueCount => (Tweaks.AlwaysShiny ? 1 : 0) + (Tweaks.LevelCap is null ? 0 : 1) + (Tweaks.LevelCapByMilestones ? 1 : 0)
        + Tweaks.LevelCapOverrides.Count + (Tweaks.TrainerLevelPercent != 0 ? 1 : 0) + (Tweaks.WildLevelPercent != 0 ? 1 : 0)
        + (Tweaks.StaticLevelPercent != 0 ? 1 : 0) + (Shops.FreeRareCandies ? 1 : 0) + (Shops.MegaStonesOnSale ? 1 : 0);

    public static GameRulesFile FromProject(Project project, GameTitle game, string? description = null) => new()
    {
        Game = game,
        Description = description,
        Tweaks = project.Tweaks.Clone(),
        Shops = project.Shops.Clone(),
    };

    /// <summary>Puts the rules of the file into the project. The level cap of the save is left to be worked out again.</summary>
    public void ApplyTo(Project project)
    {
        project.Tweaks = Tweaks.Clone();
        project.Shops = Shops.Clone();
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, Options));
    }

    /// <exception cref="InvalidDataException">Not one of these files.</exception>
    public static GameRulesFile Load(string path)
    {
        var file = JsonSerializer.Deserialize<GameRulesFile>(File.ReadAllText(path), Options);
        if (file is null || file.MagicValue != Magic)
            throw new InvalidDataException(string.Format(Strings.Data_NotRules, Path.GetFileName(path)));
        if (file.Format > CurrentFormat)
            throw new InvalidDataException(string.Format(Strings.Data_NewerFormat, file.Format, CurrentFormat));
        return file;
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
