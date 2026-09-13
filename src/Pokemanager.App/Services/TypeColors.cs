using Avalonia;
using Avalonia.Media;

namespace Pokemanager.App.Services;

/// <summary>
/// The usual colors of the Pokémon types, in the game's type order (Normal, Fighting, Flying, Poison, Ground, Rock, Bug,
/// Ghost, Steel, Fire, Water, Grass, Electric, Psychic, Ice, Dragon, Dark, Fairy), and backgrounds made from them.
/// </summary>
public static class TypeColors
{
    private static readonly Color[] Colors =
    [
        Color.Parse("#A8A77A"), Color.Parse("#C22E28"), Color.Parse("#A98FF3"), Color.Parse("#A33EA1"),
        Color.Parse("#E2BF65"), Color.Parse("#B6A136"), Color.Parse("#A6B91A"), Color.Parse("#735797"),
        Color.Parse("#B7B7CE"), Color.Parse("#EE8130"), Color.Parse("#6390F0"), Color.Parse("#7AC74C"),
        Color.Parse("#F7D02C"), Color.Parse("#F95587"), Color.Parse("#96D9D6"), Color.Parse("#6F35FC"),
        Color.Parse("#705746"), Color.Parse("#D685AD"),
    ];

    private static readonly Color Unknown = Color.Parse("#9E9E9E");

    public static Color Of(int type) => (uint)type < Colors.Length ? Colors[type] : Unknown;

    /// <summary>A little lighter, so text and sprites stay readable on it.</summary>
    private static Color Soft(Color c) => Color.FromRgb((byte)(c.R + ((255 - c.R) * 0.18)), (byte)(c.G + ((255 - c.G) * 0.18)), (byte)(c.B + ((255 - c.B) * 0.18)));

    /// <summary>One type: a solid color. Two types: split diagonally, one color per half.</summary>
    public static IBrush Background(int type1, int type2)
    {
        var first = Soft(Of(type1));
        if (type1 == type2)
            return new SolidColorBrush(first);
        var second = Soft(Of(type2));
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(first, 0), new GradientStop(first, 0.49),
                new GradientStop(second, 0.51), new GradientStop(second, 1),
            },
        };
    }

    public static IBrush Background(int type) => new SolidColorBrush(Soft(Of(type)));

    /// <summary>White on dark type colors, near black on light ones (by average perceived luminance).</summary>
    public static IBrush Foreground(params int[] types)
    {
        double luminance = types.Distinct().Average(t =>
        {
            var c = Soft(Of(t));
            return ((0.299 * c.R) + (0.587 * c.G) + (0.114 * c.B)) / 255;
        });
        return luminance < 0.62 ? Brushes.White : new SolidColorBrush(Color.Parse("#1A1A1A"));
    }
}
