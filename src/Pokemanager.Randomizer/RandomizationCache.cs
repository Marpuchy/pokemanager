using System.Security.Cryptography;
using System.Text;
using Pokemanager.Randomizer.Resources;

namespace Pokemanager.Randomizer;

/// <summary>
/// Stores UPR ZX output per (ROM, preset, seed, jar version) so the randomization is not repeated. Regenerable:
/// deleting the cache only costs running UPR again.
/// </summary>
public sealed class RandomizationCache(string root)
{
    private const string CompleteMarker = ".complete";
    private const string OutputFolder = "output";

    public static string DefaultRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pokemanager", "random");

    public string Root { get; } = root;

    /// <summary>
    /// Stable key: changes when the ROM (path, size, date), the preset, the seed, the jar or the item unlock changes.
    /// </summary>
    public static string Key(string romFile, byte[] preset, long seed, string jarFile, bool allItems = false)
    {
        var rom = new FileInfo(romFile);
        var jar = new FileInfo(jarFile);
        string material = string.Join('|',
            Path.GetFullPath(romFile).ToUpperInvariant(), rom.Length, rom.LastWriteTimeUtc.Ticks,
            Convert.ToHexString(SHA256.HashData(preset)), seed,
            jar.Length, jar.LastWriteTimeUtc.Ticks, allItems ? "all-items" : "");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)))[..24];
    }

    /// <summary>Output already generated for those parameters, without running UPR. Null if not cached.</summary>
    public UprResult? TryGet(string romFile, byte[] preset, long seed, string jarFile, bool allItems = false)
    {
        string dir = Path.Combine(Root, Key(romFile, preset, seed, jarFile, allItems));
        return File.Exists(Path.Combine(dir, CompleteMarker)) ? FindResult(Path.Combine(dir, OutputFolder), seed) : null;
    }

    /// <summary>Returns the cached output or runs UPR to generate it.</summary>
    /// <param name="allItems">Part of the key: the same seed with and without it are two different randomizations.</param>
    public async Task<UprResult> GetOrCreateAsync(
        UprRunner runner, UprTools tools, string romFile, byte[] preset, long seed,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default, bool allItems = false)
    {
        if (TryGet(romFile, preset, seed, tools.JarPath, allItems) is { } cached)
        {
            progress?.Report(string.Format(Strings.Upr_UsingCache, seed));
            // Mark it as used now so Prune keeps it.
            File.SetLastWriteTimeUtc(Path.Combine(Root, Key(romFile, preset, seed, tools.JarPath, allItems), CompleteMarker), DateTime.UtcNow);
            return cached;
        }

        string dir = Path.Combine(Root, Key(romFile, preset, seed, tools.JarPath, allItems));
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true); // incomplete: this cache created it, so it is redone
        Directory.CreateDirectory(dir);

        // UPR only reads the preset from disk.
        string presetFile = Path.Combine(dir, "preset.rnqs");
        await File.WriteAllBytesAsync(presetFile, preset, cancellationToken);

        var result = await runner.RandomizeAsync(presetFile, romFile, seed, Path.Combine(dir, OutputFolder), progress, cancellationToken, allItems);
        await File.WriteAllTextAsync(Path.Combine(dir, CompleteMarker), DateTime.UtcNow.ToString("O"), cancellationToken);
        return result;
    }

    /// <summary>Deletes cached randomizations except the <paramref name="keep"/> most recent and the given one.</summary>
    public void Prune(int keep, string? keepTitleDirectory = null)
    {
        if (!Directory.Exists(Root))
            return;
        var entries = Directory.GetDirectories(Root)
            .Where(d => File.Exists(Path.Combine(d, CompleteMarker)))
            .OrderByDescending(d => File.GetLastWriteTimeUtc(Path.Combine(d, CompleteMarker)))
            .ToList();
        foreach (string dir in entries.Skip(keep))
        {
            if (keepTitleDirectory is not null && keepTitleDirectory.StartsWith(dir, StringComparison.OrdinalIgnoreCase))
                continue;
            try { Directory.Delete(dir, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
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
