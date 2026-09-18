using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Pokemanager.Randomizer.Resources;

namespace Pokemanager.Randomizer;

/// <summary>Result of a randomization: the game's LayeredFS folder and the UPR log.</summary>
/// <param name="TitleDirectory">Folder <c>&lt;output&gt;/&lt;TitleID&gt;</c> with <c>romfs/</c> and <c>code.bin</c>.</param>
/// <param name="Warnings">Localized warnings reported by UPR.</param>
public sealed record UprResult(long Seed, string TitleDirectory, string LogPath, IReadOnlyList<string> Warnings);

public sealed class UprException(string message, string output) : Exception(message)
{
    public string Output { get; } = output;
}

/// <summary>Runs the commands of <c>upr/PokemanagerUpr.java</c> on UPR ZX.</summary>
public sealed class UprRunner(UprTools tools)
{
    /// <summary>Path of the Java launcher copied next to the binaries.</summary>
    public static string LauncherPath => Path.Combine(AppContext.BaseDirectory, "upr", "PokemanagerUpr.java");

    /// <summary>A new seed, of the same order of magnitude as the ones UPR ZX generates.</summary>
    public static long NewSeed() => Random.Shared.NextInt64(1, 1L << 47);

    /// <summary>Warning codes printed by the launcher (<c>WARNING:CODE</c>) → resource keys.</summary>
    private static readonly Dictionary<string, string> WarningKeys = new()
    {
        ["CUSTOM_STARTERS_CHANGED"] = nameof(Strings.Upr_Warning_CustomStartersChanged),
        ["OLD_PRESET"] = nameof(Strings.Upr_Warning_OldPreset),
    };

    /// <summary>Randomizes and leaves the LayeredFS output in <paramref name="outputDirectory"/> (empty or missing).</summary>
    /// <param name="allItems">Let the item randomization use every item of the game (see the launcher's unlockItems).</param>
    public async Task<UprResult> RandomizeAsync(
        string presetFile, string romFile, long seed, string outputDirectory,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default, bool allItems = false)
    {
        if (Directory.Exists(outputDirectory) && Directory.EnumerateFileSystemEntries(outputDirectory).Any())
            throw new IOException(string.Format(Strings.Upr_OutputNotEmpty, outputDirectory));
        Directory.CreateDirectory(outputDirectory);
        string logPath = Path.Combine(outputDirectory, "upr.log");

        progress?.Report(string.Format(Strings.Upr_Randomizing, seed));
        string output = await RunAsync(
            ["randomize", presetFile, romFile, outputDirectory, seed.ToString(), logPath, allItems ? "1" : "0"],
            progress, cancellationToken);

        string? titleDir = Directory.GetDirectories(outputDirectory).FirstOrDefault(d => Path.GetFileName(d).Length == 16);
        if (titleDir is null || !File.Exists(logPath))
            throw new UprException(Strings.Upr_NoLayeredFs, output);

        var warnings = output.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("WARNING:", StringComparison.Ordinal))
            .Select(l => l["WARNING:".Length..].Trim())
            .Select(code => WarningKeys.TryGetValue(code, out var key) ? Strings.ResourceManager.GetString(key, Strings.Culture) ?? code : code)
            .ToList();
        return new UprResult(seed, titleDir, logPath, warnings);
    }

    /// <summary>Base ROM + the files in <paramref name="titleDirectory"/> (romfs/ and code.bin) → .cxi.</summary>
    public async Task PackAsync(string romFile, string titleDirectory, string outputCxi, long seed,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        progress?.Report(Strings.Upr_Packing);
        await RunAsync(["pack", romFile, titleDirectory, outputCxi, seed.ToString()], progress, cancellationToken);
        if (!File.Exists(outputCxi))
            throw new UprException(Strings.Upr_NoCxi, "");
    }

    /// <summary>Options of a preset (null: UPR ZX defaults).</summary>
    /// <param name="romFile">When given, marks which misc tweaks the game supports (takes a few seconds more).</param>
    public async Task<UprSettingsDescription> DescribeSettingsAsync(byte[]? preset, string? romFile = null, CancellationToken cancellationToken = default)
    {
        using var temp = new TempFile(preset);
        var args = new List<string> { "describe-settings", preset is null ? "-" : temp.Path };
        if (romFile is not null)
            args.Add(romFile);

        string output = await RunAsync(args, null, cancellationToken);
        // UPR prints traces while loading the ROM: the JSON is the last line starting with '{'.
        string json = output.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.StartsWith('{'))
                      ?? throw new UprException(Strings.Upr_NoDescription, output);
        return JsonSerializer.Deserialize<UprSettingsDescription>(json, JsonOptions)
               ?? throw new UprException(Strings.Upr_EmptyDescription, output);
    }

    /// <summary>Applies <paramref name="assignments"/> on top of <paramref name="basePreset"/> and returns the resulting .rnqs.</summary>
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
            throw new FileNotFoundException(Strings.Upr_LauncherMissing, LauncherPath);

        var psi = new ProcessStartInfo(tools.JavaPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            // UPR looks for auxiliary files (custom names) relative to the working directory.
            WorkingDirectory = Path.GetDirectoryName(tools.JarPath)!,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        // The installer compiles the launcher (PokemanagerUpr.class next to the .java), so the bundled runtime needs no
        // compiler; a development build runs the source file with Java's source launcher.
        string launcherDir = Path.GetDirectoryName(LauncherPath)!;
        string[] launcher = File.Exists(Path.Combine(launcherDir, "PokemanagerUpr.class"))
            ? ["-cp", tools.JarPath + Path.PathSeparator + launcherDir, "PokemanagerUpr"]
            : ["-cp", tools.JarPath, LauncherPath];
        foreach (string arg in new[] { "-Xmx4096M", "-Dfile.encoding=UTF-8", "-Dstdout.encoding=UTF-8" }.Concat(launcher))
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
            // UPR prints one line per romfs file: report progress without flooding the UI.
            if (!line.StartsWith("NCCH:", StringComparison.Ordinal) && !line.StartsWith('{') && !line.StartsWith("WARNING:", StringComparison.Ordinal))
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
            throw new UprException(string.Format(Strings.Upr_ExitCode, process.ExitCode, arguments[0]), text);
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
