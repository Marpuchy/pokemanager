using pk3DS.Core;
using pk3DS.Core.CTR;
using Pokemanager.Model.Resources;

namespace Pokemanager.Model.Dump;

/// <summary>Supported games: the Pokémon games of the 3DS with their own data format.</summary>
public enum GameTitle
{
    X,
    Y,
    OmegaRuby,
    AlphaSapphire,
    Sun,
    Moon,
    UltraSun,
    UltraMoon,
}

/// <summary>Games that share data layout, save format and progression (pairs of versions).</summary>
public enum GameFamily
{
    XY,
    ORAS,
    SM,
    USUM,
}

/// <summary>Where each family keeps what the application reads, and how it is stored.</summary>
/// <param name="Personal">Base stats, types, abilities… one entry per species/form plus the whole table as the last file.</param>
/// <param name="Moves">Move data: one file per move (X/Y) or a single "WD" mini archive (the rest).</param>
/// <param name="GameText">First of the per-language game text archives (index = pk3DS language).</param>
/// <param name="Icons">Pokémon box icons (BCLIM in Gen 6, BFLIM in Gen 7); the species → icon table is in code.bin.</param>
/// <param name="Milestones">Where the badge/trial images are: archive, file inside it and image names in milestone order.</param>
/// <param name="TrainerData">One file per trainer: class, battle type, bag, the AI byte and how many Pokémon.</param>
/// <param name="TrainerPokemon">The teams, one file per trainer, entries the size the trainer's record says.</param>
/// <param name="Items">Item data: one file per item, price included.</param>
/// <param name="MegaEvolutions">One file per species: how each of its mega evolutions is triggered, the stone included.</param>
/// <param name="Statics">
/// Generation 7: the Pokémon that wait in a fixed place, the three starters first (20 bytes each in file 0). Empty in
/// Generation 6, which keeps them in a CRO module.
/// </param>
/// <param name="StoryText">First of the per-language story text archives (index = pk3DS language), or 0 when unknown.</param>
/// <param name="StarterTextFile">The file of the story text where the starter scene talks, or -1 when unknown.</param>
/// <param name="Experience">
/// The experience each level needs, one file per growth rate (101 <c>u32</c>, level 0 to 100). Found by its values in the
/// real games: the archive just before the personal one in every family.
/// </param>
public sealed record GameLayout(
    string Personal,
    string Moves,
    string LevelUp,
    string Evolution,
    int GameText,
    int LanguageCount,
    bool MovesPacked,
    int SpeciesCount,
    string[] Icons,
    string[] ItemIcons,
    MilestoneImages[] Milestones,
    int FileCount,
    string TrainerData,
    string TrainerPokemon,
    string Items,
    string MegaEvolutions,
    string Statics = "",
    int StoryText = 0,
    int StarterTextFile = -1,
    string Experience = "");

/// <summary>
/// Images of badges or trials inside a layout archive (darc in Gen 6, SARC inside ALYT in Gen 7), matched by file name; an
/// empty name means that milestone has no image. Several candidates can be listed: the first one that has the images is
/// used.
/// </summary>
public sealed record MilestoneImages(string Archive, int File, string[] Names);

/// <summary>
/// A step of the game's progression that the Locke roulette rewards: a gym badge (Gen 6) or an island's grand trial
/// (Gen 7). <see cref="Type"/> is the type of its leader or kahuna, for the colors.
/// </summary>
public sealed record Milestone(string Name, int Type);

public static class GameTitleExtensions
{
    /// <summary>Title ID of the game, the same in every region.</summary>
    public static ulong TitleId(this GameTitle title) => title switch
    {
        GameTitle.X => 0x0004000000055D00,
        GameTitle.Y => 0x0004000000055E00,
        GameTitle.OmegaRuby => 0x000400000011C400,
        GameTitle.AlphaSapphire => 0x000400000011C500,
        GameTitle.Sun => 0x0004000000164800,
        GameTitle.Moon => 0x0004000000175E00,
        GameTitle.UltraSun => 0x00040000001B5000,
        GameTitle.UltraMoon => 0x00040000001B5100,
        _ => throw new ArgumentOutOfRangeException(nameof(title), title, null),
    };

