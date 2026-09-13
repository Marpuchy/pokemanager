using System.Text;
using pk3DS.Core.CTR;
using Pokemanager.Model.Resources;

namespace Pokemanager.Model.Dump;

/// <summary>Result of inspecting a dump. <see cref="Title"/> is null when the game could not be identified.</summary>
public sealed record DumpInspection(GameTitle? Title, IReadOnlyList<string> Problems)
{
    public bool IsValid => Title is not null && Problems.Count == 0;
}

/// <summary>
/// Checks that a <c>romfs</c>/<c>exefs</c> folder pair is a usable X/Y dump. Read-only: never modifies anything.
/// </summary>
/// <remarks>
/// pk3DS identifies the game only by counting the files in <c>a/</c>, which cannot tell X from Y and fails silently
/// on an unusual dump. This adds: the structural signature of several GARCs, the title in <c>icon.bin</c> and a check
/// that <c>code.bin</c> is decompressed. Every problem is reported at once.
/// </remarks>
public static class DumpInspector
{
    public const int FileCountXY = 271;

    /// <summary>X/Y GARCs with their entry counts, measured on a real Pokémon X dump.</summary>
    internal static readonly (string Path, string Name, int Entries)[] SignatureGarcs =
    [
        ("a/2/1/8", "personal", 800),
        ("a/2/1/2", "move", 618),
        ("a/2/1/4", "levelup", 799),
        ("a/2/1/5", "evolution", 799),
    ];

    public static DumpInspection Inspect(string romFsPath, string exeFsPath)
    {
        var problems = new List<string>();

        if (!Directory.Exists(romFsPath))
            problems.Add(string.Format(Strings.Dump_RomFsMissing, romFsPath));
        else
            CheckRomFs(romFsPath, problems);

        GameTitle? title = null;
        if (!Directory.Exists(exeFsPath))
        {
            problems.Add(string.Format(Strings.Dump_ExeFsMissing, exeFsPath));
        }
        else
        {
            title = ReadTitle(Path.Combine(exeFsPath, "icon.bin"), problems);
            CheckCode(Path.Combine(exeFsPath, "code.bin"), problems);
        }

        return new DumpInspection(title, problems);
    }

    private static void CheckRomFs(string romFsPath, List<string> problems)
    {
        string aDir = Path.Combine(romFsPath, "a");
        if (!Directory.Exists(aDir))
        {
            problems.Add(Strings.Dump_NoAFolder);
            return;
        }

        int count = Directory.EnumerateFiles(aDir, "*", SearchOption.AllDirectories).Count();
        if (count != FileCountXY)
            problems.Add(string.Format(Strings.Dump_FileCount, count, FileCountXY));

        foreach (var (relative, name, entries) in SignatureGarcs)
            CheckGarc(romFsPath, relative, name, entries, problems);
    }

    private static void CheckGarc(string romFsPath, string relative, string name, int expectedEntries, List<string> problems)
    {
        string path = Path.Combine(romFsPath, relative.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
        {
            problems.Add(string.Format(Strings.Dump_GarcMissing, relative, name));
            return;
        }

        // VER_4 GARC header: magic "CRAG", version at 0x0A; FATO ("OTAF") starts after 0x1C bytes with the entry
        // count at 0x24.
        Span<byte> header = stackalloc byte[0x28];
        using (var fs = File.OpenRead(path))
        {
            if (fs.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) < header.Length)
            {
                problems.Add(string.Format(Strings.Dump_GarcTooShort, relative, name));
                return;
            }
        }

        if (!header[..4].SequenceEqual("CRAG"u8) || !header.Slice(0x1C, 4).SequenceEqual("OTAF"u8))
        {
            problems.Add(string.Format(Strings.Dump_NotGarc, relative, name));
            return;
        }

        ushort version = BitConverter.ToUInt16(header[0x0A..]);
        if (version != GARC.VER_4)
            problems.Add(string.Format(Strings.Dump_GarcVersion, relative, name, version, GARC.VER_4));

        ushort entries = BitConverter.ToUInt16(header[0x24..]);
        if (entries != expectedEntries)
            problems.Add(string.Format(Strings.Dump_GarcEntries, relative, name, entries, expectedEntries));
    }

    /// <summary>
    /// Reads the SMDH titles (<c>icon.bin</c>): 16 entries of 0x200 bytes from 0x08, each starting with the short
    /// description in UTF-16 (0x80 bytes). Every non-empty one must end in X or in Y.
    /// </summary>
    private static GameTitle? ReadTitle(string iconPath, List<string> problems)
    {
        if (!File.Exists(iconPath))
        {
            problems.Add(Strings.Dump_IconMissing);
            return null;
        }

        byte[] smdh = File.ReadAllBytes(iconPath);
        if (smdh.Length < 0x2008 || !smdh.AsSpan(0, 4).SequenceEqual("SMDH"u8))
        {
            problems.Add(Strings.Dump_IconInvalid);
            return null;
        }

        var found = new HashSet<GameTitle>();
        var names = new HashSet<string>();
        bool unrecognized = false;
        for (int i = 0; i < 16; i++)
        {
            string name = Encoding.Unicode.GetString(smdh, 0x08 + (i * 0x200), 0x80).TrimEnd('\0').Trim();
            if (name.Length == 0)
                continue;

            names.Add(name);
            switch (name[^1])
            {
                case 'X' or 'Ｘ': found.Add(GameTitle.X); break;
                case 'Y' or 'Ｙ': found.Add(GameTitle.Y); break;
                default: unrecognized = true; break;
            }
        }

        if (found.Count == 1 && !unrecognized)
            return found.First();

        problems.Add(string.Format(Strings.Dump_TitleUnknown, string.Join(" | ", names)));
        return null;
    }

    private static void CheckCode(string codePath, List<string> problems)
    {
        if (!File.Exists(codePath))
        {
            problems.Add(Strings.Dump_CodeMissing);
            return;
        }

        if (LooksBlzCompressed(codePath))
            problems.Add(Strings.Dump_CodeCompressed);
    }

    /// <summary>
    /// A BLZ file ends with an 8-byte footer: encoded length (24 bits) + header length (1 byte, 8–11) and the size
    /// increase when decompressing (int32 &gt; 0). Same checks as <see cref="BLZCoder"/>. A decompressed code.bin
    /// does not pass them.
    /// </summary>
    internal static bool LooksBlzCompressed(string path)
    {
        using var fs = File.OpenRead(path);
        if (fs.Length < 8)
            return false;

        Span<byte> footer = stackalloc byte[8];
        fs.Seek(-8, SeekOrigin.End);
        fs.ReadExactly(footer);

        uint encoded = BitConverter.ToUInt32(footer);
        int encodedLength = (int)(encoded & 0x00FFFFFF);
        int headerLength = (int)(encoded >> 24);
        int increment = BitConverter.ToInt32(footer[4..]);

        return increment > 0
               && headerLength is >= 8 and <= 0x0B
               && encodedLength > headerLength
               && encodedLength <= fs.Length;
    }
}
