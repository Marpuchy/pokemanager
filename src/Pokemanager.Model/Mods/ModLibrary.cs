using System.Security.Cryptography;
using Pokemanager.Model.Projects;
using Pokemanager.Model.Resources;

namespace Pokemanager.Model.Mods;

/// <summary>What the library holds for one file of a mod.</summary>
public enum ModFileState
{
    /// <summary>The library does not have it.</summary>
    Missing,

    /// <summary>It is there and its hash is the one the catalogue says.</summary>
    Verified,

    /// <summary>It is there and its hash is <b>not</b> the one the catalogue says: it is not the file it claims to be.</summary>
    WrongHash,

    /// <summary>It is there, and the catalogue has no hash for it, so nothing about it can be confirmed.</summary>
    HashUnknown,
}

/// <summary>One file of a mod as the library has it.</summary>
/// <param name="ServedPath">Path inside the library and the mod (<c>romfs/a/0/1/7</c>, <c>exefs/code.bin</c>).</param>
/// <param name="TitlePath">Path the ROM packer uses (<c>romfs/a/0/1/7</c>, <c>code.bin</c>).</param>
/// <param name="LocalPath">The file on disk, or null when it is missing.</param>
/// <param name="Sha256">Hash of the file on disk, upper-case hex; null when it is missing.</param>
public sealed record ModFileStatus(
    string ServedPath,
    string TitlePath,
    string? LocalPath,
    ModFileState State,
    string? Sha256,
    long Bytes)
{
    /// <summary>What the catalogue says about this file, or null for a file the catalogue does not name.</summary>
    public ModFile? Declared { get; init; }

    /// <summary>Whether this file is written into the packed ROM.</summary>
    public bool IntoRom { get; init; } = true;

    public bool Present => State != ModFileState.Missing;

    /// <summary>The mod does nothing without it.</summary>
    public bool Required => Declared?.Required ?? false;

    /// <summary>It is distributed apart from the rest of the mod, so the user has to fetch it separately.</summary>
    public bool Apart => Declared?.Apart ?? false;

    /// <summary>
    /// It is a plugin the emulator loads, kept under <c>plugin/</c> in the library and copied into the emulator's own
    /// folder by <see cref="ModPluginInstaller"/>. Never packed into a ROM.
    /// </summary>
    public bool IsPlugin => ServedPath.StartsWith(ModFile.PluginPrefix, StringComparison.OrdinalIgnoreCase);
}

/// <summary>A file of a mod that the build puts into the ROM.</summary>
/// <param name="TitlePath">Where it is served inside the title: <c>romfs/a/0/1/7</c> or <c>code.bin</c>.</param>
/// <param name="LocalPath">The file on disk in the user's library.</param>
/// <param name="Sha256">Its contents, as a hash: what the cache key of the modded base ROM is made of.</param>
public sealed record ModContent(string TitlePath, string LocalPath, string Sha256);

/// <summary>What the user's library holds for one mod, checked against the catalogue.</summary>
public sealed record ModInstallation(ModDefinition Mod, string Directory, IReadOnlyList<ModFileStatus> Files)
{
    public IEnumerable<ModFileStatus> Present => Files.Where(f => f.Present);

    /// <summary>
    /// Files that go into the ROM, in a stable order (the cache key depends on it being stable). <b>Empty for a
    /// plugin mod</b>: it puts nothing in the ROM, so there is no base to pack and nothing for the cache key to be
    /// made of, whatever the files happen to be called.
    /// </summary>
    public IReadOnlyList<ModContent> Contents => Mod.IsPlugin ? [] :
    [
        .. Present.Where(f => f.IntoRom)
            .OrderBy(f => f.TitlePath, StringComparer.Ordinal)
            .Select(f => new ModContent(f.TitlePath, f.LocalPath!, f.Sha256!)),
    ];

    /// <summary>Files the mod cannot work without and the library does not have.</summary>
    public IEnumerable<ModFileStatus> MissingRequired => Files.Where(f => f.Required && !f.Present);

    /// <summary>Files that are there but are not what the catalogue says they are.</summary>
    public IEnumerable<ModFileStatus> Wrong => Files.Where(f => f.State == ModFileState.WrongHash);

