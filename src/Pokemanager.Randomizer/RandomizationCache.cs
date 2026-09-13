using System.Security.Cryptography;
using System.Text;

namespace Pokemanager.Randomizer;

/// <summary>
/// Guarda la salida de UPR ZX por (ROM, preset, semilla, versión del jar) para no repetir la
/// randomización. Es regenerable: borrar la caché solo cuesta volver a ejecutar UPR.
/// </summary>
public sealed class RandomizationCache(string root)
{
    public static string DefaultRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pokemanager", "random");

    public string Root { get; } = root;

    /// <summary>Clave estable: cambia si cambia la ROM (ruta, tamaño, fecha), el preset, la semilla o el jar.</summary>
    public static string Key(string romFile, byte[] preset, long seed, string jarFile)
    {
        var rom = new FileInfo(romFile);
        var jar = new FileInfo(jarFile);
        string material = string.Join('|',
            Path.GetFullPath(romFile).ToUpperInvariant(), rom.Length, rom.LastWriteTimeUtc.Ticks,
            Convert.ToHexString(SHA256.HashData(preset)), seed,
            jar.Length, jar.LastWriteTimeUtc.Ticks);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)))[..24];
    }

    /// <summary>Salida ya generada para esos parámetros, sin ejecutar UPR. Null si no está en caché.</summary>
    public UprResult? TryGet(string romFile, byte[] preset, long seed, string jarFile)
    {
        string dir = Path.Combine(Root, Key(romFile, preset, seed, jarFile));
        return File.Exists(Path.Combine(dir, ".completo")) ? FindResult(Path.Combine(dir, "salida"), seed) : null;
    }

    /// <summary>Devuelve la salida en caché o ejecuta UPR para generarla.</summary>
    public async Task<UprResult> GetOrCreateAsync(
        UprRunner runner, UprTools tools, string romFile, byte[] preset, long seed,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (TryGet(romFile, preset, seed, tools.JarPath) is { } cached)
        {
            progress?.Report($"Usando la randomización en caché de la semilla {seed}.");
            return cached;
        }

        string dir = Path.Combine(Root, Key(romFile, preset, seed, tools.JarPath));
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true); // incompleta: la creó esta caché y se rehace
        Directory.CreateDirectory(dir);

        // UPR solo lee el preset de disco.
        string presetFile = Path.Combine(dir, "preset.rnqs");
        await File.WriteAllBytesAsync(presetFile, preset, cancellationToken);

        var result = await runner.RandomizeAsync(presetFile, romFile, seed, Path.Combine(dir, "salida"), progress, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(dir, ".completo"), DateTime.UtcNow.ToString("O"), cancellationToken);
        return result;
    }

    private static UprResult? FindResult(string output, long seed)
    {
        string log = Path.Combine(output, "upr.log");
        string? title = Directory.Exists(output)
            ? Directory.GetDirectories(output).FirstOrDefault(d => Path.GetFileName(d).Length == 16)
            : null;
        return title is not null && File.Exists(log) ? new UprResult(seed, title, log, []) : null;
    }
}
