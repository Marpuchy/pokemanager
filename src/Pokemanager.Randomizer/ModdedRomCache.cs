using System.Security.Cryptography;
using System.Text;
using Pokemanager.Randomizer.Resources;

namespace Pokemanager.Randomizer;

/// <summary>
/// A file a mod puts into the base ROM.
/// </summary>
/// <param name="TitlePath">Where the packer serves it: <c>romfs/a/0/1/7</c>, or <c>code.bin</c> for the executable.</param>
/// <param name="SourcePath">The file on disk, in the user's own mod library.</param>
/// <param name="Sha256">Its contents as a hash: this, and not its date, is what the cache key is made of.</param>
public sealed record ModdedRomFile(string TitlePath, string SourcePath, string Sha256);

/// <summary>
/// The base ROM with the project's mods packed into it, cached per (base ROM, mod files).
/// </summary>
/// <remarks>
/// <para>
/// A mod of this kind replaces whole files instead of editing fields, so it and the randomizer cannot both own the
/// game's tables: applied after randomizing it wipes the randomization, and applied to the dump alone it leaves UPR
/// randomizing the species the game shipped with and ignoring the new ones. So the pipeline gains a stage
/// <b>before</b> UPR — <c>dump → mods → base ROM with the mod, packed and cached → randomization → difficulty →
/// edits → build</c> — and what UPR is handed is that packed ROM, where it sees every species the mod added.
/// </para>
/// <para>
/// It is worth caching because packing a 3DS ROM takes tens of seconds and gigabytes, and the modded base only changes
/// when the mod or the ROM does: a new seed reuses it. The entries are whole ROMs, so <see cref="Prune"/> keeps very
/// few.
/// </para>
/// <para>
/// <b>One place works out the key</b> (<see cref="Key"/>), used by the lookup and by what writes the entry. The
/// randomization cache was once keyed differently on the two paths and never found its own output again; here the
/// lookup and the write cannot drift because there is only one method, and the material is content hashes, which the
/// library has already computed while checking the files.
/// </para>
/// </remarks>
public sealed class ModdedRomCache(string root)
{
    private const string CompleteMarker = ".complete";
    private const string RomName = "base";

    /// <summary><c>&lt;data&gt;/modded-roms</c>. <c>POKEMANAGER_DATA</c> moves it.</summary>
    public static string DefaultRoot => Path.Combine(
        Environment.GetEnvironmentVariable("POKEMANAGER_DATA") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pokemanager"),
        "modded-roms");

    public string Root { get; } = Path.GetFullPath(root);

    /// <summary>
    /// Stable key: changes when the base ROM (path, size, date) or any mod file (served path or contents) changes, and
    /// with nothing else — above all not with the seed, which is the point of caching this at all. The files are sorted
    /// by served path, so the order they were listed in cannot change the key.
    /// </summary>
    public static string Key(string baseRom, IEnumerable<ModdedRomFile> files)
    {
        var rom = new FileInfo(baseRom);
        var material = new StringBuilder();
        material.Append(Path.GetFullPath(baseRom).ToUpperInvariant()).Append('|')
            .Append(rom.Length).Append('|').Append(rom.LastWriteTimeUtc.Ticks).Append('|');
        foreach (var file in files.OrderBy(f => f.TitlePath, StringComparer.Ordinal))
            material.Append(file.TitlePath).Append('=').Append(file.Sha256.ToUpperInvariant()).Append(';');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material.ToString())))[..24];
    }

    /// <summary>The folder an entry lives in. Public so a caller can leave it out of a prune.</summary>
    public string DirectoryFor(string baseRom, IEnumerable<ModdedRomFile> files) =>
        Path.Combine(Root, Key(baseRom, files));

    /// <summary>The ROM already packed for those files, or null when it is not cached.</summary>
    public string? TryGet(string baseRom, IReadOnlyList<ModdedRomFile> files)
    {
        string dir = DirectoryFor(baseRom, files);
        string rom = Path.Combine(dir, RomName + RomBuilder.Extension);
        return File.Exists(Path.Combine(dir, CompleteMarker)) && File.Exists(rom) ? rom : null;
    }

    /// <summary>
    /// The modded base ROM: the cached one, or packed now. The counterpart of <see cref="TryGet"/>, and both ask
    /// <see cref="Key"/>, so what this writes is what that finds.
    /// </summary>
    /// <param name="files">
    /// Everything the mods put into the ROM. <b>Empty is not a case this handles</b>: with no mods the caller keeps
    /// using the plain base ROM, so the build of a project without mods is byte for byte what it was.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="files"/> is empty.</exception>
    public async Task<string> GetOrCreateAsync(
        UprRunner runner, string baseRom, IReadOnlyList<ModdedRomFile> files,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (files.Count == 0)
            throw new ArgumentException(Strings.Mods_NoFiles, nameof(files));

        string dir = DirectoryFor(baseRom, files);
        string rom = Path.Combine(dir, RomName + RomBuilder.Extension);
        string marker = Path.Combine(dir, CompleteMarker);

        if (File.Exists(marker) && File.Exists(rom))
        {
            progress?.Report(Strings.Mods_UsingCachedBase);
            // Mark it as used now so Prune keeps it.
            File.SetLastWriteTimeUtc(marker, DateTime.UtcNow);
            return rom;
        }

        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true); // incomplete: this cache created it, so it is redone
        Directory.CreateDirectory(dir);

        progress?.Report(string.Format(Strings.Mods_PackingBase, files.Count));
        // Staged by copying and not read into memory: a mod file can be hundreds of megabytes (the model archive is
        // distributed apart precisely because of its size).
        string title = Path.Combine(dir, "title");
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string target = Path.Combine(title, file.TitlePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file.SourcePath, target, overwrite: true);
        }

        // Packed and repaired the way RomBuilder does it: UPR ZX's NCCH writer leaves the region sizes wrong, and a
        // mod changes the size of the RomFS by definition, which is exactly the case that stops the game from loading.
        string partial = rom + ".partial";
        if (File.Exists(partial))
            File.Delete(partial);
        await runner.PackAsync(baseRom, title, partial, 0, progress, cancellationToken);
        NcchHeader.Repair(partial);
        File.Move(partial, rom, overwrite: true);
        Directory.Delete(title, recursive: true);

        await File.WriteAllTextAsync(marker, DateTime.UtcNow.ToString("O"), cancellationToken);
        return rom;
    }

    /// <summary>
    /// Deletes cached modded ROMs except the <paramref name="keep"/> most recently used and the given one. These
    /// entries are whole ROMs of some gigabytes, so the caller keeps far fewer of them than of randomizations.
    /// </summary>
    public void Prune(int keep, string? keepRom = null)
    {
        if (!Directory.Exists(Root))
            return;
        string? keepDirectory = keepRom is null ? null : Path.GetDirectoryName(Path.GetFullPath(keepRom));
        var entries = Directory.GetDirectories(Root)
            .Where(d => File.Exists(Path.Combine(d, CompleteMarker)))
            .OrderByDescending(d => File.GetLastWriteTimeUtc(Path.Combine(d, CompleteMarker)))
            .ToList();
        foreach (string dir in entries.Skip(keep))
        {
            if (keepDirectory is not null && string.Equals(Path.GetFullPath(dir), keepDirectory, StringComparison.OrdinalIgnoreCase))
                continue;
            try { Directory.Delete(dir, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
