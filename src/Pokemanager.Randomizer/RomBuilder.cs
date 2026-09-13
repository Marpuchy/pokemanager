namespace Pokemanager.Randomizer;

public sealed record RomBuildResult(string RomPath, string? LogPath, int FilesChanged);

/// <summary>
/// Crea la ROM final como archivo nuevo (.cxi): ROM base + salida del randomizer + ediciones manuales.
/// La ROM base no se modifica nunca.
/// </summary>
public static class RomBuilder
{
    /// <summary>Extensión de la ROM generada. UPR ZX solo sabe escribir juegos de 3DS como NCCH (.cxi).</summary>
    public const string Extension = ".cxi";

    /// <summary>Caracteres no válidos o nombre vacío. Devuelve el error o null.</summary>
    public static string? ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Pon un nombre a la ROM randomizada.";
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return "El nombre contiene caracteres no válidos para un archivo.";
        return null;
    }

    /// <summary><c>&lt;carpeta de la ROM base&gt;/&lt;nombre&gt;.cxi</c>.</summary>
    public static string OutputPath(string baseRom, string name) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(baseRom))!, name.Trim() + Extension);

    /// <param name="random">Salida de UPR ZX, o null para crear la ROM solo con las ediciones.</param>
    /// <param name="edits">Rutas relativas (<c>romfs/a/2/1/8</c>) → contenido; sustituyen a lo del randomizer.</param>
    /// <param name="workRoot">Carpeta de trabajo para montar los archivos antes de empaquetar.</param>
    public static async Task<RomBuildResult> BuildAsync(
        UprRunner runner, string baseRom, UprResult? random, IReadOnlyDictionary<string, byte[]> edits,
        string outputRom, long seed, string workRoot,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        outputRom = Path.GetFullPath(outputRom);
        if (string.Equals(outputRom, Path.GetFullPath(baseRom), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("La ROM randomizada no puede sustituir a la ROM base.");

        string work = Path.Combine(workRoot, "montaje-" + Guid.NewGuid().ToString("N"));
        string title = Path.Combine(work, "titulo");
        try
        {
            int files = 0;
            if (random is not null)
            {
                string romfs = Path.Combine(random.TitleDirectory, "romfs");
                if (Directory.Exists(romfs))
                {
                    foreach (string file in Directory.EnumerateFiles(romfs, "*", SearchOption.AllDirectories))
                    {
                        string target = Path.Combine(title, "romfs", Path.GetRelativePath(romfs, file));
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        File.Copy(file, target);
                        files++;
                    }
                }
                string code = Path.Combine(random.TitleDirectory, "code.bin");
                if (File.Exists(code))
                {
                    Directory.CreateDirectory(title);
                    File.Copy(code, Path.Combine(title, "code.bin"));
                    files++;
                }
            }

            foreach (var (relative, bytes) in edits)
            {
                if (!relative.StartsWith("romfs/", StringComparison.Ordinal))
                    throw new InvalidOperationException($"Solo se admiten ediciones del romfs: {relative}");
                string target = Path.Combine(title, relative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                bool replaced = File.Exists(target);
                await File.WriteAllBytesAsync(target, bytes, cancellationToken);
                if (!replaced)
                    files++;
            }
            Directory.CreateDirectory(title);

            // Se escribe con otro nombre y se renombra al final: nunca queda una ROM a medias con el nombre bueno.
            string partial = outputRom + ".parcial";
            if (File.Exists(partial))
                File.Delete(partial);
            await runner.PackAsync(baseRom, title, partial, seed, progress, cancellationToken);
            File.Move(partial, outputRom, overwrite: true);

            string? log = null;
            if (random is not null && File.Exists(random.LogPath))
            {
                log = outputRom + ".log";
                File.Copy(random.LogPath, log, overwrite: true);
            }
            return new RomBuildResult(outputRom, log, files);
        }
        finally
        {
            if (Directory.Exists(work))
                Directory.Delete(work, recursive: true);
        }
    }
}
