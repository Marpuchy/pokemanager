using pk3DS.Core.CTR;

namespace Pokemanager.Model.Data;

/// <summary>A decoded image: <see cref="Rgba"/> holds 4 bytes per pixel, rows top to bottom.</summary>
public sealed record IconImage(int Width, int Height, byte[] Rgba);

/// <summary>
/// Pokémon box icons read from the user's own romfs (<c>a/0/9/3</c> in X/Y: LZ11-compressed BCLIM images, 40×30).
/// Nothing is bundled with the application.
/// </summary>
/// <remarks>
/// BCLIM stores pixels in 8×8 tiles in Morton (Z) order. Gen 6 icons use a palette: pixel data starts with
/// <c>0x0002</c>, the color count and RGB5A1 colors, then one byte per pixel, or a nibble (high first) for up to 16
/// colors.
/// <para>
/// Which icon belongs to a species is not the species number: the game keeps a table in <c>code.bin</c> with one
/// 16-byte entry per species — default icon, female icon, pointer to the per-form icons, pointer to a second per-form list
/// (differs only for a few species; used for shiny), and both counts. It is found by content, not by a fixed address.
/// </para>
/// </remarks>
public sealed class PokemonIcons
{
    public const string Garc = "a/0/9/3";
    public const int SpeciesCount = 722;
    private const int EntrySize = 16;
    private const uint CodeBase = 0x100000;

    private readonly GARC.MemGARC garc;
    private readonly IconEntry[] entries;
    private readonly Dictionary<int, IconImage?> cache = [];

    public int Count => garc.FileCount;

    /// <summary>Icon indexes of a species: default, female, per form and per form for shiny.</summary>
    private sealed record IconEntry(int Default, int Female, int[] Forms, int[] ShinyForms);

    private PokemonIcons(GARC.MemGARC garc, IconEntry[] entries)
    {
        this.garc = garc;
        this.entries = entries;
    }

