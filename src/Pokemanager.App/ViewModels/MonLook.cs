using System.Collections;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.Input;
using Pokemanager.App.Resources;

namespace Pokemanager.App.ViewModels;

/// <summary>
/// A Pokémon as a party card or a box cell. The save editor, the project preview and multiplayer rooms all show their
/// Pokémon through this, with the same templates (<c>Styles/PokemonLook.axaml</c>), so they look the same everywhere.
/// </summary>
public interface IMonCard
{
    Bitmap? Icon { get; }
    string Name { get; }
    string GenderText { get; }
    string LevelText { get; }
    bool IsShiny { get; }
    bool IsEgg { get; }
    bool IsEmpty { get; }
    Bitmap? ItemIcon { get; }
    bool HasItem { get; }
    IBrush TypeBackground { get; }
    IBrush TypeForeground { get; }
    IReadOnlyList<TypeChip> TypeChips { get; }
    bool IsSelected { get; }
    bool HasProblem { get; }
    string Tooltip { get; }
    IRelayCommand SelectCommand { get; }
}

/// <summary>A box shown as in PKHeX: ◀ name ▶, the game's wallpaper and 6×5 cells (<see cref="Controls.BoxBrowser"/>).</summary>
public interface IBoxBrowser
{
    IEnumerable BoxNames { get; }
    int SelectedBox { get; set; }
    IRelayCommand PreviousBoxCommand { get; }
    IRelayCommand NextBoxCommand { get; }
    Bitmap? BoxWallpaper { get; }
    IEnumerable BoxSlots { get; }
}

/// <summary>Showdown's colors for a base stat bar.</summary>
public static class StatBars
{
    public static IBrush Brush(int value) => new SolidColorBrush(Color.Parse(value switch
    {
        < 30 => "#F34444", < 60 => "#FF7F0F", < 90 => "#FFDD57", < 120 => "#A0E515", < 150 => "#23CD5E", _ => "#00C2B8",
    }));

    public static double Width(int value, double full) => Math.Max(2, Math.Min(value, 200) / 200.0 * full);

    /// <summary>
    /// +1 when the nature raises the stat, -1 when it lowers it. Natures are 5 × raised + lowered in the order Atk, Def, Spe,
    /// SpA, SpD; <paramref name="row"/> is the display order HP, Atk, Def, SpA, SpD, Spe.
    /// </summary>
    public static int NatureEffect(int nature, int row)
    {
        int up = nature / 5, down = nature % 5;
        if (up == down || row <= 0 || row > 5)
            return 0;
        int order = row switch { 1 => 0, 2 => 1, 3 => 3, 4 => 4, _ => 2 };
        return order == up ? 1 : order == down ? -1 : 0;
    }
}

/// <summary>A stat in a read-only sheet: the value, and base/IV/EV when the viewer knows them.</summary>
/// <param name="ScaleMax">Without the base stat: the highest stat of the Pokémon, which fills the bar.</param>
/// <param name="Nature">+1 raised by the nature (red label ▲), -1 lowered (blue ▼), as the save editor.</param>
public sealed record SheetStat(string Label, int Value, int? Base = null, int? Iv = null, int? Ev = null, int ScaleMax = 0, int Nature = 0)
{
    public IBrush LabelBrush => Nature > 0 ? Raised : Nature < 0 ? Lowered : Plain;
    public string NatureMark => Nature > 0 ? "▲" : Nature < 0 ? "▼" : "";

    private static readonly IBrush Raised = new SolidColorBrush(Color.Parse("#D13438"));
    private static readonly IBrush Lowered = new SolidColorBrush(Color.Parse("#1C6FD1"));
    private static readonly IBrush Plain = new SolidColorBrush(Color.Parse("#1A1A1A"));

    public bool HasBase => Base is not null;
    public bool HasTraining => Iv is not null;
    public string BaseText => Base?.ToString() ?? "";
    public string IvText => Iv?.ToString() ?? "";
    public string EvText => Ev?.ToString() ?? "";

    /// <summary>The bar shows the base stat (Showdown's colors) when known, else the stat against the highest one, in a neutral color.</summary>
    public double BarWidth => Base is { } b ? StatBars.Width(b, 110) : Math.Max(2, (double)Value / Math.Max(1, ScaleMax) * 110);

    /// <summary>The same bar as a fraction of the available width (0–1), so it shrinks with narrow cards.</summary>
    public double BarFraction => Base is { } b ? Math.Min(b, 200) / 200.0 : (double)Value / Math.Max(1, ScaleMax);

    public IBrush BarBrush => Base is { } b ? StatBars.Brush(b) : Neutral;

    private static readonly IBrush Neutral = new SolidColorBrush(Color.Parse("#5B8FD6"));
}

/// <summary>
/// A Pokémon read only — the project preview and the other players in a room — laid out as the save editor's page:
/// header in the type colors, then stats, moves and data.
/// </summary>
public sealed class MonSheet(IMonCard card, string subtitle, Bitmap? ballIcon, IReadOnlyList<SheetStat> stats, IReadOnlyList<MoveLine> moves,
    IReadOnlyList<RoomInfoLine> info, string? statsNote = null, IRelayCommand? action = null, string? actionText = null)
{
    public IMonCard Card { get; } = card;
    public string Subtitle { get; } = subtitle;
    public Bitmap? BallIcon { get; } = ballIcon;
    public bool HasBall => BallIcon is not null;
    public IReadOnlyList<SheetStat> Stats { get; } = stats;
    public bool StatsHaveBase => Stats.Any(s => s.HasBase);
    public bool StatsHaveTraining => Stats.Any(s => s.HasTraining);
    public int StatTotal => Stats.Sum(s => s.Base ?? 0);
    public bool HasStatTotal => StatsHaveBase;
    public IReadOnlyList<MoveLine> Moves { get; } = moves;
    public IReadOnlyList<RoomInfoLine> Info { get; } = info;
    public string? StatsNote { get; } = statsNote;
    public bool HasStatsNote => !string.IsNullOrEmpty(StatsNote);
    public IRelayCommand? Action { get; } = action;
    public string ActionText { get; } = actionText ?? "";
    public bool HasAction => Action is not null;
    public string TotalLabel => Strings.Badge_Total;
}
