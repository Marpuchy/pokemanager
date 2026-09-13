using Avalonia;
using Avalonia.Controls;

namespace Pokemanager.App.Controls;

/// <summary>
/// Flows children top to bottom in as many columns as fit (at least <see cref="MinColumnWidth"/> wide, at most
/// <see cref="MaxColumns"/>), keeping their order and balancing the column heights, so a long list of settings is read
/// across the width instead of by scrolling down.
/// </summary>
public sealed class ColumnFlowPanel : Panel
{
    public static readonly StyledProperty<double> MinColumnWidthProperty =
        AvaloniaProperty.Register<ColumnFlowPanel, double>(nameof(MinColumnWidth), 320);

    public static readonly StyledProperty<int> MaxColumnsProperty =
        AvaloniaProperty.Register<ColumnFlowPanel, int>(nameof(MaxColumns), 5);

    public static readonly StyledProperty<double> ColumnSpacingProperty =
        AvaloniaProperty.Register<ColumnFlowPanel, double>(nameof(ColumnSpacing), 24);

    static ColumnFlowPanel() =>
        AffectsMeasure<ColumnFlowPanel>(MinColumnWidthProperty, MaxColumnsProperty, ColumnSpacingProperty);

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

    public double ColumnSpacing
    {
        get => GetValue(ColumnSpacingProperty);
        set => SetValue(ColumnSpacingProperty, value);
    }

    private List<Control> Visible => Children.Where(c => c.IsVisible).ToList();

    private int Columns(double width, int count)
    {
        if (count == 0)
            return 1;
        int fit = double.IsInfinity(width) ? MaxColumns : (int)Math.Floor((width + ColumnSpacing) / (MinColumnWidth + ColumnSpacing));
        return Math.Clamp(fit, 1, Math.Max(1, Math.Min(MaxColumns, count)));
    }

    private double ColumnWidth(double width, int columns) =>
        double.IsInfinity(width) ? MinColumnWidth : Math.Max(0, (width - (ColumnSpacing * (columns - 1))) / columns);

    /// <summary>Splits the children, in order, into columns of similar height.</summary>
    private static List<List<Control>> Split(List<Control> children, int columns)
    {
        double total = children.Sum(c => c.DesiredSize.Height);
        double target = total / columns;
        var result = new List<List<Control>> { new() };
        double height = 0;
        foreach (var child in children)
        {
            // Start the next column once this one would pass the average height (by more than half the new child).
            if (height > 0 && result.Count < columns && height + (child.DesiredSize.Height / 2) > target)
            {
                result.Add([]);
                height = 0;
            }
            result[^1].Add(child);
            height += child.DesiredSize.Height;
        }
        return result;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var children = Visible;
        int columns = Columns(availableSize.Width, children.Count);
        double columnWidth = ColumnWidth(availableSize.Width, columns);
        foreach (var child in children)
            child.Measure(new Size(columnWidth, double.PositiveInfinity));
        double height = children.Count == 0 ? 0 : Split(children, columns).Max(col => col.Sum(c => c.DesiredSize.Height));
        double width = double.IsInfinity(availableSize.Width) ? (columnWidth * columns) + (ColumnSpacing * (columns - 1)) : availableSize.Width;
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var children = Visible;
        int columns = Columns(finalSize.Width, children.Count);
        double columnWidth = ColumnWidth(finalSize.Width, columns);
        var split = Split(children, columns);
        for (int c = 0; c < split.Count; c++)
        {
            double y = 0;
            foreach (var child in split[c])
            {
                child.Arrange(new Rect(c * (columnWidth + ColumnSpacing), y, columnWidth, child.DesiredSize.Height));
                y += child.DesiredSize.Height;
            }
        }
        return finalSize;
    }
}
