using System.Text;
using pk3DS.Core.CTR;
using Pokemanager.Model.Resources;

namespace Pokemanager.Model.Dump;

/// <summary>A ROM that cannot be used: not a 3DS ROM, encrypted, or damaged.</summary>
public sealed class RomReadException(string message) : Exception(message);

/// <summary>
/// Reads single files straight out of a decrypted 3DS ROM — a card image (<c>.3ds</c>/<c>.cci</c>, NCSD) or a
/// program (<c>.cxi</c>, NCCH) — without extracting the whole thing. Read-only.
/// </summary>
/// <remarks>
/// Layout used (3dbrew): the NCSD header at 0x100 gives partition 0 in media units (0x200 bytes); the NCCH header
/// (magic at 0x100) has the program ID at 0x118, flags at 0x188 (flag 7 bit 2 = no encryption), the extended header
/// right after it (0x200) and the ExeFS/RomFS offsets in media units at 0x1A0/0x1B0. ExeFS: 10 entries of name (8),
/// offset and size, data from 0x200. RomFS: an IVFC tree whose level 3 holds directory and file metadata tables.
/// </remarks>
public sealed class RomReader : IDisposable
{
    private const int MediaUnit = 0x200;
    private readonly FileStream stream;
    private readonly long ncch;
    private readonly long exefs;
    private readonly long level3;
    private readonly Level3Header romfs;

    public string Path { get; }

    /// <summary>Program ID of the game (the title ID the emulator uses for its save folder).</summary>
    public ulong TitleId { get; }

    /// <summary>Whether <c>.code</c> is BLZ-compressed (extended header, byte 0x0D bit 0).</summary>
    public bool CodeCompressed { get; }

    private RomReader(string path, FileStream stream, long ncch, ulong titleId, bool codeCompressed, long exefs, long level3, Level3Header romfs)
    {
        Path = path;
        this.stream = stream;
        this.ncch = ncch;
        TitleId = titleId;
        CodeCompressed = codeCompressed;
        this.exefs = exefs;
        this.level3 = level3;
        this.romfs = romfs;
    }

