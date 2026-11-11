using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Pokemanager.App.Controls;

/// <summary>A small chevron, drawn pointing right and turned by <see cref="Angle"/>: the fold arrow of the panels.</summary>
public sealed class Chevron : Control
{
    public static readonly StyledProperty<double> AngleProperty = AvaloniaProperty.Register<Chevron, double>(nameof(Angle));
    public static readonly StyledProperty<IBrush?> ForegroundProperty = AvaloniaProperty.Register<Chevron, IBrush?>(nameof(Foreground));

    static Chevron() => AffectsRender<Chevron>(AngleProperty, ForegroundProperty);

    public double Angle
    {
        get => GetValue(AngleProperty);
        set => SetValue(AngleProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(10, 10);

    public override void Render(DrawingContext context)
    {
        var c = new Point(Bounds.Width / 2, Bounds.Height / 2);
        double a = Angle * Math.PI / 180;
        Point Turn(double x, double y) => new(c.X + x * Math.Cos(a) - y * Math.Sin(a), c.Y + x * Math.Sin(a) + y * Math.Cos(a));
        var pen = new Pen(Foreground ?? Brushes.Gray, 1.6, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        context.DrawLine(pen, Turn(-2, -4), Turn(2, 0));
        context.DrawLine(pen, Turn(2, 0), Turn(-2, 4));
    }
}
