using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Pokemanager.Randomizer;

/// <summary>Resultado de una randomización: carpeta LayeredFS del juego y log de UPR.</summary>
/// <param name="TitleDirectory">Carpeta <c>&lt;salida&gt;/&lt;TitleID&gt;</c> con <c>romfs/</c> y <c>code.bin</c>.</param>
public sealed record UprResult(long Seed, string TitleDirectory, string LogPath, IReadOnlyList<string> Warnings);

public sealed class UprException(string message, string output) : Exception(message)
{
    public string Output { get; } = output;
}

/// <summary>Ejecuta las órdenes de <c>upr/PokemanagerUpr.java</c> sobre UPR ZX.</summary>
public sealed class UprRunner(UprTools tools)
{
    /// <summary>Ruta del lanzador Java copiado junto a los binarios.</summary>
    public static string LauncherPath => Path.Combine(AppContext.BaseDirectory, "upr", "PokemanagerUpr.java");

    /// <summary>Semilla nueva, del mismo orden de magnitud que las que genera UPR ZX.</summary>
    public static long NewSeed() => Random.Shared.NextInt64(1, 1L << 47);

    /// <summary>Randomiza y deja la salida LayeredFS en <paramref name="outputDirectory"/> (vacía o inexistente).</summary>
    public async Task<UprResult> RandomizeAsync(
        string presetFile, string romFile, long seed, string outputDirectory,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (Directory.Exists(outputDirectory) && Directory.EnumerateFileSystemEntries(outputDirectory).Any())
            throw new IOException($"La carpeta de salida de UPR debe estar vacía: {outputDirectory}");
        Directory.CreateDirectory(outputDirectory);
        string logPath = Path.Combine(outputDirectory, "upr.log");

        progress?.Report($"Randomizando con la semilla {seed}…");
        string output = await RunAsync(["randomize", presetFile, romFile, outputDirectory, seed.ToString(), logPath], progress, cancellationToken);

        string? titleDir = Directory.GetDirectories(outputDirectory).FirstOrDefault(d => Path.GetFileName(d).Length == 16);
        if (titleDir is null || !File.Exists(logPath))
            throw new UprException("UPR ZX no generó la carpeta LayeredFS esperada.", output);

        var warnings = output.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("AVISO:", StringComparison.Ordinal))
            .Select(l => l["AVISO:".Length..].Trim())
            .ToList();
        return new UprResult(seed, titleDir, logPath, warnings);
    }

    /// <summary>ROM base + archivos de <paramref name="titleDirectory"/> (romfs/ y code.bin) → .cxi.</summary>
    public async Task PackAsync(string romFile, string titleDirectory, string outputCxi, long seed,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        progress?.Report("Creando la ROM randomizada…");
        await RunAsync(["pack", romFile, titleDirectory, outputCxi, seed.ToString()], progress, cancellationToken);
        if (!File.Exists(outputCxi))
            throw new UprException("UPR ZX no generó el .cxi.", "");
    }

    /// <summary>Opciones de un preset (null: ajustes por defecto de UPR ZX).</summary>
    /// <param name="romFile">Si se indica, marca qué ajustes varios admite el juego (tarda unos segundos más).</param>
    public async Task<UprSettingsDescription> DescribeSettingsAsync(byte[]? preset, string? romFile = null, CancellationToken cancellationToken = default)
    {
        using var temp = new TempFile(preset);
        var args = new List<string> { "describe-settings", preset is null ? "-" : temp.Path };
        if (romFile is not null)
            args.Add(romFile);

        string output = await RunAsync(args, null, cancellationToken);
        // UPR escribe trazas al cargar la ROM: el JSON es la última línea que empieza por '{'.
        string json = output.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.StartsWith('{'))
                      ?? throw new UprException("UPR ZX no devolvió la descripción de los ajustes.", output);
        return JsonSerializer.Deserialize<UprSettingsDescription>(json, JsonOptions)
               ?? throw new UprException("Descripción de ajustes vacía.", output);
    }

    /// <summary>Aplica <paramref name="assignments"/> sobre <paramref name="basePreset"/> y devuelve el .rnqs resultante.</summary>
    public async Task<byte[]> WriteSettingsAsync(byte[]? basePreset, IEnumerable<KeyValuePair<string, string>> assignments,
        CancellationToken cancellationToken = default)
    {
        using var baseFile = new TempFile(basePreset);
        using var lines = new TempFile(Encoding.UTF8.GetBytes(string.Join('\n', assignments.Select(a => $"{a.Key}={a.Value}"))));
        using var output = new TempFile(null);

        await RunAsync(["write-settings", basePreset is null ? "-" : baseFile.Path, lines.Path, output.Path], null, cancellationToken);
        return await File.ReadAllBytesAsync(output.Path, cancellationToken);
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private async Task<string> RunAsync(IReadOnlyList<string> arguments, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        if (!File.Exists(LauncherPath))
            throw new FileNotFoundException("Falta el lanzador de UPR junto a la aplicación.", LauncherPath);

        var psi = new ProcessStartInfo(tools.JavaPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            // UPR busca archivos auxiliares (nombres personalizados) relativos al directorio de trabajo.
            WorkingDirectory = Path.GetDirectoryName(tools.JarPath)!,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (string arg in new[] { "-Xmx4096M", "-Dfile.encoding=UTF-8", "-Dstdout.encoding=UTF-8", "-cp", tools.JarPath, LauncherPath })
            psi.ArgumentList.Add(arg);
        foreach (string arg in arguments)
            psi.ArgumentList.Add(arg);

        var output = new StringBuilder();
        using var process = new Process { StartInfo = psi };

        void OnLine(string? line)
        {
            if (line is null)
                return;
            lock (output)
                output.AppendLine(line);
            // UPR imprime una línea por archivo del romfs: se informa sin inundar la interfaz.
            if (!line.StartsWith("NCCH:", StringComparison.Ordinal) && !line.StartsWith('{'))
                progress?.Report(line);
        }

        process.OutputDataReceived += (_, e) => OnLine(e.Data);
        process.ErrorDataReceived += (_, e) => OnLine(e.Data);
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw;
        }

        string text;
        lock (output)
            text = output.ToString();
        if (process.ExitCode != 0)
            throw new UprException($"UPR ZX terminó con código {process.ExitCode} ({arguments[0]}).", text);
        return text;
    }

    private sealed class TempFile : IDisposable
    {
        public string Path { get; } = System.IO.Path.GetTempFileName();

        public TempFile(byte[]? content)
        {
            if (content is not null)
                File.WriteAllBytes(Path, content);
        }

        public void Dispose()
        {
            try { File.Delete(Path); } catch (IOException) { }
        }
    }
}
