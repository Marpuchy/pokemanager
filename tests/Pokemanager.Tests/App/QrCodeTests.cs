using Pokemanager.App.Services;
using Xunit;

namespace Pokemanager.Tests.App;

public class QrCodeTests
{
    [Theory]
    [InlineData("http://192.168.1.34:8642/", 25)]
    [InlineData("http://192.168.100.200:51234/pokedex", 29)]
    public void A_link_fits_a_small_code_with_its_finder_patterns(string text, int size)
    {
        var m = QrCode.Encode(text)!;
        Assert.Equal(size, m.GetLength(0));
        // The three finder patterns: a dark ring around a light ring around a dark 3×3 centre.
        foreach (var (x, y) in new[] { (0, 0), (size - 7, 0), (0, size - 7) })
        {
            Assert.True(m[y, x] && m[y + 6, x + 6] && m[y + 3, x + 3]);
            Assert.False(m[y + 1, x + 1]);
        }
    }

    [Fact]
    public void Too_long_a_text_gives_no_code() => Assert.Null(QrCode.Encode(new string('a', 400)));
}
