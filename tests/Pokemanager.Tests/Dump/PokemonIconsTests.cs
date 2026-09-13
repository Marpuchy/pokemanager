using Pokemanager.Model.Data;

namespace Pokemanager.Tests.Dump;

public class PokemonIconsTests
{
    /// <summary>8×8 palette BCLIM: 2 RGB5A1 colors, one nibble per pixel (high nibble first), Morton order.</summary>
    [Fact]
    public void DecodeBclim_PaletteNibblesInMortonOrder()
    {
        var data = new List<byte> { 0x02, 0x00, 0x02, 0x00 };
        data.AddRange(BitConverter.GetBytes((ushort)0x0000)); // color 0: transparent
        data.AddRange(BitConverter.GetBytes((ushort)0xF801)); // color 1: opaque red
        var nibbles = new byte[32];
        nibbles[0] = 0x01; // pixel 0 → color 0, pixel 1 → color 1
        data.AddRange(nibbles);
        var footer = new byte[0x28];
        "CLIM"u8.CopyTo(footer);
        BitConverter.GetBytes((ushort)8).CopyTo(footer, 0x1C);
        BitConverter.GetBytes((ushort)8).CopyTo(footer, 0x1E);
        footer[0x20] = 0x07; // RGB5A1
        data.AddRange(footer);

        var image = PokemonIcons.DecodeBclim([.. data])!;

        Assert.Equal((8, 8), (image.Width, image.Height));
        // Morton index 1 is (x=1, y=0); index 2 would be (0, 1).
        Assert.Equal([255, 0, 0, 255], image.Rgba[4..8]);
        Assert.Equal(0, image.Rgba[3]);
        Assert.Equal(0, image.Rgba[(8 * 4) + 3]);
    }

    [Fact]
    public void NotABclim_ReturnsNull() => Assert.Null(PokemonIcons.DecodeBclim(new byte[100]));

    [Fact]
    public void RealDump_SpeciesFormsAndGenderIcons()
    {
        string? dump = Environment.GetEnvironmentVariable("POKEMANAGER_DUMP");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(dump), "POKEMANAGER_DUMP is not set.");

        var icons = PokemonIcons.Load(new RomFsLayers(Path.Combine(dump!, "romfs")), Path.Combine(dump!, "exefs", "code.bin"));

        Assert.NotNull(icons);
        var pikachu = icons!.For(25)!;
        Assert.Equal((40, 30), (pikachu.Width, pikachu.Height));
        Assert.Contains(Enumerable.Range(0, pikachu.Width * pikachu.Height), i => pikachu.Rgba[(i * 4) + 3] == 255);
        // Measured on Pokémon X: forms and female variants have their own icons, not the species number.
        Assert.Equal(29, icons.IndexFor(25));
        Assert.Equal(9, icons.IndexFor(6));
        Assert.Equal([7, 8], new[] { icons.IndexFor(6, 1), icons.IndexFor(6, 2) });
        Assert.NotEqual(icons.IndexFor(521), icons.IndexFor(521, female: true));
        Assert.Equal(-1, icons.IndexFor(9999));
    }
}
