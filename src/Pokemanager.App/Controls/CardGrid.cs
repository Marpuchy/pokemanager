using Avalonia;
using Avalonia.Controls;

namespace Pokemanager.App.Controls;

/// <summary>
/// Cards in even rows (2026-11-11, user: blocks of the same tab "que ocupen lo mismo" in width and height, laid out "lo
/// mejor posible", reordered when nothing says one goes first). As many equal columns as fit at
/// <see cref="MinColumnWidth"/> (up to <see cref="MaxColumns"/>); every card of a row is as tall as the tallest one; and
/// when the cards do not fill the last row, the tallest ones go there and share its whole width, so a card whose
/// options flow in columns spreads them across instead of leaving a hole. One card alone (the panel gets the class
/// <c>solo</c>) is drawn without its frame: a block around the only options of a page adds nothing.
/// </summary>
public sealed class CardGrid : Panel
{
    public static readonly StyledProperty<double> MinColumnWidthProperty =
        AvaloniaProperty.Register<CardGrid, double>(nameof(MinColumnWidth), 340);

    public static readonly StyledProperty<int> MaxColumnsProperty =
        AvaloniaProperty.Register<CardGrid, int>(nameof(MaxColumns), 3);

    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<CardGrid, double>(nameof(Spacing), 12);

    /// <summary>Keeps a card in its place (and out of the last row's reordering): the first card of a page, say.</summary>
    public static readonly AttachedProperty<bool> FixedProperty =
        AvaloniaProperty.RegisterAttached<CardGrid, Control, bool>("Fixed");

    public static bool GetFixed(Control control) => control.GetValue(FixedProperty);
    public static void SetFixed(Control control, bool value) => control.SetValue(FixedProperty, value);

    /// <summary>Fixed on the card itself, or on what an items control's presenter holds.</summary>
    private static bool IsFixed(Control card) =>
        GetFixed(card) || card is Avalonia.Controls.Presenters.ContentPresenter { Child: { } child } && GetFixed(child);

    static CardGrid() => AffectsMeasure<CardGrid>(MinColumnWidthProperty, MaxColumnsProperty, SpacingProperty);

    public double MinColumnWidth
    {
        get => GetValue(MinColumnWidthProperty);
        set => SetValue(MinColumnWidthProperty, value);
    }

    public int MaxColumns
    {
        get => GetValue(MaxColumnsProperty);
        set => SetValue(MaxColumnsProperty, value);
    }

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>The layout worked out by the last measure: each row's cards, their width and the row's height.</summary>
    private List<(List<Control> Cards, double Width, double Height)> rows = [];

    protected override Size MeasureOverride(Size availableSize)
    {
        var cards = Children.Where(c => c.IsVisible).ToList();
        Classes.Set("solo", cards.Count == 1);
        rows = [];
        if (cards.Count == 0)
            return default;

        double gap = Spacing;
        double width = double.IsFinite(availableSize.Width)
            ? availableSize.Width
            : MaxColumns * MinColumnWidth + (MaxColumns - 1) * gap;
        int columns = Math.Clamp((int)((width + gap) / (MinColumnWidth + gap)), 1, Math.Max(1, MaxColumns));
        columns = Math.Min(columns, cards.Count);
        double columnWidth = (width - (columns - 1) * gap) / columns;

        // Each card's height in one column, to pick the tallest for a short last row.
        var tall = new Dictionary<Control, double>();
        foreach (var card in cards)
        {
            card.Measure(new Size(columnWidth, double.PositiveInfinity));
            tall[card] = card.DesiredSize.Height;
        }

        // Cards of similar height share a row (fewer gaps under the short ones); the tallest go to a short last row,
        // where they get its whole width. Fixed cards keep their place at the start.
        int last = cards.Count % columns;
        var fixedCards = cards.Where(IsFixed).ToList();
        var rest = cards.Where(c => !IsFixed(c)).OrderByDescending(c => tall[c]).ToList();
        var order = last != 0 && rest.Count >= last
            ? [.. fixedCards, .. rest.Skip(last), .. rest.Take(last)]
            : new List<Control>([.. fixedCards, .. rest]);

        double total = 0;
        for (int i = 0; i < order.Count; i += columns)
        {
            var row = order.Skip(i).Take(columns).ToList();
            double rowWidth = (width - (row.Count - 1) * gap) / row.Count;
            double height = 0;
            foreach (var card in row)
            {
                if (row.Count != columns)
                    card.Measure(new Size(rowWidth, double.PositiveInfinity));
                height = Math.Max(height, card.DesiredSize.Height);
            }
            rows.Add((row, rowWidth, height));
            total += height + (rows.Count > 1 ? gap : 0);
        }
        return new Size(width, total);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double y = 0;
        foreach (var (cards, width, height) in rows)
        {
            double x = 0;
            foreach (var card in cards)
            {
                card.Arrange(new Rect(x, y, width, height));
                x += width + Spacing;
            }
            y += height + Spacing;
        }
        foreach (var hidden in Children.Where(c => !c.IsVisible))
            hidden.Arrange(default);
        return finalSize;
    }
}