    /// <summary>Files the catalogue names, that are not there and are not distributed apart.</summary>
    public IEnumerable<ModFileStatus> MissingExpected => Files.Where(f => f.Declared is not null && !f.Present && !f.Apart);

    /// <summary>Nothing of this mod is in the library yet.</summary>
    public bool IsEmpty => !Present.Any();

    /// <summary>
    /// Every required file is there and nothing present is the wrong file. For a ROM mod that is the build being able
    /// to go on; for a plugin mod it is the plugin being ready to install, which is why
    /// <see cref="CanInstallPlugin"/> says the same thing in the words of the thing it does.
    /// </summary>
    public bool CanBuild => !IsEmpty && !MissingRequired.Any() && !Wrong.Any();

    /// <summary>
    /// The plugin file the library holds for this mod, or null when it has not been fetched yet (or this is not a
    /// plugin mod at all). It is what <see cref="ModPluginInstaller"/> copies into the emulator.
    /// </summary>
    public ModFileStatus? Plugin => Mod.IsPlugin ? Files.FirstOrDefault(f => f.IsPlugin && f.Present) : null;

    /// <summary>The library holds the plugin, so it can be copied into the emulator's folder.</summary>
    public bool CanInstallPlugin => Mod.IsPlugin && Plugin is not null && !Wrong.Any();

    /// <summary>
    /// The files really are the release the catalogue describes. Only true when the catalogue carries hashes and every
    /// one it carries matched: with no hashes known this is false and <see cref="VersionText"/> says so, rather than
    /// claiming a version nothing was checked against.
    /// </summary>
    public bool ConfirmsVersion =>
        Mod.HasKnownHashes
        && !Wrong.Any()
        && Files.Where(f => f.Declared is { HashKnown: true } && f.Present).All(f => f.State == ModFileState.Verified)
        && Files.Any(f => f.State == ModFileState.Verified);

    /// <summary>Whether a file is there whose hash the catalogue does not know.</summary>
    public bool HasUnconfirmedFiles => Files.Any(f => f.State == ModFileState.HashUnknown);

    /// <summary>What stops this mod from being built, or null when nothing does.</summary>
    public string? Problem
    {
        get
        {
            if (IsEmpty)
                return string.Format(Strings.Mods_NothingProvided, Mod.Name, Directory);
            if (MissingRequired.FirstOrDefault() is { } missing)
                return string.Format(Strings.Mods_RequiredMissing, Mod.Name, missing.ServedPath, Directory);
            if (Wrong.FirstOrDefault() is { } wrong)
                return string.Format(Strings.Mods_FileWrong, Mod.Name, wrong.ServedPath);
            return null;
        }
    }

    /// <summary>What can honestly be said about which release these files are.</summary>
    public string VersionText => ConfirmsVersion
        ? string.Format(Strings.Mods_VersionConfirmed, Mod.Version)
        : string.Format(Strings.Mods_VersionUnknown, Mod.Version);
}

/// <summary>
/// The user's own copies of the mod files, one folder per mod under <see cref="Root"/>, laid out as the game is:
/// <c>&lt;root&gt;/&lt;mod id&gt;/romfs/a/0/1/7</c>, <c>&lt;root&gt;/&lt;mod id&gt;/exefs/code.bin</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The files are the user's own.</b> Pokemanager ships none of them: this class only reads what is in the folder,
/// checks it by hash against the catalogue and says what it found. They get there by hand or, since 2026-11, through
/// <see cref="ModDownloader"/>, which fetches from the author's own source into this same folder and nowhere else. The
/// folder is the same shape as the mod's own release and as the emulator's LayeredFS, so a release can be unzipped
/// straight into it — which is also what lets a downloaded archive be unpacked into it unchanged.
/// </para>
/// <para>
/// It follows <c>AppSettings.DataRoot</c>, like the randomization cache and the save backups: regenerable by the user,
/// never inside the project (a project is shareable and weighs kilobytes), and moved by <c>POKEMANAGER_DATA</c> so
/// tests and harnesses never touch the real one.
/// </para>
/// </remarks>
public sealed class ModLibrary(string root)
{
    /// <summary><c>&lt;data&gt;/mods</c>. <c>POKEMANAGER_DATA</c> moves it.</summary>
    public static string DefaultRoot => Path.Combine(
        Environment.GetEnvironmentVariable("POKEMANAGER_DATA") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pokemanager"),
        "mods");

