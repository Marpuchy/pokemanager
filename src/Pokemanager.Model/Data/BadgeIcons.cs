using System.Text;
using pk3DS.Core.CTR;

namespace Pokemanager.Model.Data;

/// <summary>
/// The eight Kalos gym badge images, read from the user's own romfs: the trainer card layout (<c>a/1/1/3</c>, file 0)
/// is a darc archive with <c>badge_01.bclim</c> … <c>badge_08.bclim</c> (RGBA8, 50–90 px) plus their grey
/// <c>badge_0N_base</c> silhouettes. Nothing is bundled with the application.
/// </summary>
public static class BadgeIcons
{
    public const string Garc = "a/1/1/3";

    /// <summary>The 8 images in badge order; an entry is null when it cannot be read. Null when the archive is missing.</summary>
    public static IconImage?[]? Load(RomFsLayers layers)
    {
        try
        {
            var garc = new GARC.MemGARC(File.ReadAllBytes(layers.Resolve(Garc)));
            if (garc.FileCount == 0)
                return null;
            byte[] file = garc.GetFile(0);
            if (file.Length > 4 && file[0] == 0x11)
            {
                using var input = new MemoryStream(file);
                using var output = new MemoryStream();
                LZSS.Decompress(input, file.Length, output);
                file = output.ToArray();
            }
            var files = ReadDarc(file);
            if (files is null)
                return null;
            var badges = new IconImage?[8];
            for (int i = 0; i < badges.Length; i++)
                badges[i] = files.TryGetValue($"badge_{i + 1:00}.bclim", out var bclim) ? PokemonIcons.DecodeBclim(bclim) : null;
            return badges.Any(b => b is not null) ? badges : null;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or IndexOutOfRangeException or EndOfStreamException)
        {
            return null;
        }
    }

    /// <summary>
    /// Files of the first darc archive in <paramref name="data"/>, by name. darc: header (<c>"darc"</c>, BOM, header
    /// size, version, file size, table offset, table size, data offset), then 12-byte entries (name offset with the
    /// directory flag in the top bit, data offset, size) followed by the UTF-16 names.
    /// </summary>
    public static Dictionary<string, byte[]>? ReadDarc(byte[] data)
    {
        int start = data.AsSpan().IndexOf("darc"u8);
        if (start < 0 || start + 0x1C > data.Length)
            return null;
        int tableOffset = start + BitConverter.ToInt32(data, start + 0x10);
        int tableSize = BitConverter.ToInt32(data, start + 0x14);
        if (tableOffset + 12 > data.Length)
            return null;
        // The root entry's size is the number of entries.
        int count = BitConverter.ToInt32(data, tableOffset + 8);
        int namesStart = tableOffset + (12 * count);
        if (count <= 0 || namesStart > data.Length || namesStart > tableOffset + tableSize)
            return null;

        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < count; i++)
        {
            int e = tableOffset + (12 * i);
            uint name = BitConverter.ToUInt32(data, e);
            if ((name & 0x01000000) != 0)
                continue; // directory
            int offset = start + BitConverter.ToInt32(data, e + 4);
            int size = BitConverter.ToInt32(data, e + 8);
            if (size <= 0 || offset < 0 || offset + size > data.Length)
                continue;
            files[ReadName(data, namesStart + (int)(name & 0xFFFFFF))] = data[offset..(offset + size)];
        }
        return files;
    }

    private static string ReadName(byte[] data, int offset)
    {
        var name = new StringBuilder();
        for (int o = offset; o + 1 < data.Length; o += 2)
        {
            char c = (char)BitConverter.ToUInt16(data, o);
            if (c == '\0')
                break;
            name.Append(c);
        }
        return name.ToString();
    }

    /// <summary>A grey, faded copy for badges not earned yet.</summary>
    public static IconImage Faded(IconImage image)
    {
        var rgba = (byte[])image.Rgba.Clone();
        for (int i = 0; i < rgba.Length; i += 4)
        {
            byte grey = (byte)(((rgba[i] * 30) + (rgba[i + 1] * 59) + (rgba[i + 2] * 11)) / 100);
            grey = (byte)(150 + (grey / 4));
            rgba[i] = rgba[i + 1] = rgba[i + 2] = grey;
            rgba[i + 3] = (byte)(rgba[i + 3] * 55 / 100);
        }
        return image with { Rgba = rgba };
    }
}