    /// <summary>Title ID as 16 hex digits, as the emulator's folders use it.</summary>
    public static string TitleIdHex(this GameTitle title) => title.TitleId().ToString("X16");

    /// <summary>The game of a program ID (read from a ROM), or null when it is not a supported game.</summary>
    public static GameTitle? FromTitleId(ulong titleId) =>
        Enum.GetValues<GameTitle>().Cast<GameTitle?>().FirstOrDefault(t => t!.Value.TitleId() == titleId);

    public static GameFamily Family(this GameTitle title) => title switch
    {
        GameTitle.X or GameTitle.Y => GameFamily.XY,
        GameTitle.OmegaRuby or GameTitle.AlphaSapphire => GameFamily.ORAS,
        GameTitle.Sun or GameTitle.Moon => GameFamily.SM,
        _ => GameFamily.USUM,
    };

    public static int Generation(this GameTitle title) => title.Family() is GameFamily.XY or GameFamily.ORAS ? 6 : 7;

    /// <summary>pk3DS version with the exact game, so no file is needed to tell Sun from Moon.</summary>
    public static GameVersion Pk3dsVersion(this GameTitle title) => title switch
    {
        GameTitle.X or GameTitle.Y => GameVersion.XY,
        GameTitle.OmegaRuby or GameTitle.AlphaSapphire => GameVersion.ORAS,
        GameTitle.Sun => GameVersion.SN,
        GameTitle.Moon => GameVersion.MN,
        GameTitle.UltraSun => GameVersion.US,
        _ => GameVersion.UM,
    };

    /// <summary>Name of the game in the interface language ("Pokémon Ultra Moon").</summary>
    public static string DisplayName(this GameTitle title) => title switch
    {
        GameTitle.X => Strings.Game_X,
        GameTitle.Y => Strings.Game_Y,
        GameTitle.OmegaRuby => Strings.Game_OmegaRuby,
        GameTitle.AlphaSapphire => Strings.Game_AlphaSapphire,
        GameTitle.Sun => Strings.Game_Sun,
        GameTitle.Moon => Strings.Game_Moon,
        GameTitle.UltraSun => Strings.Game_UltraSun,
        _ => Strings.Game_UltraMoon,
    };

    public static GameLayout Layout(this GameTitle title) => title.Family() switch
    {
        GameFamily.XY => new GameLayout("a/2/1/8", "a/2/1/2", "a/2/1/4", "a/2/1/5", 72, 8, MovesPacked: false, SpeciesCount: 722,
            Icons: ["a/0/9/3"], ItemIcons: [], Milestones: [new MilestoneImages("a/1/1/3", 0, BadgeNames)], FileCount: 271,
            TrainerData: "a/0/3/8", TrainerPokemon: "a/0/4/0", Items: "a/2/2/0", MegaEvolutions: "a/2/1/6",
            Experience: "a/2/1/7"),
        GameFamily.ORAS => new GameLayout("a/1/9/5", "a/1/8/9", "a/1/9/1", "a/1/9/2", 71, 8, MovesPacked: true, SpeciesCount: 722,
            Icons: ["a/0/9/1"], ItemIcons: [], Milestones: [new MilestoneImages("a/1/0/9", 0, BadgeNames)], FileCount: 299,
            TrainerData: "a/0/3/6", TrainerPokemon: "a/0/3/8", Items: "a/1/9/7", MegaEvolutions: "a/1/9/3",
            Experience: "a/1/9/4"),
        // Sun/Moon: the archives are not verified on a real dump (none was available); icons and stamps are looked up
        // among the candidates and simply not shown if absent.
        GameFamily.SM => new GameLayout("a/0/1/7", "a/0/1/1", "a/0/1/3", "a/0/1/4", 30, 10, MovesPacked: true, SpeciesCount: 803,
            Icons: ["a/0/6/2", "a/0/6/1", "a/0/6/3"], ItemIcons: ["a/0/6/1", "a/0/6/0", "a/0/6/3"],
            Milestones: TrialStamps("a/2/3/8", "a/2/3/9", "a/2/4/0", "a/2/4/1", "a/2/4/2"), FileCount: 311,
            TrainerData: "a/1/0/5", TrainerPokemon: "a/1/0/6", Items: "a/0/1/9", MegaEvolutions: "a/0/1/5",
            Statics: "a/1/5/5", StoryText: 40, StarterTextFile: 41, Experience: "a/0/1/6"),
        _ => new GameLayout("a/0/1/7", "a/0/1/1", "a/0/1/3", "a/0/1/4", 30, 10, MovesPacked: true, SpeciesCount: 808,
            Icons: ["a/0/6/2"], ItemIcons: ["a/0/6/1"], Milestones: TrialStamps("a/2/4/2", "a/2/9/6", "a/2/9/7"), FileCount: 333,
            TrainerData: "a/1/0/6", TrainerPokemon: "a/1/0/7", Items: "a/0/1/9", MegaEvolutions: "a/0/1/5",
            Statics: "a/1/5/9", StoryText: 40, StarterTextFile: 39, Experience: "a/0/1/6"),
    };

