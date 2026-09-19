using Pokemanager.Randomizer.Resources;

namespace Pokemanager.Randomizer;

public sealed record RomBuildResult(string RomPath, string? LogPath, int FilesChanged);

/// <summary>
/// Builds the final ROM as a new file (.cxi): base ROM + randomizer output + manual edits. The base ROM is never
/// modified.
/// </summary>
public static class RomBuilder
{
    /// <summary>Extension of the built ROM. UPR ZX can only write 3DS games as NCCH (.cxi).</summary>
    public const string Extension = ".cxi";

    /// <summary>Empty name or invalid characters. Returns the error, or null.</summary>
    public static string? ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Strings.Rom_NameEmpty;
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return Strings.Rom_NameInvalid;
        return null;
    }

    /// <summary><c>&lt;base ROM folder&gt;/&lt;name&gt;.cxi</c>.</summary>
    public static string OutputPath(string baseRom, string name) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(baseRom))!, name.Trim() + Extension);

    /// <param name="random">UPR ZX output, or null to build the ROM with the edits only.</param>
    /// <param name="edits">Relative paths (<c>romfs/a/2/1/8</c>) → contents; they override the randomizer files.</param>
    /// <param name="workRoot">Working folder where the files are staged before packing.</param>
    public static async Task<RomBuildResult> BuildAsync(
        UprRunner runner, string baseRom, UprResult? random, IReadOnlyDictionary<string, byte[]> edits,
        string outputRom, long seed, string workRoot,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        outputRom = Path.GetFullPath(outputRom);
        if (string.Equals(outputRom, Path.GetFullPath(baseRom), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(Strings.Rom_CannotReplaceBase);

        string work = Path.Combine(workRoot, "staging-" + Guid.NewGuid().ToString("N"));
        string title = Path.Combine(work, "title");
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
                // romfs files, and the executable (the shop table lives in it).
                if (!relative.StartsWith("romfs/", StringComparison.Ordinal) && relative != "code.bin")
                    throw new InvalidOperationException(string.Format(Strings.Rom_OnlyRomFs, relative));
                string target = Path.Combine(title, relative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                bool replaced = File.Exists(target);
                await File.WriteAllBytesAsync(target, bytes, cancellationToken);
                if (!replaced)
                    files++;
            }
            Directory.CreateDirectory(title);

            // Written under another name and renamed at the end: a half-written ROM never has the final name.
            string partial = outputRom + ".partial";
            if (File.Exists(partial))
                File.Delete(partial);
            await runner.PackAsync(baseRom, title, partial, seed, progress, cancellationToken);
            // UPR ZX writes the region sizes one byte at a time: a RomFS that shrank would not load (see NcchHeader).
            NcchHeader.Repair(partial);
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
