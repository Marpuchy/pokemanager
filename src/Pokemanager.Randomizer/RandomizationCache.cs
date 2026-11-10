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
    public static string Key(string romFile, byte[] preset, long seed, string jarFile, bool allItems = false, IReadOnlyList<int>? goodItems = null)
    {
        var rom = new FileInfo(romFile);
        var jar = new FileInfo(jarFile);
        string material = string.Join('|',
            Path.GetFullPath(romFile).ToUpperInvariant(), rom.Length, rom.LastWriteTimeUtc.Ticks,
            Convert.ToHexString(SHA256.HashData(preset)), seed,
            jar.Length, jar.LastWriteTimeUtc.Ticks, allItems ? "all-items" : "", string.Join('.', goodItems ?? []),
            PatchStamp(jarFile));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)))[..24];
    }

    private static readonly Dictionary<string, string> patchStamps = new(StringComparer.Ordinal);

    /// <summary>
    /// What the patched classes next to the jar (<c>patch/</c>) contain, hashed: a fix to them is a different randomizer
    /// (the expansion mod's abilities past 255 came out wrong before one), so an output made by the old ones is not reused.
    /// Empty when there is no such folder, which keeps the keys of an installation without it as they were.
    /// </summary>
    private static string PatchStamp(string jarFile)
    {
        string dir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(jarFile)) ?? "", "patch");
        lock (patchStamps)
        {
            if (patchStamps.TryGetValue(dir, out var known))
                return known;
            string stamp = "";
            if (Directory.Exists(dir))
            {
                using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                foreach (string file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
                {
                    sha.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(dir, file).Replace('\\', '/')));
                    sha.AppendData(File.ReadAllBytes(file));
                }
                stamp = Convert.ToHexString(sha.GetHashAndReset())[..16];
            }
            return patchStamps[dir] = stamp;
        }
    }

    /// <summary>Output already generated for those parameters, without running UPR. Null if not cached.</summary>
    public UprResult? TryGet(string romFile, byte[] preset, long seed, string jarFile, bool allItems = false, IReadOnlyList<int>? goodItems = null)
    {
        string dir = Path.Combine(Root, Key(romFile, preset, seed, jarFile, allItems, goodItems));
        return File.Exists(Path.Combine(dir, CompleteMarker)) ? FindResult(Path.Combine(dir, OutputFolder), seed) : null;
    }

    /// <summary>Returns the cached output or runs UPR to generate it.</summary>
    /// <param name="allItems">Part of the key: the same seed with and without it are two different randomizations.</param>
    public async Task<UprResult> GetOrCreateAsync(
        UprRunner runner, UprTools tools, string romFile, byte[] preset, long seed,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default, bool allItems = false,
        IReadOnlyList<int>? goodItems = null, UprGameCounts? counts = null)
    {
        if (TryGet(romFile, preset, seed, tools.JarPath, allItems, goodItems) is { } cached)
        {
            progress?.Report(string.Format(Strings.Upr_UsingCache, seed));
            // Mark it as used now so Prune keeps it.
            File.SetLastWriteTimeUtc(Path.Combine(Root, Key(romFile, preset, seed, tools.JarPath, allItems, goodItems), CompleteMarker), DateTime.UtcNow);
            return cached;
        }

        string dir = Path.Combine(Root, Key(romFile, preset, seed, tools.JarPath, allItems, goodItems));
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true); // incomplete: this cache created it, so it is redone
        Directory.CreateDirectory(dir);

        // UPR only reads the preset from disk.
        string presetFile = Path.Combine(dir, "preset.rnqs");
        await File.WriteAllBytesAsync(presetFile, preset, cancellationToken);

        var result = await runner.RandomizeAsync(presetFile, romFile, seed, Path.Combine(dir, OutputFolder), progress, cancellationToken, allItems, goodItems, counts);
        await File.WriteAllTextAsync(Path.Combine(dir, CompleteMarker), DateTime.UtcNow.ToString("O"), cancellationToken);
        return result;
    }

    /// <summary>The complete output in the cache folder <paramref name="key"/>, if it is there and of that seed.</summary>
    public UprResult? TryGetFolder(string key, long seed)
    {
        if (key.Length == 0 || key.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return null;
        string dir = Path.Combine(Root, key);
        return File.Exists(Path.Combine(dir, CompleteMarker)) && LogSeed(Path.Combine(dir, OutputFolder, "upr.log")) == seed
            ? FindResult(Path.Combine(dir, OutputFolder), seed) is { } found ? found with { Earlier = true } : null
            : null;
    }

    /// <summary>
    /// A complete output of the same preset and seed whose key is no longer the one asked for — made before the patched
    /// classes changed (their hash is in the key) — the one last used by a build first. It is how the randomization the
    /// player's ROM was built from is still found after an update of the randomizer (2026-11-10: the editor showed the
    /// game without it, and an edit of the save wrote the original abilities back).
    /// </summary>
    public UprResult? FindEarlier(byte[] preset, long seed)
    {
        if (!Directory.Exists(Root))
            return null;
        foreach (string dir in Directory.GetDirectories(Root)
                     .Where(d => File.Exists(Path.Combine(d, CompleteMarker)))
                     .OrderByDescending(d => File.GetLastWriteTimeUtc(Path.Combine(d, CompleteMarker))))
        {
            string stored = Path.Combine(dir, "preset.rnqs");
            if (!File.Exists(stored) || !File.ReadAllBytes(stored).AsSpan().SequenceEqual(preset))
                continue;
            if (LogSeed(Path.Combine(dir, OutputFolder, "upr.log")) != seed)
                continue;
            if (FindResult(Path.Combine(dir, OutputFolder), seed) is { } found)
                return found with { Earlier = true };
        }
        return null;
    }

    /// <summary>The "Random Seed:" line near the top of UPR ZX's log.</summary>
    private static long? LogSeed(string log)
    {
        if (!File.Exists(log))
            return null;
        try
        {
            using var reader = new StreamReader(log);
            for (int i = 0; i < 40 && reader.ReadLine() is { } line; i++)
                if (line.StartsWith("Random Seed:", StringComparison.Ordinal) && long.TryParse(line["Random Seed:".Length..].Trim(), out long seed))
                    return seed;
        }
        catch (IOException)
        {
        }
        return null;
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
