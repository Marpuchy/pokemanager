using Avalonia;
using Avalonia.Media;

namespace Pokemanager.App.Services;

/// <summary>
/// The colour tokens of <c>App.axaml</c> for code that draws or builds brushes itself, read for the variant on screen.
/// Read them when drawing, not into a static field: the variant can change while the application runs.
/// </summary>
public static class Tokens
{
    public static IBrush Brush(string key)
    {
        if (Application.Current is { } app
            && app.TryGetResource(key, app.ActualThemeVariant, out object? value) && value is IBrush brush)
            return brush;
        return Brushes.Gray;
    }

    public static IBrush Text => Brush("TextBrush");
    public static IBrush Muted => Brush("MutedBrush");
    public static IBrush Line => Brush("LineBrush");
    public static IBrush Panel2 => Brush("Panel2Brush");
    public static IBrush Accent => Brush("AccentBrush");
    public static IBrush AccentText => Brush("AccentTextBrush");

    /// <summary>A raised stat (nature): red, lighter on the dark theme so it reads.</summary>
    public static IBrush Raised => new SolidColorBrush(Color.Parse(AppTheme.IsDark ? "#FF7A7D" : "#C42B2F"));

    /// <summary>A lowered stat (nature): blue, lighter on the dark theme so it reads.</summary>
    public static IBrush Lowered => new SolidColorBrush(Color.Parse(AppTheme.IsDark ? "#7EB2FF" : "#1C6FD1"));
}
