using System.Text;
using pk3DS.Core.CTR;

namespace Pokemanager.Model.Dump;

/// <summary>Resultado de inspeccionar un volcado. <see cref="Title"/> es null si no se pudo identificar.</summary>
public sealed record DumpInspection(GameTitle? Title, IReadOnlyList<string> Problems)
{
    public bool IsValid => Title is not null && Problems.Count == 0;
}

/// <summary>
/// Comprueba que un par de carpetas <c>romfs</c>/<c>exefs</c> es un volcado utilizable de X/Y.
/// Solo lee: nunca modifica nada.
/// </summary>
/// <remarks>
/// pk3DS identifica el juego únicamente contando los archivos de <c>a/</c>, que no distingue X de Y y
/// falla en silencio con un volcado raro. Aquí se añade: firma estructural de varios GARC, el título
/// de <c>icon.bin</c> y que <c>code.bin</c> esté descomprimido. Se informa de todos los problemas a la vez.
/// </remarks>
public static class DumpInspector
{
    public const int FileCountXY = 271;

    /// <summary>GARC de X/Y con su número de entradas, medido sobre un volcado real de Pokémon X.</summary>
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
            problems.Add($"No existe la carpeta romfs: {romFsPath}");
        else
            CheckRomFs(romFsPath, problems);

        GameTitle? title = null;
        if (!Directory.Exists(exeFsPath))
        {
            problems.Add($"No existe la carpeta exefs: {exeFsPath}");
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
            problems.Add("La carpeta romfs no contiene 'a'. ¿Es la raíz del romfs?");
            return;
        }

        int count = Directory.EnumerateFiles(aDir, "*", SearchOption.AllDirectories).Count();
        if (count != FileCountXY)
            problems.Add($"romfs/a tiene {count} archivos; X/Y tiene {FileCountXY} (ORAS 299, Sol/Luna 311, USUM 333).");

        foreach (var (relative, name, entries) in SignatureGarcs)
            CheckGarc(romFsPath, relative, name, entries, problems);
    }

    private static void CheckGarc(string romFsPath, string relative, string name, int expectedEntries, List<string> problems)
    {
        string path = Path.Combine(romFsPath, relative.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
        {
            problems.Add($"Falta {relative} ({name}).");
            return;
        }

        // Cabecera GARC VER_4: magic "CRAG", versión en 0x0A; tras 0x1C bytes empieza FATO ("OTAF")
        // con el número de entradas en 0x24.
        Span<byte> header = stackalloc byte[0x28];
        using (var fs = File.OpenRead(path))
        {
            if (fs.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) < header.Length)
            {
                problems.Add($"{relative} ({name}) es demasiado corto para ser un GARC.");
                return;
            }
        }

        if (!header[..4].SequenceEqual("CRAG"u8) || !header.Slice(0x1C, 4).SequenceEqual("OTAF"u8))
        {
            problems.Add($"{relative} ({name}) no es un GARC.");
            return;
        }

        ushort version = BitConverter.ToUInt16(header[0x0A..]);
        if (version != GARC.VER_4)
            problems.Add($"{relative} ({name}) es un GARC versión 0x{version:X4}; X/Y usa 0x{GARC.VER_4:X4}.");

        ushort entries = BitConverter.ToUInt16(header[0x24..]);
        if (entries != expectedEntries)
            problems.Add($"{relative} ({name}) tiene {entries} entradas; X/Y tiene {expectedEntries}.");
    }

    /// <summary>
    /// Lee los títulos del SMDH (<c>icon.bin</c>): 16 entradas de 0x200 bytes desde 0x08, cada una
    /// empieza por la descripción corta en UTF-16 (0x80 bytes). Todas las no vacías deben acabar en X o en Y.
    /// </summary>
    private static GameTitle? ReadTitle(string iconPath, List<string> problems)
    {
        if (!File.Exists(iconPath))
        {
            problems.Add("Falta exefs/icon.bin: no se puede saber si es Pokémon X o Y.");
            return null;
        }

        byte[] smdh = File.ReadAllBytes(iconPath);
        if (smdh.Length < 0x2008 || !smdh.AsSpan(0, 4).SequenceEqual("SMDH"u8))
        {
            problems.Add("exefs/icon.bin no es un SMDH válido.");
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

        problems.Add($"El título de exefs/icon.bin no es Pokémon X ni Y: {string.Join(" | ", names)}");
        return null;
    }

    private static void CheckCode(string codePath, List<string> problems)
    {
        if (!File.Exists(codePath))
        {
            problems.Add("Falta exefs/code.bin.");
            return;
        }

        if (LooksBlzCompressed(codePath))
            problems.Add("exefs/code.bin está comprimido (BLZ). Hace falta la versión descomprimida.");
    }

    /// <summary>
    /// Un archivo BLZ termina en un pie de 8 bytes: longitud codificada (24 bits) + longitud de cabecera
    /// (1 byte, 8–11) y el incremento de tamaño al descomprimir (int32 &gt; 0). Mismas comprobaciones que
    /// <see cref="BLZCoder"/>. Un code.bin descomprimido no las supera.
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
