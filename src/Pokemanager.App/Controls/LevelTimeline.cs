using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Pokemanager.App.Controls;

/// <summary>Where one level of the timeline goes: its x, and which side of the line (and how far out) its label sits.</summary>
public readonly record struct TimelineMark(int Level, string Label, double X, bool Above, int Tier);

/// <summary>
/// The levels a Pokémon learns moves at, as one line across the whole width, from the first level to the last (user,
/// 2026-11-04: "sin slider, que ocupe todo el horizontal, solamente los niveles, alternando arriba y abajo"). Each level
/// is a dot placed in proportion; the labels alternate above and below the line, and one that would still touch the
/// previous label on its side moves one row further out.
/// </summary>
public sealed class LevelTimeline : Control
{
    public static readonly StyledProperty<IReadOnlyList<int>?> LevelsProperty =
        AvaloniaProperty.Register<LevelTimeline, IReadOnlyList<int>?>(nameof(Levels));

    /// <summary>What level 0 (learnt on evolving) is called.</summary>
    public static readonly StyledProperty<string> EvolutionLabelProperty =
        AvaloniaProperty.Register<LevelTimeline, string>(nameof(EvolutionLabel), "Evo");

    static LevelTimeline() => AffectsRender<LevelTimeline>(LevelsProperty, EvolutionLabelProperty);

    public IReadOnlyList<int>? Levels { get => GetValue(LevelsProperty); set => SetValue(LevelsProperty, value); }
    public string EvolutionLabel { get => GetValue(EvolutionLabelProperty); set => SetValue(EvolutionLabelProperty, value); }

    public const double RowHeight = 17, DotRadius = 4.5, Side = 18, Rows = 2;
    private const double FontSize = 11;

    private static readonly Typeface Face = new(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);
    private static readonly IBrush LineBrush = new SolidColorBrush(Color.Parse("#C9D1DA"));
    private static readonly IBrush DotBrush = new SolidColorBrush(Color.Parse("#1C3A70"));
    private static readonly IBrush TickBrush = new SolidColorBrush(Color.Parse("#9AA6B2"));
    private static readonly IPen LinePen = new Pen(LineBrush, 3, lineCap: PenLineCap.Round);
    private static readonly IPen TickPen = new Pen(TickBrush, 1);

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 400 : availableSize.Width, 2 * Rows * RowHeight + 8);

    /// <summary>
    /// The marks of <paramref name="levels"/> over <paramref name="width"/> pixels: x in proportion between the first
    /// and the last level (<see cref="Side"/> kept free at each end), labels alternating sides, a label pushed one row
    /// out when it would overlap the last one on that row.
    /// </summary>
    public static IReadOnlyList<TimelineMark> Layout(IEnumerable<int> levels, double width, Func<int, string> label, Func<string, double> textWidth)
    {
        var distinct = levels.Distinct().Order().ToList();
        var marks = new List<TimelineMark>();
        if (distinct.Count == 0)
            return marks;
        int first = distinct[0], last = distinct[^1];
        double span = Math.Max(1, width - 2 * Side);
        // The right edge of the last label on each (side, row).
        var ends = new double[2, (int)Rows];
        for (int s = 0; s < 2; s++)
            for (int r = 0; r < Rows; r++)
                ends[s, r] = double.NegativeInfinity;
        for (int i = 0; i < distinct.Count; i++)
        {
            int level = distinct[i];
            double x = last == first ? width / 2 : Side + (level - first) * span / (last - first);
            string text = label(level);
            double half = textWidth(text) / 2 + 3;
            bool above = i % 2 == 0;
            int side = above ? 0 : 1, tier = 0;
            // Its row, or the next one out; when every row on both sides is taken it goes where it overlaps least.
            int bestSide = side, bestTier = 0;
            double bestOverlap = double.PositiveInfinity;
            foreach (int s in new[] { side, 1 - side })
                for (int r = 0; r < Rows; r++)
                {
                    double overlap = ends[s, r] - (x - half);
                    if (overlap <= 0) { bestSide = s; bestTier = r; bestOverlap = 0; goto placed; }
                    if (overlap < bestOverlap) { bestOverlap = overlap; bestSide = s; bestTier = r; }
                }
            placed:
            side = bestSide; tier = bestTier;
            ends[side, tier] = x + half;
            marks.Add(new TimelineMark(level, text, x, side == 0, tier));
        }
        return marks;
    }

    public override void Render(DrawingContext context)
    {
        if (Levels is not { Count: > 0 } levels)
            return;
        double width = Bounds.Width, mid = Bounds.Height / 2;
        var marks = Layout(levels, width, Label, t => Text(t).Width);
        context.DrawLine(LinePen, new Point(marks[0].X, mid), new Point(marks[^1].X, mid));
        foreach (var m in marks)
        {
            var text = Text(m.Label);
            double distance = DotRadius + 3 + m.Tier * RowHeight;
            double top = m.Above ? mid - distance - text.Height : mid + distance;
            if (m.Tier > 0)
            {
                double near = m.Above ? mid - DotRadius : mid + DotRadius;
                double far = m.Above ? top + text.Height : top;
                context.DrawLine(TickPen, new Point(m.X, near), new Point(m.X, far));
            }
            context.DrawText(text, new Point(m.X - text.Width / 2, top));
            context.DrawEllipse(DotBrush, null, new Point(m.X, mid), DotRadius, DotRadius);
        }
    }

    private string Label(int level) => level == 0 ? EvolutionLabel : level.ToString(CultureInfo.InvariantCulture);

    private static FormattedText Text(string text) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face, FontSize, DotBrush);
}
