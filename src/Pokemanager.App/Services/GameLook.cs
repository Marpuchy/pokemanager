using Avalonia.Media;
using Avalonia.Media.Imaging;
using Pokemanager.Model.Dump;

namespace Pokemanager.App.Services;

/// <summary>
/// What each game looks like in the app: its colour (the trainer card, the start screen's project strips) and the box
/// wallpaper behind the start screen's project list (2026-11-09, user chose "a box wallpaper"). Generation 6 gets its
/// cover legendary, which PKHeX's wallpaper set has; Generation 7 has no Alola art there, so it gets the plain wallpaper
/// closest to the game's colour.
/// </summary>
public static class GameLook
{
    /// <summary>The game's colour: the accent of <see cref="AppTheme"/>, so the start screen and the editor agree.</summary>
    public static Color Color(GameTitle game) => AppTheme.ColorOf(game);

    /// <summary>The wallpaper number (0-based, as the save stores it) shown for a game.</summary>
    public static int WallpaperOf(GameTitle? game) => game switch
    {
        GameTitle.X => 17,             // Xerneas
        GameTitle.Y => 18,             // Yveltal
        GameTitle.OmegaRuby => 17,     // Groudon (ORAS's own set from 17 on)
        GameTitle.AlphaSapphire => 18, // Kyogre
        GameTitle.Sun or GameTitle.UltraSun => 5,   // the warm red pattern
        GameTitle.Moon or GameTitle.UltraMoon => 4, // the purple one
        _ => 12,                       // the Poké Ball
    };

    public static Bitmap? Wallpaper(GameTitle? game) => PkhexImages.Wallpaper(game ?? GameTitle.X, WallpaperOf(game));
}