    /// <summary>
    /// The library in the usual place. A property and not a cached instance, so a harness that sets
    /// <c>POKEMANAGER_DATA</c> after start is still obeyed.
    /// </summary>
    public static ModLibrary Default => new(DefaultRoot);

    public string Root { get; } = Path.GetFullPath(root);

    public string DirectoryFor(string modId) => Path.Combine(Root, modId);

    public string RomFsRoot(string modId) => Path.Combine(DirectoryFor(modId), "romfs");

    public string ExeFsRoot(string modId) => Path.Combine(DirectoryFor(modId), "exefs");

    /// <summary>
    /// Where a served path lives on disk for that mod: <c>&lt;root&gt;/&lt;id&gt;/romfs/a/0/9/4</c>. The one place that
    /// turns a served path into a file, so <see cref="ModDownloader"/> puts a download exactly where
    /// <see cref="Inspect"/> looks for it instead of keeping a second idea of the layout.
    /// </summary>
    public string PathFor(string modId, string servedPath) => LocalPath(DirectoryFor(modId), servedPath);

    /// <summary>
    /// The romfs folders of the project's mods, highest priority first, leaving out the ones that are not there. The
    /// read path stacks these over the dump (see <see cref="Projects.Project.ReadLayers"/>); an empty list means the
    /// application reads exactly what it read before mods existed.
    /// </summary>
    public IReadOnlyList<string> RomFsRoots(ModSettings mods)
    {
        if (mods.IsEmpty)
            return [];
        // Only the mods that bring files to the game: a plugin mod has no romfs at all, and a folder of its own here
        // would make the application read a layer that is not part of the ROM it builds.
        return [.. mods.EnabledRomMods.Select(RomFsRoot).Where(path => System.IO.Directory.Exists(path))];
    }

    /// <summary>
    /// The executable of the highest-priority mod that has one, or null. The mod's <c>code.bin</c> is what reads its
    /// expanded tables, so the build has to start from it when there is no randomization to have done it already.
    /// </summary>
    public string? CodePath(ModSettings mods)
    {
        if (mods.IsEmpty)
            return null;
        return mods.EnabledRomMods.Select(id => Path.Combine(ExeFsRoot(id), "code.bin")).FirstOrDefault(path => File.Exists(path));
    }

    /// <summary>
    /// Everything the library holds for a mod, hashed and checked against the catalogue: the files the catalogue names
    /// (missing ones included) and any other file the user provided, which is packed all the same — a mod that replaces
    /// whole archives can ship more than the catalogue lists.
    /// </summary>
    public ModInstallation Inspect(ModDefinition mod)
    {
        string directory = DirectoryFor(mod.Id);
        var statuses = new List<ModFileStatus>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var declared in mod.Files)
        {
            string local = LocalPath(directory, declared.ServedPath);
            seen.Add(declared.ServedPath);
            statuses.Add(Status(declared.ServedPath, declared.TitlePath, local, declared, declared.IntoRom));
        }

        foreach (string served in Provided(directory).Where(p => !seen.Contains(p)).Order(StringComparer.Ordinal))
        {
            statuses.Add(Status(served, TitlePathOf(served), LocalPath(directory, served), declared: null,
                intoRom: IsPackable(served)));
        }

