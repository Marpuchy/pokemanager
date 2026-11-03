using Pokemanager.App.Services;
using Pokemanager.Model.Data;

namespace Pokemanager.Tests.App;

/// <summary>Every icon is drawn centred, wherever the game put it on its canvas (user, 2026-11-04).</summary>
public sealed class SpriteCentringTests
{
    private static IconImage Canvas(int width, int height, params (int X, int Y)[] pixels)
    {
        byte[] rgba = new byte[width * height * 4];
        foreach (var (x, y) in pixels)
            rgba[((y * width) + x) * 4 + 3] = 255;
        return new IconImage(width, height, rgba);
    }

    private static (int MinX, int MaxX, int MinY, int MaxY) Box(IconImage image)
    {
        int minX = int.MaxValue, maxX = -1, minY = int.MaxValue, maxY = -1;
        for (int y = 0; y < image.Height; y++)
        for (int x = 0; x < image.Width; x++)
            if (image.Rgba[((y * image.Width) + x) * 4 + 3] != 0)
            {
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
            }
        return (minX, maxX, minY, maxY);
    }

    [Fact]
    public void ASpriteInACorner_EndsUpInTheMiddle()
    {
        // A 10×6 sprite at the far right of Gen 7's 64×32 canvas.
        var image = Canvas(64, 32, (54, 26), (63, 31));

        var centred = PokemonSprites.Centred(image, 40, 30);

        Assert.Equal((15, 24, 12, 17), Box(centred));
    }

    [Fact]
    public void ASpriteWiderThanTheCanvas_KeepsItsCentre()
    {
        var image = Canvas(64, 32, (0, 0), (49, 31), (5, 10), (44, 10)); // 50 wide: 5 lost on each side

        var (minX, maxX, _, _) = Box(PokemonSprites.Centred(image, 40, 30));

        Assert.Equal((0, 39), (minX, maxX));
    }

    [Fact]
    public void AnEmptyIcon_StaysEmpty() =>
        Assert.Equal(-1, Box(PokemonSprites.Centred(Canvas(40, 30), 40, 30)).MaxX);
}
