using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Pokemanager.Model.Dump;

namespace Pokemanager.App.Services;

/// <summary>The background behind the pages (2026-11-11, user: "brillo del juego" by default, others in Ajustes).</summary>
public enum AppBackground
{
    /// <summary>A soft glow of the game's colour from the top-left corner over the slate gradient.</summary>
    Glow,

    /// <summary>The glow plus a faint hexagon grid, like the Pokédex screens.</summary>
    Hexagons,

    /// <summary>The gradient plus faint Poké Ball outlines.</summary>
    PokeBalls,

    /// <summary>The glow plus fine diagonal lines in the game's colour.</summary>
    Lines,

    /// <summary>Only the slate gradient.</summary>
    Plain,
}

/// <summary>
/// The look of the whole application (2026-11-11, user chose "Grafito + color de tu juego"): dark or light, the accent
/// taken from the game being edited, and the background. Everything else is the tokens of <c>App.axaml</c>; this only
/// sets the variant and the brushes that depend on the game, as application resources, so every
/// <c>DynamicResource</c> follows at once.
/// </summary>
public static class AppTheme
{
    /// <summary>The accent when no game is on screen (the start page with nothing selected).</summary>
    public static readonly Color Neutral = Color.Parse("#3B82F6");

    private static Color accent = Neutral;
    private static bool dark = true;
    private static AppBackground background = AppBackground.Glow;

    /// <summary>The colour of each game, from its box and logo: Ultra Sun orange, Ultra Moon violet, X blue, Y red…</summary>
    public static Color ColorOf(GameTitle game) => Color.Parse(game switch
    {
        GameTitle.X => "#2F7FE0",
        GameTitle.Y => "#E0414B",
        GameTitle.OmegaRuby => "#D9483B",
        GameTitle.AlphaSapphire => "#2F77D0",
        GameTitle.Sun => "#F29A2E",
        GameTitle.Moon => "#7B6CF0",
        GameTitle.UltraSun => "#F28C28",
        GameTitle.UltraMoon => "#9B6CF2",
        _ => "#3B82F6",
    });

    public static bool IsDark => dark;

    /// <summary>Applies the user's settings (variant and background) and repaints.</summary>
    public static void Configure(bool darkMode, AppBackground kind)
    {
        dark = darkMode;
        background = kind;
        if (Application.Current is { } app)
            app.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        Apply();
    }

    /// <summary>The game on screen changed (a project opened or was picked): its colour becomes the accent.</summary>
    public static void UseGame(GameTitle? game)
    {
        accent = game is { } g ? ColorOf(g) : Neutral;
        Apply();
    }

    private static void Apply()
    {
        if (Application.Current is not { } app)
            return;
        var resources = app.Resources;
        resources["AccentBrush"] = new SolidColorBrush(accent);
        resources["AccentSoftBrush"] = new SolidColorBrush(WithAlpha(accent, (byte)(dark ? 0x2E : 0x24)));
        // Text in the accent colour: lighter on the dark slate, darker on white, so it keeps 4.5:1.
        resources["AccentTextBrush"] = new SolidColorBrush(dark ? Mix(accent, Colors.White, 0.35) : Mix(accent, Colors.Black, 0.35));
        // Text on a filled accent: white or a near-black ink, whichever contrasts more (orange takes the ink).
        var ink = Color.Parse("#16130F");
        resources["AccentInkBrush"] = new SolidColorBrush(Contrast(Colors.White, accent) >= Contrast(ink, accent) ? Colors.White : ink);
        resources["AppGlowBrush"] = Glow();
        resources["BarGlowBrush"] = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(WithAlpha(accent, (byte)(dark ? 0x38 : 0x2A)), 0),
                new GradientStop(WithAlpha(accent, 0), 0.6),
            },
        };
        resources["AppPatternBrush"] = Pattern();
    }

    /// <summary>The glow of the game's colour from the top-left corner (transparent for Poké Balls and Plain).</summary>
    private static IBrush Glow()
    {
        if (background is AppBackground.Plain or AppBackground.PokeBalls)
            return Brushes.Transparent;
        return new RadialGradientBrush
        {
            Center = new RelativePoint(0, 0, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0, 0, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.9, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.75, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(WithAlpha(accent, (byte)(dark ? 0x3A : 0x30)), 0),
                new GradientStop(WithAlpha(accent, 0), 1),
            },
        };
    }

    /// <summary>A tile repeated over the whole window, drawn in the game's colour at a very low opacity.</summary>
    private static IBrush Pattern()
    {
        byte alpha = (byte)(dark ? 0x1C : 0x22);
        var pen = new Pen(new SolidColorBrush(WithAlpha(accent, alpha)), 1);
        DrawingGroup tile;
        Rect size;
        switch (background)
        {
            case AppBackground.Hexagons:
                size = new Rect(0, 0, 56, 48);
                tile = Tile(size, new GeometryDrawing { Pen = pen, Geometry = Geometry.Parse("M14,0 L42,0 L56,24 L42,48 L14,48 L0,24 Z") });
                break;
            case AppBackground.PokeBalls:
                size = new Rect(0, 0, 90, 90);
                var ball = new Pen(new SolidColorBrush(WithAlpha(accent, (byte)(alpha + 8))), 1.5);
                tile = Tile(size,
                    new GeometryDrawing { Pen = ball, Geometry = new EllipseGeometry(new Rect(9, 9, 26, 26)) },
                    new GeometryDrawing { Pen = ball, Geometry = Geometry.Parse("M9,22 L35,22") },
                    new GeometryDrawing { Pen = ball, Geometry = new EllipseGeometry(new Rect(18, 18, 8, 8)) },
                    new GeometryDrawing { Pen = ball, Geometry = new EllipseGeometry(new Rect(54, 54, 26, 26)) },
                    new GeometryDrawing { Pen = ball, Geometry = Geometry.Parse("M54,67 L80,67") },
                    new GeometryDrawing { Pen = ball, Geometry = new EllipseGeometry(new Rect(63, 63, 8, 8)) });
                break;
            case AppBackground.Lines:
                size = new Rect(0, 0, 12, 12);
                tile = Tile(size, new GeometryDrawing { Pen = pen, Geometry = Geometry.Parse("M0,12 L12,0") });
                break;
            default:
                return Brushes.Transparent;
        }
        return new DrawingBrush(tile)
        {
            TileMode = TileMode.Tile,
            Stretch = Stretch.None,
            DestinationRect = new RelativeRect(size, RelativeUnit.Absolute),
            SourceRect = new RelativeRect(size, RelativeUnit.Absolute),
        };
    }

    private static DrawingGroup Tile(Rect size, params Drawing[] drawings)
    {
        var group = new DrawingGroup();
        // A transparent rectangle the size of the tile, so every tile is exactly that big.
        group.Children.Add(new GeometryDrawing { Brush = Brushes.Transparent, Geometry = new RectangleGeometry(size) });
        foreach (var drawing in drawings)
            group.Children.Add(drawing);
        return group;
    }

    private static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

    private static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    public static double Contrast(Color a, Color b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(Color c)
    {
        static double Channel(byte v) => v / 255.0 is var x && x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4);
        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }
}
