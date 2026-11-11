using Avalonia;
using Avalonia.Controls;

namespace Pokemanager.App.Controls;

/// <summary>
/// Two children: the first at its own width, the second filling the rest beside it — or below it, full width, when
/// the rest would be narrower than <see cref="MinMainWidth"/>. The start screen's team and the picked Pokémon
/// (2026-11-11, user: the data on the right of the team), which at 1024 px has no room beside it.
/// </summary>
public sealed class SidePanel : Panel
{
    public static readonly StyledProperty<double> MinMainWidthProperty =
        AvaloniaProperty.Register<SidePanel, double>(nameof(MinMainWidth), 560);

    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<SidePanel, double>(nameof(Spacing), 8);

    static SidePanel() => AffectsMeasure<SidePanel>(MinMainWidthProperty, SpacingProperty);

    public double MinMainWidth
    {
        get => GetValue(MinMainWidthProperty);
        set => SetValue(MinMainWidthProperty, value);
    }

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    private bool stacked;

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children.Count < 2)
            return Children.Count == 1 ? Measure(Children[0], availableSize) : default;
        var (side, main) = (Children[0], Children[1]);
        side.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        double rest = availableSize.Width - side.DesiredSize.Width - Spacing;
        stacked = rest < MinMainWidth;
        if (stacked)
        {
            main.Measure(new Size(availableSize.Width, double.PositiveInfinity));
            return new Size(availableSize.Width, side.DesiredSize.Height + Spacing + main.DesiredSize.Height);
        }
        main.Measure(new Size(rest, double.PositiveInfinity));
        return new Size(availableSize.Width, Math.Max(side.DesiredSize.Height, main.DesiredSize.Height));
    }

    private static Size Measure(Control c, Size size)
    {
        c.Measure(size);
        return c.DesiredSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children.Count < 2)
        {
            if (Children.Count == 1)
                Children[0].Arrange(new Rect(finalSize));
            return finalSize;
        }
        var (side, main) = (Children[0], Children[1]);
        if (stacked)
        {
            side.Arrange(new Rect(0, 0, finalSize.Width, side.DesiredSize.Height));
            main.Arrange(new Rect(0, side.DesiredSize.Height + Spacing, finalSize.Width, main.DesiredSize.Height));
        }
        else
        {
            double w = side.DesiredSize.Width;
            side.Arrange(new Rect(0, 0, w, finalSize.Height));
            main.Arrange(new Rect(w + Spacing, 0, Math.Max(0, finalSize.Width - w - Spacing), finalSize.Height));
        }
        return finalSize;
    }
}