    /// <exception cref="RomReadException">Not a 3DS ROM, encrypted or damaged.</exception>
    public static RomReader Open(string path)
    {
        var stream = File.OpenRead(path);
        try
        {
            long ncch = 0;
            string magic = Ascii(stream, 0x100, 4);
            if (magic == "NCSD")
            {
                ncch = (long)U32(stream, 0x120) * MediaUnit;
                magic = Ascii(stream, ncch + 0x100, 4);
            }
            if (magic != "NCCH")
                throw new RomReadException(string.Format(Strings.Rom_NotA3dsRom, System.IO.Path.GetFileName(path)));

            ulong titleId = U64(stream, ncch + 0x118);
            byte cryptoFlags = Bytes(stream, ncch + 0x18F, 1)[0];
            if ((cryptoFlags & 0x04) == 0)
                throw new RomReadException(string.Format(Strings.Rom_Encrypted, System.IO.Path.GetFileName(path)));

            bool compressed = (Bytes(stream, ncch + 0x200 + 0x0D, 1)[0] & 1) != 0;
            long exefs = ncch + ((long)U32(stream, ncch + 0x1A0) * MediaUnit);
            long romfsStart = ncch + ((long)U32(stream, ncch + 0x1B0) * MediaUnit);

            if (Ascii(stream, romfsStart, 4) != "IVFC")
                throw new RomReadException(string.Format(Strings.Rom_Damaged, System.IO.Path.GetFileName(path)));
            // Level 3 starts after the IVFC header (0x60) and the master hash, aligned to its block size.
            uint masterHashSize = U32(stream, romfsStart + 0x08);
            int blockSize = 1 << (int)U32(stream, romfsStart + 0x4C);
            long level3 = romfsStart + AlignUp(0x60 + masterHashSize, blockSize);
            var header = new Level3Header(
                U32(stream, level3 + 0x0C), U32(stream, level3 + 0x10),
                U32(stream, level3 + 0x1C), U32(stream, level3 + 0x20),
                U32(stream, level3 + 0x24));
            return new RomReader(path, stream, ncch, titleId, compressed, exefs, level3, header);
        }
        catch (Exception ex) when (ex is EndOfStreamException or ArgumentOutOfRangeException)
        {
            stream.Dispose();
            throw new RomReadException(string.Format(Strings.Rom_Damaged, System.IO.Path.GetFileName(path)));
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>Only reads the header: the title ID, or null when it is not a usable ROM.</summary>
    public static ulong? TryReadTitleId(string path)
    {
        try
        {
            using var rom = Open(path);
            return rom.TitleId;
        }
        catch (Exception ex) when (ex is RomReadException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The extended header (0x400 bytes after the NCCH header).</summary>
    public byte[] ReadExtendedHeader() => Bytes(stream, ncch + 0x200, 0x400);

    /// <summary>An ExeFS file by name (<c>.code</c>, <c>icon</c>, <c>banner</c>, <c>logo</c>); null when missing.</summary>
    public byte[]? ReadExeFs(string name)
    {
        byte[] header = Bytes(stream, exefs, 0xA0);
        for (int i = 0; i < 10; i++)
        {
            string entry = Encoding.ASCII.GetString(header, i * 16, 8).TrimEnd('\0');
            if (entry != name)
                continue;
            uint offset = BitConverter.ToUInt32(header, (i * 16) + 8), size = BitConverter.ToUInt32(header, (i * 16) + 12);
            return Bytes(stream, exefs + 0x200 + offset, (int)size);
        }
        return null;
    }

    /// <summary><c>code.bin</c> decompressed, as the app and the emulator's mods expect it.</summary>
    public byte[] ReadCode()
    {
        byte[] code = ReadExeFs(".code") ?? throw new RomReadException(string.Format(Strings.Rom_Damaged, System.IO.Path.GetFileName(Path)));
        if (!CodeCompressed)
            return code;
        string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pokemanager-code-" + Guid.NewGuid().ToString("N") + ".bin");
        try
        {
            File.WriteAllBytes(tmp, code);
            _ = new BLZCoder(["-d", tmp]); // decompresses in place
            return File.ReadAllBytes(tmp);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    /// <summary>Size of a RomFS file (<c>a/2/1/8</c>), or null when it does not exist.</summary>
    public long? RomFsFileSize(string relative) => FindFile(relative) is { } f ? f.Size : null;

    /// <summary>A RomFS file (<c>a/2/1/8</c>); null when it does not exist.</summary>
    public byte[]? ReadRomFs(string relative) =>
        FindFile(relative) is { } f ? Bytes(stream, level3 + romfs.FileData + f.Offset, checked((int)f.Size)) : null;

    /// <summary>Copies a RomFS file to disk without loading it whole. False when it does not exist.</summary>
    public bool CopyRomFs(string relative, string target)
    {
        if (FindFile(relative) is not { } f)
            return false;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
        string partial = target + ".partial";
        using (var output = File.Create(partial))
        {
            stream.Seek(level3 + romfs.FileData + f.Offset, SeekOrigin.Begin);
            var buffer = new byte[1 << 20];
            long left = f.Size;
            while (left > 0)
            {
                int read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, left));
                if (read <= 0)
                    throw new EndOfStreamException();
                output.Write(buffer, 0, read);
                left -= read;
            }
        }
        File.Move(partial, target, overwrite: true);
        return true;
    }

    /// <summary>Every RomFS file under a directory (<c>a</c>, or empty for all) with its size.</summary>
    public IEnumerable<(string Path, long Size)> EnumerateRomFs(string directory = "")
    {
        uint dir = 0;
        foreach (string part in directory.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            uint child = U32(stream, DirMeta(dir) + 0x08);
            while (child != uint.MaxValue && Name(DirMeta(child) + 0x18) != part)
                child = U32(stream, DirMeta(child) + 0x04);
            if (child == uint.MaxValue)
                yield break;
            dir = child;
        }
        foreach (var entry in Walk(dir, directory.Trim('/')))
            yield return entry;
    }

    private IEnumerable<(string Path, long Size)> Walk(uint dir, string prefix)
    {
        for (uint file = U32(stream, DirMeta(dir) + 0x0C); file != uint.MaxValue; file = U32(stream, level3 + romfs.FileMeta + file + 0x04))
        {
            long meta = level3 + romfs.FileMeta + file;
            yield return (Join(prefix, Name(meta + 0x20)), (long)U64(stream, meta + 0x10));
        }
        for (uint child = U32(stream, DirMeta(dir) + 0x08); child != uint.MaxValue; child = U32(stream, DirMeta(child) + 0x04))
        {
            foreach (var entry in Walk(child, Join(prefix, Name(DirMeta(child) + 0x18))))
                yield return entry;
        }
    }

    private static string Join(string prefix, string name) => prefix.Length == 0 ? name : prefix + "/" + name;

    /// <summary>Walks the directory tree from the root (directory 0).</summary>
    private (long Offset, long Size)? FindFile(string relative)
    {
        string[] parts = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return null;
        uint dir = 0;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            uint child = U32(stream, DirMeta(dir) + 0x08);
            uint found = uint.MaxValue;
            while (child != uint.MaxValue)
            {
                if (Name(DirMeta(child) + 0x18) == parts[i])
                {
                    found = child;
                    break;
                }
                child = U32(stream, DirMeta(child) + 0x04);
            }
            if (found == uint.MaxValue)
                return null;
            dir = found;
        }

        uint file = U32(stream, DirMeta(dir) + 0x0C);
        while (file != uint.MaxValue)
        {
            long meta = level3 + romfs.FileMeta + file;
            if (Name(meta + 0x20) == parts[^1])
                return ((long)U64(stream, meta + 0x08), (long)U64(stream, meta + 0x10));
            file = U32(stream, meta + 0x04);
        }
        return null;
    }

    private long DirMeta(uint dir) => level3 + romfs.DirMeta + dir;

    /// <summary>Name length (u32) followed by UTF-16 characters.</summary>
    private string Name(long lengthOffset)
    {
        uint length = U32(stream, lengthOffset - 4);
        return length == 0 ? "" : Encoding.Unicode.GetString(Bytes(stream, lengthOffset, (int)length));
    }

    public void Dispose() => stream.Dispose();

    private sealed record Level3Header(uint DirMeta, uint DirMetaSize, uint FileMeta, uint FileMetaSize, uint FileData);

    private static long AlignUp(long value, int alignment) => (value + alignment - 1) / alignment * alignment;

    private static byte[] Bytes(FileStream stream, long offset, int count)
    {
        if (offset < 0 || offset + count > stream.Length)
            throw new EndOfStreamException();
        stream.Seek(offset, SeekOrigin.Begin);
        var buffer = new byte[count];
        stream.ReadExactly(buffer);
        return buffer;
    }

    private static uint U32(FileStream s, long offset) => BitConverter.ToUInt32(Bytes(s, offset, 4));
    private static ulong U64(FileStream s, long offset) => BitConverter.ToUInt64(Bytes(s, offset, 8));
    private static string Ascii(FileStream s, long offset, int count) => Encoding.ASCII.GetString(Bytes(s, offset, count));
}
