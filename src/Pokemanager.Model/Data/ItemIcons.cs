using pk3DS.Core.CTR;
using Pokemanager.Model.Dump;

namespace Pokemanager.Model.Data;

/// <summary>
/// Item icons from the user's own romfs (Generation 7: <c>a/0/6/1</c>, 769 LZ11-compressed BFLIM images of 32×32), used
/// for the Z-crystals of the trials — the game draws each one with the symbol of its type inside.
/// </summary>
public static class ItemIcons
{
    /// <summary>Type crystals in a game: the eighteen types plus the ten Pokémon-exclusive ones.</summary>
    private const int CrystalCount = 28;

    public const int TypeCount = 18;

    /// <summary>
    /// The eighteen type crystals, in the game's type order (Normal, Fire, Water, Electric, Grass, Ice, Fighting, Poison,
    /// Ground, Flying, Psychic, Bug, Rock, Ghost, Dragon, Dark, Steel, Fairy — the order of the Pokédex, not the order
    /// types have in battle data), or null when the archive has none.
    /// </summary>
    public static IconImage?[]? LoadTypeCrystals(RomFsLayers layers, GameTitle title)
    {
        foreach (string path in title.Layout().ItemIcons)
        {
            try
            {
                var garc = new GARC.MemGARC(File.ReadAllBytes(layers.Resolve(path)));
                if (FindCrystals(garc) is { } crystals)
                    return crystals;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or IndexOutOfRangeException or FileNotFoundException)
            {
                // Not the archive we are after: try the next candidate.
            }
        }
        return null;
    }

    /// <summary>
    /// The crystals are consecutive in the archive and all share the same silhouette, so they are found as the longest
    /// run of entries with the same number of opaque pixels (checked on Ultra Moon: 28 entries from icon 661, the type
    /// ones first in Pokédex order). Discs of TMs make shorter runs, so the longest one is the crystals.
    /// </summary>
    private static IconImage?[]? FindCrystals(GARC.MemGARC garc)
    {
        var images = new IconImage?[garc.FileCount];
        var pixels = new int[garc.FileCount];
        for (int i = 0; i < garc.FileCount; i++)
        {
            images[i] = Decode(garc, i);
            pixels[i] = images[i] is { } image ? Opaque(image) : -1;
        }

        int bestStart = -1, bestLength = 0;
        for (int start = 0; start < pixels.Length;)
        {
            int end = start;
            while (end + 1 < pixels.Length && pixels[end + 1] == pixels[start] && pixels[start] > 0)
                end++;
            if (end - start + 1 > bestLength)
            {
                bestLength = end - start + 1;
                bestStart = start;
            }
            start = end + 1;
        }

        if (bestStart < 0 || bestLength < CrystalCount)
            return null;
        return [.. Enumerable.Range(bestStart, TypeCount).Select(i => images[i] is { } image ? Trim(image) : null)];
    }

    /// <summary>
    /// The icon without its transparent border: the crystals are drawn small inside the 32×32 canvas, and shown as they are
    /// they would float in the middle of their slot.
    /// </summary>
    private static IconImage Trim(IconImage image)
    {
        int left = image.Width, top = image.Height, right = -1, bottom = -1;
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                if (image.Rgba[((y * image.Width) + x) * 4 + 3] < 16)
                    continue;
                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
            }
        }
        if (right < left || bottom < top)
            return image;

        int width = right - left + 1, height = bottom - top + 1;
        var rgba = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
            Array.Copy(image.Rgba, (((top + y) * image.Width) + left) * 4, rgba, y * width * 4, width * 4);
        return new IconImage(width, height, rgba);
    }

    private static int Opaque(IconImage image)
    {
        int count = 0;
        for (int i = 3; i < image.Rgba.Length; i += 4)
        {
            if (image.Rgba[i] >= 128)
                count++;
        }
        return count;
    }

    private static IconImage? Decode(GARC.MemGARC garc, int index)
    {
        byte[] file = garc.GetFile(index);
        if (file.Length > 0 && file[0] == 0x11)
        {
            using var input = new MemoryStream(file);
            using var output = new MemoryStream();
            LZSS.Decompress(input, file.Length, output);
            file = output.ToArray();
        }
        return PokemonIcons.DecodeBclim(file);
    }
}
