using System.Diagnostics;
using System.Text;

namespace Pokemanager.Randomizer;

/// <summary>Resultado de una randomización: carpeta LayeredFS del juego y log de UPR.</summary>
/// <param name="TitleDirectory">Carpeta <c>&lt;salida&gt;/&lt;TitleID&gt;</c> con <c>romfs/</c> y <c>code.bin</c>.</param>
public sealed record UprResult(long Seed, string TitleDirectory, string LogPath, IReadOnlyList<string> Warnings);

public sealed class UprException(string message, string output) : Exception(message)
{
    public string Output { get; } = output;
}

/// <summary>Ejecuta UPR ZX con una semilla fija mediante <c>upr/PokemanagerUpr.java</c>.</summary>
public sealed class UprRunner(UprTools tools)
{
    /// <summary>Ruta del lanzador Java copiado junto a los binarios.</summary>
    public static string LauncherPath => Path.Combine(AppContext.BaseDirectory, "upr", "PokemanagerUpr.java");

    /// <summary>Semilla nueva, del mismo orden de magnitud que las que genera UPR ZX.</summary>
    public static long NewSeed() => Random.Shared.NextInt64(1, 1L << 47);

    /// <param name="presetFile">Ajustes <c>.rnqs</c> de UPR ZX.</param>
    /// <param name="romFile">ROM descifrada (.3ds / .cxi) de Pokémon X o Y.</param>
    /// <param name="outputDirectory">Carpeta vacía o inexistente donde UPR dejará <c>&lt;TitleID&gt;/</c>.</param>
    public async Task<UprResult> RunAsync(
        string presetFile, string romFile, long seed, string outputDirectory,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(LauncherPath))
            throw new FileNotFoundException("Falta el lanzador de UPR junto a la aplicación.", LauncherPath);

        if (Directory.Exists(outputDirectory) && Directory.EnumerateFileSystemEntries(outputDirectory).Any())
            throw new IOException($"La carpeta de salida de UPR debe estar vacía: {outputDirectory}");
        Directory.CreateDirectory(outputDirectory);
        string logPath = Path.Combine(outputDirectory, "upr.log");

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
        foreach (string arg in new[] { "-Xmx4096M", "-cp", tools.JarPath, LauncherPath, presetFile, romFile, outputDirectory, seed.ToString(), logPath })
            psi.ArgumentList.Add(arg);

        var output = new StringBuilder();
        var warnings = new List<string>();
        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        void OnLine(string? line)
        {
            if (line is null)
                return;
            lock (output)
                output.AppendLine(line);
            if (line.StartsWith("AVISO:", StringComparison.Ordinal))
                lock (warnings)
                    warnings.Add(line["AVISO:".Length..].Trim());
            // UPR imprime una línea por archivo del romfs al leerlo: se informa sin inundar la interfaz.
            if (!line.StartsWith("NCCH: Visiting", StringComparison.Ordinal))
                progress?.Report(line);
        }

        process.OutputDataReceived += (_, e) => OnLine(e.Data);
        process.ErrorDataReceived += (_, e) => OnLine(e.Data);

        progress?.Report($"Ejecutando UPR ZX con la semilla {seed}…");
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
            throw new UprException($"UPR ZX terminó con código {process.ExitCode}.", text);

        string? titleDir = Directory.GetDirectories(outputDirectory).FirstOrDefault(d => Path.GetFileName(d).Length == 16);
        if (titleDir is null || !File.Exists(logPath))
            throw new UprException("UPR ZX no generó la carpeta LayeredFS esperada.", text);

        return new UprResult(seed, titleDir, logPath, warnings);
    }
}