        return new ModInstallation(mod, directory, statuses);
    }

    /// <summary>
    /// Copies a mod release from a folder the user picked into the library, keeping the paths inside it. Only the two
    /// halves of a game and the plugin folder are taken (<c>romfs/</c>, <c>exefs/</c>, <c>plugin/</c>), so a release's
    /// readme and licence are left behind. Nothing is fetched from anywhere: the folder is already on the user's disk.
    /// </summary>
    /// <returns>The served paths that were copied.</returns>
    public IReadOnlyList<string> ImportFrom(ModDefinition mod, string sourceDirectory)
    {
        sourceDirectory = Path.GetFullPath(sourceDirectory);
        string directory = DirectoryFor(mod.Id);
        var copied = new List<string>();
        foreach (string served in Provided(sourceDirectory).Order(StringComparer.Ordinal))
        {
            string target = LocalPath(directory, served);
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(LocalPath(sourceDirectory, served), target, overwrite: true);
            copied.Add(served);
        }
        return copied;
    }

    /// <summary>Adds one file the user provides (the model archive comes apart from the rest) under a served path.</summary>
    public void ImportFile(ModDefinition mod, string sourceFile, string servedPath)
    {
        string target = LocalPath(DirectoryFor(mod.Id), servedPath);
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(Path.GetFullPath(sourceFile), target, overwrite: true);
    }

    /// <summary>
    /// The served paths a folder laid out like a game holds, under <c>romfs/</c> and <c>exefs/</c> — plus
    /// <c>plugin/</c>, which is not part of the game but of the emulator and is kept here all the same, so one
    /// library and one <see cref="Inspect"/> answer for both classes of mod.
    /// </summary>
    private static IEnumerable<string> Provided(string directory)
    {
        foreach (string half in new[] { "romfs", "exefs", "plugin" })
        {
            string root = Path.Combine(directory, half);
            if (!System.IO.Directory.Exists(root))
                continue;
            foreach (string file in System.IO.Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                yield return half + "/" + Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
        }
    }

    private static string LocalPath(string directory, string servedPath) =>
        Path.Combine(directory, servedPath.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>Served path → the path the ROM packer uses: the executable is the bare <c>code.bin</c>.</summary>
    private static string TitlePathOf(string servedPath) =>
        servedPath.StartsWith("exefs/", StringComparison.OrdinalIgnoreCase) ? servedPath["exefs/".Length..] : servedPath;

    /// <summary>
    /// Whether the ROM packer can write that file: romfs files and the executable, which is all
    /// <c>PokemanagerUpr.pack</c> knows (<c>NCCH.writeFile</c> and <c>NCCH.writeCode</c>). Anything else in the exefs —
    /// <c>icon.bin</c>, <c>banner.bin</c> — is kept and checked but not carried into the ROM.
    /// </summary>
    /// <remarks>
    /// A <c>plugin/</c> file is never packable: it is not part of the game, it is a file the emulator loads.
    /// </remarks>
    private static bool IsPackable(string servedPath) =>
        servedPath.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase)
        || string.Equals(servedPath, "exefs/code.bin", StringComparison.OrdinalIgnoreCase);

    private static ModFileStatus Status(string served, string titlePath, string local, ModFile? declared, bool intoRom)
    {
        if (!File.Exists(local))
            return new ModFileStatus(served, titlePath, null, ModFileState.Missing, null, 0) { Declared = declared, IntoRom = intoRom };

        string hash = Hash(local);
        long bytes = new FileInfo(local).Length;
        var state = declared?.Sha256 is { } expected
            ? string.Equals(hash, expected, StringComparison.OrdinalIgnoreCase) ? ModFileState.Verified : ModFileState.WrongHash
            : ModFileState.HashUnknown;
        return new ModFileStatus(served, titlePath, local, state, hash, bytes) { Declared = declared, IntoRom = intoRom };
    }

    /// <summary>SHA-256 of a file, upper-case hex: how a file the user provides is told apart from one that lies.</summary>
    /// <remarks>
    /// Remembered per (path, size, last write time) for the life of the process: the model archive is 2.3 GB, and
    /// hashing it again every time the Mods tab is shown or a ROM is built kept the tab on "reading" for many seconds.
    /// A file that is replaced gets a new size or time and is hashed again.
    /// </remarks>
    public static string Hash(string path)
    {
        var info = new FileInfo(path);
        var key = (Path.GetFullPath(path), info.Length, info.LastWriteTimeUtc);
        if (HashCache.TryGetValue(key, out string? known))
            return known;
        string hash;
        using (var stream = File.OpenRead(path))
            hash = Convert.ToHexString(SHA256.HashData(stream));
        HashCache[key] = hash;
        return hash;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(string, long, DateTime), string> HashCache =
        new();
}
