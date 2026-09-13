using System.Text.Json;

namespace Pokemanager.Model.Build;

public sealed record InstallResult(string ModDirectory, IReadOnlyList<string> Written, IReadOnlyList<string> Removed);

/// <summary>
/// Escribe el mod en <c>load/mods/&lt;TitleID&gt;</c>: la salida del randomizer y, encima, los GARC de
/// las ediciones manuales.
/// </summary>
/// <remarks>
/// Un manifiesto registra lo escrito para borrar en la siguiente instalación lo que ya no toque, sin
/// tocar archivos ajenos a la aplicación. Se niega a escribir dentro del volcado.
/// </remarks>
public static class ModInstaller
{
    public const string ManifestName = "pokemanager-build.json";

    /// <param name="modDirectory"><c>&lt;usuario&gt;/load/mods/&lt;TitleID&gt;</c>.</param>
    /// <param name="dumpDirectory">Volcado, solo para impedir escribir dentro de él.</param>
    /// <param name="randomizedTitleDirectory">Salida de UPR ZX (<c>romfs/</c> y <c>code.bin</c>), o null.</param>
    /// <param name="editOutputs">Resultado de <see cref="ModBuilder.BuildEdits"/>; sustituye a lo del randomizer.</param>
    public static InstallResult Install(
        string modDirectory, string dumpDirectory, string? randomizedTitleDirectory,
        IReadOnlyDictionary<string, byte[]> editOutputs)
    {
        modDirectory = Path.GetFullPath(modDirectory);
        GuardNotInsideDump(dumpDirectory, modDirectory);

        var written = new List<string>();

        // 1. Randomizer: romfs tal cual; code.bin a exefs/code.bin (ruta de override de ExeFS del emulador).
        if (randomizedTitleDirectory is not null)
        {
            string romfs = Path.Combine(randomizedTitleDirectory, "romfs");
            if (Directory.Exists(romfs))
            {
                foreach (string file in Directory.EnumerateFiles(romfs, "*", SearchOption.AllDirectories))
                {
                    string relative = "romfs/" + Path.GetRelativePath(romfs, file).Replace(Path.DirectorySeparatorChar, '/');
                    if (!editOutputs.ContainsKey(relative))
                        Copy(file, modDirectory, relative, written);
                }
            }

            string code = Path.Combine(randomizedTitleDirectory, "code.bin");
            if (File.Exists(code))
                Copy(code, modDirectory, "exefs/code.bin", written);
        }

        // 2. Ediciones manuales encima.
        foreach (var (relative, bytes) in editOutputs)
        {
            string path = Target(modDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
            written.Add(relative);
        }

        // 3. Lo que instalamos antes y ya no toca.
        var removed = new List<string>();
        foreach (string stale in ReadManifest(modDirectory).Except(written))
        {
            string path = Target(modDirectory, stale);
            if (File.Exists(path))
            {
                File.Delete(path);
                removed.Add(stale);
            }
        }

        WriteManifest(modDirectory, written);
        return new InstallResult(modDirectory, written, removed);
    }

    private static void Copy(string source, string modDirectory, string relative, List<string> written)
    {
        string target = Target(modDirectory, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(source, target, overwrite: true);
        written.Add(relative);
    }

    private static string Target(string modDirectory, string relative)
    {
        string path = Path.GetFullPath(Path.Combine(modDirectory, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(modDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Ruta fuera de la carpeta del mod: {relative}");
        return path;
    }

    private static void GuardNotInsideDump(string dumpDirectory, string modDirectory)
    {
        string dump = Path.GetFullPath(dumpDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string target = modDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (target.StartsWith(dump, StringComparison.OrdinalIgnoreCase) || dump.StartsWith(target, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"La carpeta del mod ({modDirectory}) no puede estar dentro del volcado ni contenerlo: el volcado es de solo lectura.");
    }

    private static IReadOnlyList<string> ReadManifest(string modDirectory)
    {
        string path = Path.Combine(modDirectory, ManifestName);
        if (!File.Exists(path))
            return [];
        return JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path))?.Files ?? [];
    }

    private static void WriteManifest(string modDirectory, List<string> files)
    {
        Directory.CreateDirectory(modDirectory);
        File.WriteAllText(Path.Combine(modDirectory, ManifestName),
            JsonSerializer.Serialize(new Manifest(files), new JsonSerializerOptions { WriteIndented = true }));
    }

    private sealed record Manifest(List<string> Files);
}