    /// <summary>Null when the dump has no icon archive or the species table cannot be found in <c>code.bin</c>.</summary>
    public static PokemonIcons? Load(RomFsLayers layers, string codeBinPath)
    {
        try
        {
            var garc = new GARC.MemGARC(File.ReadAllBytes(layers.Resolve(Garc)));
            var entries = ReadTable(File.ReadAllBytes(codeBinPath), garc.FileCount);
            return entries is null ? null : new PokemonIcons(garc, entries);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or IOException or InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>Icon of a Pokémon, or null if there is none.</summary>
    public IconImage? For(int species, int form = 0, bool female = false, bool shiny = false) => Get(IndexFor(species, form, female, shiny));

    public int IndexFor(int species, int form = 0, bool female = false, bool shiny = false)
    {
        if ((uint)species >= entries.Length)
            return -1;
        var e = entries[species];
        int[] forms = shiny && e.ShinyForms.Length > 0 ? e.ShinyForms : e.Forms;
        if (form > 0 && form < forms.Length)
            return forms[form];
        if (shiny && e.ShinyForms.Length > 0)
            return e.ShinyForms[0];
        return female ? e.Female : e.Default;
    }

    /// <summary>
    /// Finds the species icon table: an array of 16-byte entries whose first two fields are icon indexes that start
    /// 0, 1, 2 (egg placeholder, Bulbasaur, Ivysaur) and stay inside the archive, with valid form pointers.
    /// </summary>
    private static IconEntry[]? ReadTable(byte[] code, int iconCount)
    {
        for (int o = 0; o + (EntrySize * SpeciesCount) <= code.Length; o += 4)
        {
            if (U16(code, o + EntrySize) != 1 || U16(code, o + (2 * EntrySize)) != 2 || U16(code, o + 2) != 0 || U16(code, o) != 0)
                continue;
            var table = TryRead(code, o, iconCount);
            if (table is not null)
                return table;
        }
        return null;
    }

    private static IconEntry[]? TryRead(byte[] code, int offset, int iconCount)
    {
        var table = new IconEntry[SpeciesCount];
        for (int s = 0; s < SpeciesCount; s++)
        {
            int o = offset + (EntrySize * s);
            int def = U16(code, o), female = U16(code, o + 2);
            if (def >= iconCount || female >= iconCount || (s > 0 && def == 0))
                return null;
            var forms = ReadList(code, BitConverter.ToUInt32(code, o + 4), U16(code, o + 12), iconCount);
            var shiny = ReadList(code, BitConverter.ToUInt32(code, o + 8), U16(code, o + 14), iconCount);
            if (forms is null || shiny is null)
                return null;
            table[s] = new IconEntry(def, female, forms, shiny);
        }
        return table;
    }

    private static int[]? ReadList(byte[] code, uint pointer, int count, int iconCount)
    {
        if (pointer == 0 || count == 0)
            return [];
        long start = (long)pointer - CodeBase;
        if (count > 64 || start < 0 || start + (2 * count) > code.Length)
            return null;
        var list = new int[count];
        for (int i = 0; i < count; i++)
        {
            list[i] = U16(code, (int)start + (2 * i));
            if (list[i] >= iconCount)
                return null;
        }
        return list;
    }

    private static int U16(byte[] data, int offset) => BitConverter.ToUInt16(data, offset);

    /// <summary>Icon by archive index; null if missing or undecodable.</summary>
    public IconImage? Get(int index)
    {
        if (index < 0 || index >= garc.FileCount)
            return null;
        lock (cache)
        {
            if (cache.TryGetValue(index, out var cached))
                return cached;
        }

        IconImage? image;
        try
        {
            byte[] file = garc.GetFile(index);
            if (file.Length > 4 && file[0] == 0x11)
            {
                using var input = new MemoryStream(file);
                using var output = new MemoryStream();
                LZSS.Decompress(input, file.Length, output);
                file = output.ToArray();
            }
            image = DecodeBclim(file);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or IndexOutOfRangeException or EndOfStreamException)
        {
            image = null;
        }

        lock (cache)
            cache[index] = image;
        return image;
    }

    /// <summary>Decodes a BCLIM (palette, RGBA8, RGBA4, RGB5A1, RGB565, LA4, L8, A8).</summary>
    public static IconImage? DecodeBclim(byte[] data)
    {
        const int footerSize = 0x28;
        if (data.Length < footerSize)
            return null;
        int footer = data.Length - footerSize;
        if (BitConverter.ToUInt32(data, footer) != 0x4D494C43) // "CLIM"
            return null;
        int width = BitConverter.ToUInt16(data, footer + 0x1C);
        int height = BitConverter.ToUInt16(data, footer + 0x1E);
        var format = (ClimFormat)data[footer + 0x20];
        var orientation = (ClimOrientation)data[footer + 0x21];
        // Textures need not be square (a 55×77 image is stored as 64×128).
        var pixels = DecodePixels(data.AsSpan(0, footer), format, NextPow2(width) * NextPow2(height));
        if (pixels is null)
            return null;

        // Tiles of 8×8 in Morton order.
        int stride = NextPow2(orientation == ClimOrientation.None ? RoundUp8(width) : RoundUp8(height));
        int tilesPerRow = Math.Max(1, RoundUp8(stride) / 8);
        int texHeight = NextPow2(RoundUp8(height));
        var rgba = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i++)
        {
            Morton((uint)i & 0x3F, out uint x, out uint y);
            uint tile = (uint)i >> 6;
            x |= (uint)(tile % tilesPerRow) << 3;
            y |= (uint)(tile / tilesPerRow) << 3;
            if (orientation.HasFlag(ClimOrientation.Rotate90))
                (x, y) = (y, (uint)texHeight - 1 - x);
            if (orientation.HasFlag(ClimOrientation.Transpose))
                (x, y) = (y, x);
            if (x >= width || y >= height)
                continue;
            uint c = pixels[i];
            int o = (int)(4 * (x + (y * width)));
            rgba[o] = (byte)(c >> 16);    // R
            rgba[o + 1] = (byte)(c >> 8); // G
            rgba[o + 2] = (byte)c;        // B
            rgba[o + 3] = (byte)(c >> 24); // A
        }
        return new IconImage(width, height, rgba);
    }

    /// <returns>ARGB values, or null for unsupported formats.</returns>
    private static uint[]? DecodePixels(ReadOnlySpan<byte> data, ClimFormat format, int count)
    {
        var pixels = new uint[count];
        if (data.Length >= 4 && BitConverter.ToUInt16(data) == 2)
        {
            // Gen 6 palette.
            int colors = BitConverter.ToUInt16(data[2..]);
            var palette = new uint[colors];
            for (int i = 0; i < colors; i++)
                palette[i] = Rgb5A1(BitConverter.ToUInt16(data[(4 + (2 * i))..]));
            int offset = 4 + (2 * colors);
            bool half = colors <= 0x10; // 16 colors still fit in a nibble
            for (int i = 0; i < count && offset < data.Length; offset++)
            {
                byte b = data[offset];
                if (half)
                {
                    pixels[i++] = Pick(palette, b >> 4); // high nibble first
                    if (i < count)
                        pixels[i++] = Pick(palette, b & 0xF);
                }
                else
                {
                    pixels[i++] = Pick(palette, b);
                }
            }
            return pixels;
        }

        int bpp = format switch
        {
            ClimFormat.RGBA8 => 4,
            ClimFormat.RGBA4 or ClimFormat.RGB5A1 or ClimFormat.RGB565 or ClimFormat.LA8 => 2,
            ClimFormat.LA4 or ClimFormat.L8 or ClimFormat.A8 => 1,
            _ => 0,
        };
        if (bpp == 0 || data.Length < count * bpp)
            return null;
        for (int i = 0; i < count; i++)
        {
            var px = data.Slice(i * bpp, bpp);
            pixels[i] = format switch
            {
                ClimFormat.RGBA8 => Argb(px[0], px[3], px[2], px[1]),
                ClimFormat.RGBA4 => Rgba4(BitConverter.ToUInt16(px)),
                ClimFormat.RGB5A1 => Rgb5A1(BitConverter.ToUInt16(px)),
                ClimFormat.RGB565 => Rgb565(BitConverter.ToUInt16(px)),
                ClimFormat.LA8 => Argb(px[0], px[1], px[1], px[1]),
                ClimFormat.LA4 => Argb((byte)(0x11 * (px[0] & 0xF)), (byte)(0x11 * (px[0] >> 4)), (byte)(0x11 * (px[0] >> 4)), (byte)(0x11 * (px[0] >> 4))),
                ClimFormat.L8 => Argb(0xFF, px[0], px[0], px[0]),
                _ => Argb(px[0], 0, 0, 0), // A8
            };
        }
        return pixels;
    }

    private static uint Pick(uint[] palette, int index) => index < palette.Length ? palette[index] : 0;

    private static uint Argb(byte a, byte r, byte g, byte b) => ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | b;

    private static byte Five(int v) => (byte)((v << 3) | (v >> 2));

    private static uint Rgb565(ushort v) => Argb(0xFF, Five((v >> 11) & 0x1F), (byte)((((v >> 5) & 0x3F) << 2) | (((v >> 5) & 0x3F) >> 4)), Five(v & 0x1F));

    private static uint Rgb5A1(ushort v) => Argb((v & 1) == 1 ? (byte)0xFF : (byte)0, Five((v >> 11) & 0x1F), Five((v >> 6) & 0x1F), Five((v >> 1) & 0x1F));

    private static uint Rgba4(ushort v) => Argb((byte)(0x11 * (v & 0xF)), (byte)(0x11 * ((v >> 12) & 0xF)), (byte)(0x11 * ((v >> 8) & 0xF)), (byte)(0x11 * ((v >> 4) & 0xF)));

    private static int NextPow2(int v)
    {
        int p = 1;
        while (p < v)
            p <<= 1;
        return p;
    }

    private static int RoundUp8(int v) => (v + 7) / 8 * 8;

    private static void Morton(uint d, out uint x, out uint y)
    {
        x = d;
        y = x >> 1;
        x &= 0x55555555; y &= 0x55555555;
        x |= x >> 1; y |= y >> 1;
        x &= 0x33333333; y &= 0x33333333;
        x |= x >> 2; y |= y >> 2;
        x &= 0x0f0f0f0f; y &= 0x0f0f0f0f;
        x |= x >> 4; y |= y >> 4;
        x &= 0x00ff00ff; y &= 0x00ff00ff;
    }
}

/// <summary>BCLIM pixel formats (the values stored in the file).</summary>
internal enum ClimFormat : byte
{
    L8 = 0x00,
    A8 = 0x01,
    LA4 = 0x02,
    LA8 = 0x03,
    HILO8 = 0x04,
    RGB565 = 0x05,
    RGBX8 = 0x06,
    RGB5A1 = 0x07,
    RGBA4 = 0x08,
    RGBA8 = 0x09,
    ETC1 = 0x0A,
    ETC1A4 = 0x0B,
    L4 = 0x0C,
    A4 = 0x0D,
}

[Flags]
internal enum ClimOrientation : byte
{
    None = 0,
    Rotate90 = 4,
    Transpose = 8,
}
