using System.Text.Json;
using Pokemanager.Model.Mods;

namespace Pokemanager.Model.Build;

/// <summary>What <see cref="ModEmulatorFiles.Sync"/> did: files copied, files already there, files taken away.</summary>
public sealed record ModEmulatorFilesResult(IReadOnlyList<string> Copied, IReadOnlyList<string> Kept, IReadOnlyList<string> Removed);

/// <summary>
/// The files of a mod that cannot go into the built ROM, placed in the emulator's LayeredFS folder for the game
/// (<c>&lt;user&gt;/load/mods/&lt;TitleID&gt;/romfs/…</c>) instead.
/// </summary>
/// <remarks>
/// <para>
/// Only what is too big for the packer goes here (<see cref="ModInstallation.EmulatorContents"/>): the USUM
/// Expansion's model archive, 2.27 GB. Without it the game has the new species' data but not their models, so a
/// Skeledirge has no sprite in battle, follows nobody and a Mega Dragonite cannot be drawn.
/// </para>
/// <para>
/// A LayeredFS file applies to <b>every</b> ROM of that title the emulator opens, which is why the rest of the build
/// stopped installing there. This is the exception, kept as small as it can be: one manifest of its own
/// (<see cref="ManifestName"/>) lists exactly what it put there, a build of a project without such files takes them
/// away again, and nothing it did not write is touched. The file is copied only when it is not there already with the
/// same size and time, so a rebuild does not copy gigabytes again.
/// </para>
/// </remarks>
public static class ModEmulatorFiles
{
    public const string ManifestName = "pokemanager-mod-files.json";

    /// <param name="modDirectory"><c>&lt;user&gt;/load/mods/&lt;TitleID&gt;</c> of the emulator the player uses.</param>
    /// <param name="files">What the project's mods need there; empty takes away everything this put there before.</param>
    public static ModEmulatorFilesResult Sync(string modDirectory, IReadOnlyList<ModContent> files)
    {
        modDirectory = Path.GetFullPath(modDirectory);
        var copied = new List<string>();
        var kept = new List<string>();
        var wanted = new List<string>();

        foreach (var file in files)
        {
            string target = Target(modDirectory, file.TitlePath);
            wanted.Add(file.TitlePath);
            var source = new FileInfo(file.LocalPath);
            var existing = new FileInfo(target);
            if (existing.Exists && existing.Length == source.Length && existing.LastWriteTimeUtc == source.LastWriteTimeUtc)
            {
                kept.Add(file.TitlePath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            string partial = target + ".partial";
            File.Copy(source.FullName, partial, overwrite: true);
            File.SetLastWriteTimeUtc(partial, source.LastWriteTimeUtc);
            File.Move(partial, target, overwrite: true);
            copied.Add(file.TitlePath);
        }

        var removed = new List<string>();
        foreach (string stale in ReadManifest(modDirectory).Except(wanted, StringComparer.Ordinal))
        {
            string path = Target(modDirectory, stale);
            if (File.Exists(path))
            {
                File.Delete(path);
                removed.Add(stale);
            }
        }

        string manifest = Path.Combine(modDirectory, ManifestName);
        if (wanted.Count > 0)
        {
            Directory.CreateDirectory(modDirectory);
            File.WriteAllText(manifest, JsonSerializer.Serialize(wanted, new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            if (File.Exists(manifest))
                File.Delete(manifest);
            RemoveEmptyFolders(modDirectory);
        }

        return new ModEmulatorFilesResult(copied, kept, removed);
    }

    private static string Target(string modDirectory, string titlePath)
    {
        string path = Path.GetFullPath(Path.Combine(modDirectory, titlePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(modDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(string.Format(Resources.Strings.Mod_OutsideFolder, titlePath));
        return path;
    }

    private static IReadOnlyList<string> ReadManifest(string modDirectory)
    {
        string path = Path.Combine(modDirectory, ManifestName);
        if (!File.Exists(path))
            return [];
        try
        {
            return JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path)) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static void RemoveEmptyFolders(string modDirectory)
    {
        if (!Directory.Exists(modDirectory))
            return;
        foreach (string dir in Directory.EnumerateDirectories(modDirectory, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
        {
            if (!Directory.EnumerateFileSystemEntries(dir).Any())
                Directory.Delete(dir);
        }
        if (!Directory.EnumerateFileSystemEntries(modDirectory).Any())
            Directory.Delete(modDirectory);
    }
}
