using Avalonia;
using Avalonia.Controls;

namespace Pokemanager.App.Controls;

/// <summary>
/// Lays children out in as many equal columns as fit (between 1 and <see cref="MaxColumns"/>), row by row, so a page
/// uses the width of wide windows and stacks into one column on narrow ones.
/// </summary>
public sealed class AdaptiveColumnsPanel : Panel
{
    public static readonly StyledProperty<double> MinColumnWidthProperty =
        AvaloniaProperty.Register<AdaptiveColumnsPanel, double>(nameof(MinColumnWidth), 380);

    public static readonly StyledProperty<int> MaxColumnsProperty =
        AvaloniaProperty.Register<AdaptiveColumnsPanel, int>(nameof(MaxColumns), 3);

    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<AdaptiveColumnsPanel, double>(nameof(Spacing), 12);

    static AdaptiveColumnsPanel() =>
        AffectsMeasure<AdaptiveColumnsPanel>(MinColumnWidthProperty, MaxColumnsProperty, SpacingProperty);

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

    private int Columns(double width)
    {
        var visible = Children.Count(c => c.IsVisible);
        if (double.IsInfinity(width))
            return Math.Max(1, Math.Min(MaxColumns, visible));
        int fit = (int)Math.Floor((width + Spacing) / (MinColumnWidth + Spacing));
        return Math.Clamp(fit, 1, Math.Max(1, Math.Min(MaxColumns, visible)));
    }

    private double ColumnWidth(double width, int columns) =>
        double.IsInfinity(width) ? MinColumnWidth : Math.Max(0, (width - (Spacing * (columns - 1))) / columns);

    protected override Size MeasureOverride(Size availableSize)
    {
        int columns = Columns(availableSize.Width);
        double columnWidth = ColumnWidth(availableSize.Width, columns);
        double total = 0, row = 0;
        int index = 0;
        foreach (var child in Children.Where(c => c.IsVisible))
        {
            child.Measure(new Size(columnWidth, double.PositiveInfinity));
            row = Math.Max(row, child.DesiredSize.Height);
            if (++index % columns == 0)
            {
                total += row + Spacing;
                row = 0;
            }
        }
        total += row > 0 ? row : -Spacing;
        double width = double.IsInfinity(availableSize.Width) ? (columnWidth * columns) + (Spacing * (columns - 1)) : availableSize.Width;
        return new Size(width, Math.Max(0, total));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        int columns = Columns(finalSize.Width);
        double columnWidth = ColumnWidth(finalSize.Width, columns);
        var visible = Children.Where(c => c.IsVisible).ToList();
        double y = 0;
        for (int start = 0; start < visible.Count; start += columns)
        {
            var rowChildren = visible.Skip(start).Take(columns).ToList();
            double rowHeight = rowChildren.Max(c => c.DesiredSize.Height);
            for (int i = 0; i < rowChildren.Count; i++)
                rowChildren[i].Arrange(new Rect(i * (columnWidth + Spacing), y, columnWidth, rowHeight));
            y += rowHeight + Spacing;
        }
        return finalSize;
    }
}