    /// <summary>Gen 6 trainer card: <c>badge_01.bclim</c> … <c>badge_08.bclim</c> in a darc.</summary>
    private static readonly string[] BadgeNames = Enumerable.Range(1, 8).Select(i => $"badge_{i:00}.bclim").ToArray();

    /// <summary>
    /// Gen 7 trial screens: <c>Shiren_Stamp_00</c> … <c>03</c>, the guardian seals of Melemele, Akala, Ula'ula and Poni
    /// (verified on Ultra Moon, <c>a/2/4/2</c> file 4). The island challenge completion has no image of its own.
    /// </summary>
    private static MilestoneImages[] TrialStamps(params string[] archives) => archives
        .Select(a => new MilestoneImages(a, 4, [.. Enumerable.Range(0, 4).Select(i => $"Shiren_Stamp_{i:00}.bflim"), ""]))
        .ToArray();

    /// <summary>
    /// The progression steps the Locke roulette rewards, in game order. Gen 6: the eight gym badges. Gen 7: the four
    /// islands' grand trials and the completed island challenge (trainer stamps).
    /// </summary>
    public static IReadOnlyList<Milestone> Milestones(this GameTitle title) => title.Family() switch
    {
        GameFamily.XY =>
        [
            new(Strings.Badge_Bug, 6), new(Strings.Badge_Cliff, 5), new(Strings.Badge_Rumble, 1), new(Strings.Badge_Plant, 11),
            new(Strings.Badge_Voltage, 12), new(Strings.Badge_Fairy, 17), new(Strings.Badge_Psychic, 13), new(Strings.Badge_Iceberg, 14),
        ],
        GameFamily.ORAS =>
        [
            new(Strings.Badge_Stone, 5), new(Strings.Badge_Knuckle, 1), new(Strings.Badge_Dynamo, 12), new(Strings.Badge_Heat, 9),
            new(Strings.Badge_Balance, 0), new(Strings.Badge_Feather, 2), new(Strings.Badge_Mind, 13), new(Strings.Badge_Rain, 10),
        ],
        _ =>
        [
            new(Strings.Trial_Melemele, 1), new(Strings.Trial_Akala, 5), new(Strings.Trial_Ulaula, 16), new(Strings.Trial_Poni, 4),
            new(Strings.Trial_Champion, 15),
        ],
    };

    /// <summary>Whether the progression is counted in badges (Gen 6) or trials (Gen 7).</summary>
    public static bool HasBadges(this GameTitle title) => title.Generation() == 6;

    /// <summary>GARC version the game uses (for archives built from scratch).</summary>
    public static int GarcVersion(this GameTitle title) => title.Generation() == 6 ? GARC.VER_4 : GARC.VER_6;
}
