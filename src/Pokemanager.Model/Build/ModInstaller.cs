using System.Text.Json;
using Pokemanager.Model.Resources;

namespace Pokemanager.Model.Build;

public sealed record InstallResult(string ModDirectory, IReadOnlyList<string> Written, IReadOnlyList<string> Removed);

/// <summary>
/// LayeredFS mod folder (<c>load/mods/&lt;TitleID&gt;</c>): randomizer output with the manual edit GARCs on top.
/// </summary>
/// <remarks>
/// The app now builds ROM files instead of installing mods, because a mod applies to every ROM of the game.
/// <see cref="Uninstall"/> removes what earlier versions installed. A manifest records what was written so that
/// files that no longer apply are removed without touching anything the app did not create. Writing inside the dump
/// is refused.
/// </remarks>
public static class ModInstaller
{
    public const string ManifestName = "pokemanager-build.json";

    /// <param name="modDirectory"><c>&lt;user&gt;/load/mods/&lt;TitleID&gt;</c>.</param>
    /// <param name="dumpDirectory">The dump, only to refuse writing inside it.</param>
    /// <param name="randomizedTitleDirectory">UPR ZX output (<c>romfs/</c> and <c>code.bin</c>), or null.</param>
    /// <param name="editOutputs">Result of <see cref="ModBuilder.BuildEdits"/>; overrides the randomizer files.</param>
    public static InstallResult Install(
        string modDirectory, string dumpDirectory, string? randomizedTitleDirectory,
        IReadOnlyDictionary<string, byte[]> editOutputs)
    {
        modDirectory = Path.GetFullPath(modDirectory);
        GuardNotInsideDump(dumpDirectory, modDirectory);

        var written = new List<string>();

        // 1. Randomizer: romfs as is; code.bin to exefs/code.bin (the emulator's ExeFS override path).
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

        // 2. Manual edits on top.
        foreach (var (relative, bytes) in editOutputs)
        {
            string path = Target(modDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
            written.Add(relative);
        }

        // 3. What an earlier install wrote and no longer applies.
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

    /// <summary>
    /// Removes from <paramref name="modDirectory"/> what the app installed (according to its manifest) and nothing
    /// else, then deletes folders left empty. Returns the removed files.
    /// </summary>
    public static IReadOnlyList<string> Uninstall(string modDirectory)
    {
        modDirectory = Path.GetFullPath(modDirectory);
        if (!File.Exists(Path.Combine(modDirectory, ManifestName)))
            return [];

        var removed = new List<string>();
        foreach (string relative in ReadManifest(modDirectory))
        {
            string path = Target(modDirectory, relative);
            if (File.Exists(path))
            {
                File.Delete(path);
                removed.Add(relative);
            }
        }
        File.Delete(Path.Combine(modDirectory, ManifestName));

        foreach (string dir in Directory.EnumerateDirectories(modDirectory, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
        {
            if (!Directory.EnumerateFileSystemEntries(dir).Any())
                Directory.Delete(dir);
        }
        if (!Directory.EnumerateFileSystemEntries(modDirectory).Any())
            Directory.Delete(modDirectory);
        return removed;
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
            throw new InvalidOperationException(string.Format(Strings.Mod_OutsideFolder, relative));
        return path;
    }

    private static void GuardNotInsideDump(string dumpDirectory, string modDirectory)
    {
        string dump = Path.GetFullPath(dumpDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string target = modDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (target.StartsWith(dump, StringComparison.OrdinalIgnoreCase) || dump.StartsWith(target, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(string.Format(Strings.Mod_InsideDump, modDirectory));
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
