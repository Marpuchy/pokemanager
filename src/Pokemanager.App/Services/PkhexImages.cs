using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Pokemanager.Model.Dump;

namespace Pokemanager.App.Services;

/// <summary>
/// Images taken from PKHeX (GPL-3, see <c>Assets/PKHeX/SOURCE.txt</c>): box wallpapers of Gen 6/7, item icons and Poké Ball
/// icons, loaded from the app's resources and cached.
/// </summary>
public static class PkhexImages
{
    private const string Root = "avares://Pokemanager.App/Assets/PKHeX/";
    private static readonly Dictionary<string, Bitmap?> Cache = [];

    private static Bitmap? Load(string relative)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(relative, out var cached))
                return cached;
        }
        var uri = new Uri(Root + relative);
        Bitmap? bitmap = AssetLoader.Exists(uri) ? new Bitmap(AssetLoader.Open(uri)) : null;
        lock (Cache)
            Cache[relative] = bitmap;
        return bitmap;
    }

    /// <summary>An item's icon; TMs and HMs share one; unknown items get a question mark. Null for no item.</summary>
    public static Bitmap? Item(int item, bool isTm = false) => item <= 0
        ? null
        : isTm ? Load("items/bitem_tm.png") : Load($"items/bitem_{item}.png") ?? Load("items/bitem_unk.png");

    /// <summary>
    /// Picture for a Generation 7 trial: the Z-crystal of its type. The games only carry seals for the four island
    /// trials, so the fifth one had no picture at all; crystals give every trial one and they all match.
    /// Type crystals are items 776–793 in Pokédex type order (checked on the user's Ultra Moon).
    /// </summary>
    public static Bitmap? TrialCrystal(int type) => Item(CrystalIds[Math.Clamp(type, 0, CrystalIds.Length - 1)]);

    /// <summary>
    /// The eighteen type crystals in the order the bag shows them, with the two item ids each one has: the held piece
    /// (776–793, the only one PKHeX has an icon for) and the bead the bag keeps (that id + 31, checked on the user's
    /// Ultra Moon: Normalium 776/807, Fightinium 782/813, Rockium 788/819).
    /// </summary>
    public static IReadOnlyList<(int Held, int Bead, int Type)> TypeCrystals =>
        field ??= [.. Enumerable.Range(0, CrystalIds.Length)
            .Select(type => (Held: CrystalIds[type], Bead: CrystalIds[type] + 31, Type: type))
            .OrderBy(c => c.Held)];

    // Game type order: Normal, Fighting, Flying, Poison, Ground, Rock, Bug, Ghost, Steel, Fire, Water, Grass,
    // Electric, Psychic, Ice, Dragon, Dark, Fairy.
    private static readonly int[] CrystalIds =
        [776, 782, 785, 783, 784, 788, 787, 789, 792, 777, 778, 780, 779, 786, 781, 790, 791, 793];

    /// <summary>Whether PKHeX has a real icon for the item (it has none for the Gen 6/7 key items).</summary>
    public static bool HasItemIcon(int item, bool isTm = false) => item > 0 && (isTm || Load($"items/bitem_{item}.png") is not null);

    /// <summary>A type's square icon (PKHeX's, game type order); a question mark for an unknown type.</summary>
    public static Bitmap? Type(int type) => Load($"types/type_icon_{type:00}.png") ?? Load("types/type_icon_99.png");

    /// <summary>
    /// A move category's icon, drawn: physical a red-orange burst, special blue rings, status a grey half circle (the usual
    /// colors of the games and Showdown). 0 status, 1 physical, 2 special.
    /// </summary>
    public static Avalonia.Media.IImage Category(int category)
    {
        lock (Categories)
        {
            if (Categories.TryGetValue(category, out var cached))
                return cached;
            var image = DrawCategory(category);
            Categories[category] = image;
            return image;
        }
    }

    private static readonly Dictionary<int, Avalonia.Media.IImage> Categories = [];

    private static Avalonia.Media.IImage DrawCategory(int category)
    {
        var group = new Avalonia.Media.DrawingGroup();
        (string color, string figure) = category switch
        {
            1 => ("#C92112", "M12,2 L14.6,8.2 L21.5,6.5 L16.8,11.6 L21.5,17.5 L14.6,15.8 L12,22 L9.4,15.8 L2.5,17.5 L7.2,11.6 L2.5,6.5 L9.4,8.2 Z"),
            2 => ("#4F5870", "M12,4 A8,8 0 1 1 11.99,4 Z M12,8 A4,4 0 1 0 12.01,8 Z"),
            _ => ("#8C888C", "M4,14 A8,8 0 0 1 20,14 Z"),
        };
        group.Children.Add(new Avalonia.Media.GeometryDrawing
        {
            Brush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(color)),
            Geometry = Avalonia.Media.Geometry.Parse("M0,0 H24 V24 H0 Z"),
        });
        group.Children.Add(new Avalonia.Media.GeometryDrawing
        {
            Brush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#F2FFFFFF")),
            Geometry = Avalonia.Media.Geometry.Parse(figure),
        });
        return new Avalonia.Media.DrawingImage(group);
    }

    /// <summary>A Poké Ball's icon (PKHeX's ball numbers are the game's); a Poké Ball when unknown.</summary>
    public static Bitmap? Ball(int ball) => ball <= 0 ? null : Load($"balls/_ball{ball}.png") ?? Load("balls/_ball4.png");

    /// <summary>
    /// The box wallpaper as PKHeX shows it: X/Y's set for Gen 6 and 7, Omega Ruby/Alpha Sapphire's own for their last 8
    /// wallpapers.
    /// </summary>
    public static Bitmap? Wallpaper(GameTitle game, int wallpaper)
    {
        int number = wallpaper + 1;
        string suffix = game.Family() == GameFamily.ORAS && number > 16 ? "ao" : "xy";
        return Load($"box/box_wp{number:00}{suffix}.png") ?? Load("box/box_wp16xy.png");
    }
}
