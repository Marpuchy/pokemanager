using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Pokemanager.App.Controls;

/// <summary>A QR code drawn black on white with its quiet zone (<see cref="Services.QrCode"/>), square, as big as it is given.</summary>
public sealed class QrView : Control
{
    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<QrView, string?>(nameof(Text));

    static QrView() => AffectsRender<QrView>(TextProperty);

    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }

    private string? encoded;
    private bool[,]? modules;

    protected override Size MeasureOverride(Size availableSize)
    {
        double side = double.IsFinite(Width) ? Width : 200;
        return new Size(side, side);
    }

    public override void Render(DrawingContext context)
    {
        if (Text != encoded)
        {
            encoded = Text;
            modules = string.IsNullOrEmpty(Text) ? null : Services.QrCode.Encode(Text);
        }
        if (modules is null)
            return;
        int n = modules.GetLength(0), quiet = 4;
        double side = Math.Min(Bounds.Width, Bounds.Height), cell = Math.Floor(side / (n + 2 * quiet));
        double origin = (side - cell * (n + 2 * quiet)) / 2;
        context.FillRectangle(Brushes.White, new Rect(0, 0, side, side));
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
                if (modules[y, x])
                    context.FillRectangle(Brushes.Black, new Rect(origin + (x + quiet) * cell, origin + (y + quiet) * cell, cell, cell));
    }
}
