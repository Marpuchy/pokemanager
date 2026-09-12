using System.Text;
using pk3DS.Core.CTR;
using Pokemanager.Model.Dump;

namespace Pokemanager.Tests.Dump;

/// <summary>
/// Volcado falso con la forma mínima que exige <see cref="DumpInspector"/>: 271 archivos en romfs/a,
/// los GARC de firma con sus entradas, icon.bin con título y un code.bin descomprimido.
/// Vive en una carpeta temporal que se borra al terminar.
/// </summary>
internal sealed class SyntheticDump : IDisposable
{
    public string Root { get; } = Directory.CreateTempSubdirectory("pokemanager-dump-").FullName;
    public string RomFs => Path.Combine(Root, "romfs");
    public string ExeFs => Path.Combine(Root, "exefs");

    public SyntheticDump(string title = "Pokémon X")
    {
        for (int n = 0; n < DumpInspector.FileCountXY; n++)
            WriteRomFsFile(GarcPath(n), []);

        foreach (var (path, _, entries) in DumpInspector.SignatureGarcs)
            WriteGarc(path, entries);

        Directory.CreateDirectory(ExeFs);
        WriteIcon(title);
        File.WriteAllBytes(CodePath, Enumerable.Range(0, 0x4000).Select(i => (byte)(i % 7)).ToArray());
    }

    public string CodePath => Path.Combine(ExeFs, "code.bin");
    public string IconPath => Path.Combine(ExeFs, "icon.bin");

    public void WriteGarc(string relativePath, int entries, ushort version = GARC.VER_4)
    {
        byte[] garc = GARC.PackGARC(Enumerable.Repeat(Array.Empty<byte>(), entries).ToArray(), GARC.VER_4, 4).Data;
        BitConverter.TryWriteBytes(garc.AsSpan(0x0A), version);
        WriteRomFsFile(relativePath, garc);
    }

    public void WriteIcon(string title)
    {
        var smdh = new byte[0x36C0];
        "SMDH"u8.CopyTo(smdh);
        byte[] name = Encoding.Unicode.GetBytes(title);
        for (int i = 0; i < 16; i++)
            name.CopyTo(smdh, 0x08 + (i * 0x200));
        File.WriteAllBytes(IconPath, smdh);
    }

    public void WriteRomFsFile(string relativePath, byte[] data)
    {
        string path = Path.Combine(RomFs, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, data);
    }

    private static string GarcPath(int number) => $"a/{number / 100}/{number / 10 % 10}/{number % 10}";

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch (IOException) { }
    }
}
