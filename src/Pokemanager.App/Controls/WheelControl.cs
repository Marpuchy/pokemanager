using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Pokemanager.App.Controls;

/// <summary>A segment of the wheel: its share of the circle is its weight over the total.</summary>
/// <param name="Icon">Picture drawn near the rim (an item), or null.</param>
/// <param name="Glyph">Symbol drawn near the rim when there is no picture (♥, ✕), or null.</param>
public sealed record WheelSegment(string Label, double Weight, Color Color, IImage? Icon = null, string? Glyph = null);

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

    /// <summary>Picture in the hub of the wheel.</summary>
    public IImage? CenterIcon { get; set; } = Services.PkhexImages.Ball(4);

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

            // Icon near the rim, upright, on a white disc so any item reads on any color.
            double mid = (a0 + a1) / 2;
            double iconSize = Math.Clamp(radius * (end - start) / 360 * 1.6, 16, 34);
            var iconCenter = PointAt(center, radius * 0.84, mid);
            if (segments[i].Icon is not null || segments[i].Glyph is not null)
            {
                context.DrawEllipse(new SolidColorBrush(Color.Parse("#F2FFFFFF")), new Pen(new SolidColorBrush(Color.Parse("#40000000")), 1),
                    iconCenter, iconSize * 0.62, iconSize * 0.62);
                if (segments[i].Icon is { } icon)
                {
                    using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = Avalonia.Media.Imaging.BitmapInterpolationMode.None }))
                        context.DrawImage(icon, new Rect(iconCenter.X - (iconSize / 2), iconCenter.Y - (iconSize / 2), iconSize, iconSize));
                }
                else
                {
                    var glyph = new FormattedText(segments[i].Glyph!, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                        new Typeface("Segoe UI Symbol", FontStyle.Normal, FontWeight.Bold), iconSize * 0.72,
                        new SolidColorBrush(Color.Parse(segments[i].Glyph == "♥" ? "#D13438" : "#505050")));
                    context.DrawText(glyph, new Point(iconCenter.X - (glyph.Width / 2), iconCenter.Y - (glyph.Height / 2)));
                }
            }

            // Label along the middle of the segment, between the icon and the center.
            var text = new FormattedText(segments[i].Label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI", FontStyle.Normal, FontWeight.SemiBold), end - start < 20 ? 10 : 12,
                Luminance(segments[i].Color) < 0.6 ? Brushes.White : Brushes.Black)
            {
                MaxTextWidth = radius * 0.5,
                MaxLineCount = 1,
                Trimming = TextTrimming.CharacterEllipsis,
            };
            var labelCenter = PointAt(center, radius * 0.45, mid);
            // Rotate so the text runs radially; flip on the left half so it is never upside down.
            double rotation = mid - 90;
            if (NormalizeDegrees(mid) > 180)
                rotation += 180;
            using (context.PushTransform(Matrix.CreateTranslation(-text.Width / 2, -text.Height / 2)
                                         * Matrix.CreateRotation(rotation * Math.PI / 180)
                                         * Matrix.CreateTranslation(labelCenter.X, labelCenter.Y)))
                context.DrawText(text, new Point(0, 0));
        }

        context.DrawEllipse(null, new Pen(new SolidColorBrush(Color.Parse("#404040")), 3), center, radius, radius);
        // A Poké Ball in the middle.
        double hub = radius * 0.13;
        context.DrawEllipse(Brushes.White, new Pen(new SolidColorBrush(Color.Parse("#404040")), 2), center, hub, hub);
        if (CenterIcon is { } ball)
        {
            using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = Avalonia.Media.Imaging.BitmapInterpolationMode.None }))
                context.DrawImage(ball, new Rect(center.X - (hub * 0.8), center.Y - (hub * 0.8), hub * 1.6, hub * 1.6));
        }
    }

    private static double NormalizeDegrees(double degrees) => ((degrees % 360) + 360) % 360;

    private static Point PointAt(Point center, double radius, double degreesFromTop)
    {
        double rad = (degreesFromTop - 90) * Math.PI / 180;
        return new Point(center.X + (radius * Math.Cos(rad)), center.Y + (radius * Math.Sin(rad)));
    }

    private static double Luminance(Color c) => ((0.299 * c.R) + (0.587 * c.G) + (0.114 * c.B)) / 255;
}
