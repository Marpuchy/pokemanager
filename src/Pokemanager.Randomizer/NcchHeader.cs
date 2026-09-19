using System.Buffers.Binary;

namespace Pokemanager.Randomizer;

/// <summary>
/// Puts right the region fields of an NCCH (.cxi) written by UPR ZX's <c>NCCH.saveAsNCCH</c>.
/// </summary>
/// <remarks>
/// **Found on the user's ROMs (2026-09-19)**: UPR ZX writes the new offsets and sizes of the ExeFS and the RomFS with
/// <c>RandomAccessFile.write(int)</c>, which writes **one byte** — the lowest — over the base ROM's header. While a
/// build keeps the RomFS the same size nothing shows; when it shrinks (the Ultra Moon wild encounter archive
/// recompressed 255 KB smaller) the header still promises the old size, the emulator reads past the end of the file and
/// the game stops with a fatal error ("Unable to read RomFS"). It also never updates the content size. Every value is
/// recomputed from the file itself: the regions follow each other in the order UPR writes them (header and extended
/// header, logo, plain region, ExeFS, RomFS aligned to 4 KB, which runs to the end of the file).
/// </remarks>
public static class NcchHeader
{
    private const int MediaUnit = 0x200;
    private const int HeaderAndExheader = 0xA00;

    /// <summary>Corrects the header in place.</summary>
    /// <returns>Whether anything had to change.</returns>
    /// <exception cref="InvalidDataException">The file is not an NCCH laid out as UPR ZX writes it.</exception>
    public static bool Repair(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        byte[]? corrected = Corrected(file);
        if (corrected is null)
            return false;
        file.Position = 0;
        file.Write(corrected); // the NCCH header only; the extended header is untouched
        return true;
    }

    /// <summary>The first 0x200 bytes as they should be, or null when they already are. Reads only.</summary>
    /// <exception cref="InvalidDataException">The file is not an NCCH laid out as UPR ZX writes it.</exception>
    public static byte[]? Corrected(Stream file)
    {
        byte[] header = new byte[HeaderAndExheader];
        file.Position = 0;
        file.ReadExactly(header);
        if (!header.AsSpan(0x100, 4).SequenceEqual("NCCH"u8))
            throw new InvalidDataException("Not an NCCH file.");

        long logoLength = Units(header, 0x19C);
        long plainLength = Units(header, 0x194);
        long plainOffset = HeaderAndExheader + logoLength;
        long exefsOffset = plainOffset + plainLength;
        long exefsLength = ExefsLength(file, exefsOffset);
        long romfsOffset = Align(exefsOffset + exefsLength, 0x1000);
        long fileLength = file.Length;

        // The RomFS has to be where the layout puts it: its IVFC header is the proof.
        byte[] magic = new byte[8];
        file.Position = romfsOffset;
        file.ReadExactly(magic);
        if (!magic.AsSpan(0, 4).SequenceEqual("IVFC"u8) || BinaryPrimitives.ReadUInt32LittleEndian(magic.AsSpan(4)) != 0x10000)
            throw new InvalidDataException($"No RomFS where the layout puts it (0x{romfsOffset:X}).");
        if (fileLength <= romfsOffset || (fileLength - romfsOffset) % MediaUnit != 0)
            throw new InvalidDataException("The RomFS does not end on a media unit.");

        byte[] original = (byte[])header.Clone();
        if (logoLength > 0)
            SetUnits(header, 0x198, HeaderAndExheader);
        if (plainLength > 0)
            SetUnits(header, 0x190, plainOffset);
        SetUnits(header, 0x1A0, exefsOffset);
        SetUnits(header, 0x1A4, exefsLength);
        SetUnits(header, 0x1B0, romfsOffset);
        SetUnits(header, 0x1B4, fileLength - romfsOffset);
        SetUnits(header, 0x104, fileLength);
        return header.AsSpan().SequenceEqual(original) ? null : header[..0x200];
    }

    /// <summary>The ExeFS header lists up to ten files (name, offset, size) after 0x200 bytes of header: its end, aligned.</summary>
    private static long ExefsLength(Stream file, long exefsOffset)
    {
        byte[] table = new byte[0xA0];
        file.Position = exefsOffset;
        file.ReadExactly(table);
        long end = 0;
        for (int i = 0; i < 10; i++)
        {
            if (table[i * 16] == 0)
                continue;
            long offset = BinaryPrimitives.ReadUInt32LittleEndian(table.AsSpan((i * 16) + 8));
            long size = BinaryPrimitives.ReadUInt32LittleEndian(table.AsSpan((i * 16) + 12));
            end = Math.Max(end, offset + size);
        }
        if (end == 0)
            throw new InvalidDataException("Empty ExeFS.");
        return Align(MediaUnit + end, MediaUnit);
    }

    private static long Units(byte[] header, int offset) => (long)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(offset)) * MediaUnit;

    private static void SetUnits(byte[] header, int offset, long bytes) =>
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(offset), checked((uint)(bytes / MediaUnit)));

    private static long Align(long value, long alignment) => (value + alignment - 1) / alignment * alignment;
}
