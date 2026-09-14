using System.Text;
using pk3DS.Core.CTR;
using Pokemanager.Model.Dump;

namespace Pokemanager.Model.Data;

/// <summary>
/// Images of the game's badges or trials, read from the user's own romfs. Gen 6: the trainer card layout is a darc with
/// <c>badge_01.bclim</c> … <c>badge_08.bclim</c> (RGBA8, 50–90 px; X/Y <c>a/1/1/3</c>, ORAS <c>a/1/0/9</c>). Gen 7: the
/// trial screen layout is an ALYT with a SARC holding the guardian seals <c>Shiren_Stamp_00</c> … <c>03</c> (ETC1A4
/// BFLIM, 256 px). Nothing is bundled with the application.
/// </summary>
public static class MilestoneIcons
{
    /// <summary>One image per milestone of the game (null when it has none). Null when no candidate archive has them.</summary>
    public static IconImage?[]? Load(RomFsLayers layers, GameTitle title)
    {
        int count = title.Milestones().Count;
        foreach (var candidate in title.Layout().Milestones)
        {
            if (ReadArchive(layers, candidate) is not { } files)
                continue;
            var images = new IconImage?[count];
            for (int i = 0; i < count && i < candidate.Names.Length; i++)
            {
                if (candidate.Names[i].Length > 0 && Find(files, candidate.Names[i]) is { } image)
                    images[i] = PokemonIcons.DecodeBclim(image);
            }
            if (images.Any(b => b is not null))
                return images;
        }
        return null;
    }

    /// <summary>The files of a darc or SARC stored in a GARC entry; null when missing or neither.</summary>
    internal static Dictionary<string, byte[]>? ReadArchive(RomFsLayers layers, MilestoneImages candidate)
    {
        try
        {
            return ReadArchive(File.ReadAllBytes(layers.Resolve(candidate.Archive)), candidate.File);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The files of the darc or SARC in entry <paramref name="file"/> of a GARC; null when it is neither.</summary>
    public static Dictionary<string, byte[]>? ReadArchive(byte[] garcData, int file)
    {
        try
        {
            var garc = new GARC.MemGARC(garcData);
            if (file >= garc.FileCount)
                return null;
            byte[] data = garc.GetFile(file);
            if (data.Length > 4 && data[0] == 0x11)
            {
                using var input = new MemoryStream(data);
                using var output = new MemoryStream();
                LZSS.Decompress(input, data.Length, output);
                data = output.ToArray();
            }
            return ReadDarc(data) ?? ReadSarc(data);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or IndexOutOfRangeException or EndOfStreamException or FormatException)
        {
            return null;
        }
    }

    /// <summary>A file by name, ignoring folders inside the archive and case.</summary>
    public static byte[]? Find(Dictionary<string, byte[]> files, string name) =>
        files.FirstOrDefault(f => f.Key.Replace('\\', '/').Split('/')[^1].Equals(name, StringComparison.OrdinalIgnoreCase)).Value;

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
            files[ReadUtf16Name(data, namesStart + (int)(name & 0xFFFFFF))] = data[offset..(offset + size)];
        }
        return files;
    }

    /// <summary>
    /// Files of the first SARC in <paramref name="data"/> (a Gen 7 ALYT layout keeps one after its own header). SARC:
    /// header (<c>"SARC"</c>, header size, BOM, file size, data offset at 0x0C), SFAT (<c>"SFAT"</c>, header size, count, hash
    /// key, then per file: name hash, name offset (low 24 bits × 4, flag in the top byte), data start and end relative to
    /// the data offset), SFNT (<c>"SFNT"</c>, header size, then the zero-terminated ASCII names).
    /// </summary>
    public static Dictionary<string, byte[]>? ReadSarc(byte[] data)
    {
        int start = data.AsSpan().IndexOf("SARC"u8);
        if (start < 0 || start + 0x14 > data.Length)
            return null;
        int headerSize = BitConverter.ToUInt16(data, start + 4);
        int dataOffset = start + BitConverter.ToInt32(data, start + 0x0C);
        int sfat = start + headerSize;
        if (sfat + 0x0C > data.Length || Encoding.ASCII.GetString(data, sfat, 4) != "SFAT")
            return null;
        int count = BitConverter.ToUInt16(data, sfat + 6);
        int entries = sfat + BitConverter.ToUInt16(data, sfat + 4);
        int sfnt = entries + (count * 16);
        if (sfnt + 8 > data.Length || Encoding.ASCII.GetString(data, sfnt, 4) != "SFNT")
            return null;
        int names = sfnt + BitConverter.ToUInt16(data, sfnt + 4);

        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < count; i++)
        {
            int e = entries + (i * 16);
            uint nameAttr = BitConverter.ToUInt32(data, e + 4);
            int begin = dataOffset + BitConverter.ToInt32(data, e + 8), end = dataOffset + BitConverter.ToInt32(data, e + 12);
            if ((nameAttr & 0xFF000000) == 0 || end <= begin || end > data.Length)
                continue;
            int nameAt = names + ((int)(nameAttr & 0xFFFFFF) * 4);
            int nameEnd = Array.IndexOf(data, (byte)0, nameAt);
            if (nameAt >= data.Length || nameEnd < 0)
                continue;
            files[Encoding.ASCII.GetString(data, nameAt, nameEnd - nameAt)] = data[begin..end];
        }
        return files;
    }

    private static string ReadUtf16Name(byte[] data, int offset)
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

    /// <summary>A grey, faded copy for milestones not reached yet.</summary>
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
