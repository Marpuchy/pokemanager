using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Pokemanager.App.Controls;

/// <summary>A segment of the wheel: its share of the circle is its weight over the total.</summary>
public sealed record WheelSegment(string Label, double Weight, Color Color);

/// <summary>
/// A prize wheel. Segments start at the top and go clockwise; <see cref="Angle"/> rotates the whole wheel clockwise, so
/// the segment under a pointer at the top is the one that contains <c>360 − Angle</c>.
/// </summary>
public sealed class WheelControl : Control
{
    public static readonly StyledProperty<IReadOnlyList<WheelSegment>?> SegmentsProperty =
        AvaloniaProperty.Register<WheelControl, IReadOnlyList<WheelSegment>?>(nameof(Segments));

    public static readonly StyledProperty<double> AngleProperty =
        AvaloniaProperty.Register<WheelControl, double>(nameof(Angle));

    static WheelControl() => AffectsRender<WheelControl>(SegmentsProperty, AngleProperty);

    public IReadOnlyList<WheelSegment>? Segments
    {
        get => GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    public double Angle
    {
        get => GetValue(AngleProperty);
        set => SetValue(AngleProperty, value);
    }

    /// <summary>Start and end angle (degrees, clockwise from the top, unrotated) of each segment.</summary>
    public static IReadOnlyList<(double Start, double End)> Spans(IReadOnlyList<WheelSegment> segments)
    {
        double total = segments.Sum(s => Math.Max(0, s.Weight));
        var spans = new List<(double, double)>();
        double at = 0;
        foreach (var s in segments)
        {
            double sweep = total <= 0 ? 0 : 360 * Math.Max(0, s.Weight) / total;
            spans.Add((at, at + sweep));
            at += sweep;
        }
        return spans;
    }

    public override void Render(DrawingContext context)
    {
        var segments = Segments;
        if (segments is null || segments.Count == 0)
            return;
        double radius = Math.Min(Bounds.Width, Bounds.Height) / 2 - 4;
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var spans = Spans(segments);
        var outline = new Pen(new SolidColorBrush(Color.Parse("#50000000")), 1);

        for (int i = 0; i < segments.Count; i++)
        {
            var (start, end) = spans[i];
            if (end - start <= 0)
                continue;
            double a0 = start + Angle, a1 = end + Angle;
            if (end - start >= 359.99)
            {
                // A single possible prize: an arc from a point to itself draws nothing.
                context.DrawEllipse(new SolidColorBrush(segments[i].Color), outline, center, radius, radius);
            }
            else
            {
                var geometry = new StreamGeometry();
                using (var g = geometry.Open())
                {
                    g.BeginFigure(center, true);
                    g.LineTo(PointAt(center, radius, a0));
                    g.ArcTo(PointAt(center, radius, a1), new Size(radius, radius), 0, end - start > 180, SweepDirection.Clockwise);
                    g.EndFigure(true);
                }
                context.DrawGeometry(new SolidColorBrush(segments[i].Color), outline, geometry);
            }

            // Label along the middle of the segment, from the rim toward the center.
            double mid = (a0 + a1) / 2;
            var text = new FormattedText(segments[i].Label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI", FontStyle.Normal, FontWeight.SemiBold), end - start < 20 ? 10 : 12,
                Luminance(segments[i].Color) < 0.6 ? Brushes.White : Brushes.Black)
            {
                MaxTextWidth = radius * 0.78,
                MaxLineCount = 1,
                Trimming = TextTrimming.CharacterEllipsis,
            };
            var labelCenter = PointAt(center, radius * 0.58, mid);
            // Rotate so the text runs radially; flip on the left half so it is never upside down.
            double rotation = mid - 90;
            if (NormalizeDegrees(mid) > 180)
                rotation += 180;
            using (context.PushTransform(Matrix.CreateTranslation(-text.Width / 2, -text.Height / 2)
                                         * Matrix.CreateRotation(rotation * Math.PI / 180)
                                         * Matrix.CreateTranslation(labelCenter.X, labelCenter.Y)))
                context.DrawText(text, new Point(0, 0));
        }

        context.DrawEllipse(Brushes.White, new Pen(new SolidColorBrush(Color.Parse("#404040")), 2), center, radius * 0.12, radius * 0.12);
        context.DrawEllipse(null, new Pen(new SolidColorBrush(Color.Parse("#404040")), 3), center, radius, radius);
    }

    private static double NormalizeDegrees(double degrees) => ((degrees % 360) + 360) % 360;

    private static Point PointAt(Point center, double radius, double degreesFromTop)
    {
        double rad = (degreesFromTop - 90) * Math.PI / 180;
        return new Point(center.X + (radius * Math.Cos(rad)), center.Y + (radius * Math.Sin(rad)));
    }

    private static double Luminance(Color c) => ((0.299 * c.R) + (0.587 * c.G) + (0.114 * c.B)) / 255;
}
