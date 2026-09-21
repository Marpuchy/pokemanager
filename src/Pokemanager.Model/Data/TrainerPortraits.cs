using Pokemanager.Model.Dump;

namespace Pokemanager.Model.Data;

/// <summary>
/// The player's own picture, as the game draws it: the portraits of the screen where the adventure starts and the player
/// picks how they look.
/// </summary>
/// <remarks>
/// **Found in the user's Ultra Moon**: the layout archive <c>a/1/6/2</c> (<c>HeroSelect</c>) holds eight 100×100 BFLIM
/// images — <c>m1_1</c>…<c>m1_4</c> and <c>f1_1</c>…<c>f1_4</c>, the four looks of each gender. Which one the player is
/// wearing is in the save: the gender and <c>DressUpSkinColor</c> (0-3). Everything else the game shows of the trainer
/// (the card, the passport) is a 3D model rendered live, so these are the only pictures of the player in the ROM.
/// </remarks>
public static class TrainerPortraits
{
    /// <summary>How many looks each gender has.</summary>
    public const int Looks = 4;

    /// <summary>
    /// The player's portrait, or null when the game folder does not have that archive (a folder imported before this
    /// version, or a game whose screen this application has not found).
    /// </summary>
    /// <param name="gender">0 male, 1 female, as the save stores it.</param>
    /// <param name="look">The look chosen at the start (0-3).</param>
    public static IconImage? Load(RomFsLayers layers, GameTitle title, int gender, int look)
    {
        foreach (string archive in title.Layout().Portraits)
        {
            if (Read(layers, archive) is not { } files)
                continue;
            string name = $"{(gender == 1 ? "f" : "m")}1_{Math.Clamp(look, 0, Looks - 1) + 1}";
            if (Find(files, name) is { } image && PokemonIcons.DecodeBclim(image) is { } picture)
                return picture;
        }
        return null;
    }

    /// <summary>Whether this game's portraits are where this application looks for them.</summary>
    public static bool Available(RomFsLayers layers, GameTitle title) =>
        title.Layout().Portraits.Any(a => Read(layers, a) is { } files && Find(files, "m1_1") is not null);

    private static Dictionary<string, byte[]>? Read(RomFsLayers layers, string archive)
    {
        try
        {
            return MilestoneIcons.ReadArchive(File.ReadAllBytes(layers.Resolve(archive)), 0);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>The layout files are named <c>timg/m1_1.bflim</c>; only the name matters.</summary>
    private static byte[]? Find(Dictionary<string, byte[]> files, string name) =>
        files.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f.Key).Equals(name, StringComparison.OrdinalIgnoreCase)).Value;
}
