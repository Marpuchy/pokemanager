using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Pokemanager.Model.Dump;

namespace Pokemanager.Model.Mods;

/// <summary>
/// What the user wrote about one of their own mods, kept in <c>pokemanager-mod.json</c> inside its folder. Everything is
/// optional: a folder dropped in with nothing but <c>romfs/</c> is a mod named after the folder.
/// </summary>
public sealed class UserModInfo
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Author { get; set; }
    public string? Url { get; set; }

    /// <summary>The games it is for; empty = not said, which is shown in every project and says so.</summary>
    public List<GameTitle> Games { get; set; } = [];

    /// <summary>A picture inside the mod's folder (its file name), for the list.</summary>
    public string? Image { get; set; }
}

/// <summary>
/// The user's own mods (2026-11-11, user: "una carpeta de mods para ponerlos ahí y que si quiere poner un mod externo
/// pueda editar nombre y tal y activarlo y desactivarlo desde la app"): <c>Documents\Pokemanager\Mods</c>, one folder
/// per mod, read as the catalogue's mods are read and turned on and off the same way.
/// </summary>
/// <remarks>
/// <para>What can be dropped in:</para>
/// <list type="bullet">
/// <item>a <b>LayeredFS folder</b> — <c>romfs/</c> and/or <c>exefs/</c>, at its top or deeper (a release often ships
/// <c>luma/titles/&lt;TitleID&gt;/romfs</c>; the title id also says which game it is for);</item>
/// <item>a <b>.zip</b> or <b>.7z</b>: unpacked into a folder of its own name, the archive then kept in
/// <c>_archivos</c> (.7z needs the system's <c>tar</c> or 7-Zip; when neither can, the scan says so);</item>
/// <item>a <b>.3gx</b> plugin: put in a folder of its own under <c>plugin/</c>, so it is installed into the emulator
/// like the catalogue's plugins and never packed into a ROM.</item>
/// </list>
/// <para>Folders whose name starts with <c>_</c> or <c>.</c> are not mods. An id is <c>user:&lt;folder&gt;</c>, so it
/// never meets a catalogue id, and the project stores it like any other.</para>
/// </remarks>
public static class UserMods
{
    public const string IdPrefix = "user:";
    public const string InfoFile = "pokemanager-mod.json";
    public const string ArchivesFolder = "_archivos";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// <c>Documents\Pokemanager\Mods</c>; <c>POKEMANAGER_DOCUMENTS</c> moves it, as it moves the projects and the games
    /// (tests and harnesses never touch the real one). The application may set another.
    /// </summary>
    public static string Root { get; set; } = DefaultRoot;

    public static string DefaultRoot => Path.Combine(
        Environment.GetEnvironmentVariable("POKEMANAGER_DOCUMENTS") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Pokemanager"),
        "Mods");

    private static readonly object Gate = new();
    private static IReadOnlyList<ModDefinition> current = [];
    private static Dictionary<string, string> contentByid = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The mods found by the last <see cref="Scan"/>.</summary>
    public static IReadOnlyList<ModDefinition> Current
    {
        get { lock (Gate) return current; }
    }

    public static bool IsUser(string id) => id.StartsWith(IdPrefix, StringComparison.OrdinalIgnoreCase);

    public static string IdOf(string folderName) => IdPrefix + folderName;

    /// <summary>The mod's folder in <see cref="Root"/> (where its <c>pokemanager-mod.json</c> lives).</summary>
    public static string FolderOf(string id) => Path.Combine(Root, id[IdPrefix.Length..]);

    /// <summary>
    /// Where its <c>romfs/</c>, <c>exefs/</c> and <c>plugin/</c> are: the folder itself, or the one inside it that holds
    /// them. What <see cref="ModLibrary.DirectoryFor"/> answers for a user mod.
    /// </summary>
    public static string ContentDirectory(string id)
    {
        lock (Gate)
            if (contentByid.TryGetValue(id, out string? known))
                return known;
        return FolderOf(id);
    }

    /// <summary>What happened while scanning, for the status line: archives unpacked, plugins put in place, problems.</summary>
    public sealed record ScanReport(IReadOnlyList<string> Added, IReadOnlyList<string> Problems);

    /// <summary>
    /// Reads the folder: unpacks what was dropped in as an archive, gives a loose .3gx its own folder, and describes every
    /// mod in it. The folder is created when it does not exist.
    /// </summary>
    public static ScanReport Scan()
    {
        var added = new List<string>();
        var problems = new List<string>();
        Directory.CreateDirectory(Root);

        foreach (string file in Directory.EnumerateFiles(Root))
        {
            string extension = Path.GetExtension(file).ToLowerInvariant();
            try
            {
                switch (extension)
                {
                    case ".zip" or ".7z":
                        if (Unpack(file) is { } folder)
                        {
                            KeepArchive(file);
                            added.Add(Path.GetFileName(folder));
                        }
                        else
                            problems.Add(string.Format(Resources.Strings.UserMods_CannotUnpack, Path.GetFileName(file)));
                        break;
                    case ".3gx":
                        string pluginFolder = UniqueFolder(Path.GetFileNameWithoutExtension(file));
                        Directory.CreateDirectory(Path.Combine(pluginFolder, "plugin"));
                        File.Move(file, Path.Combine(pluginFolder, "plugin", Path.GetFileName(file)));
                        added.Add(Path.GetFileName(pluginFolder));
                        break;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                problems.Add($"{Path.GetFileName(file)}: {ex.Message}");
            }
        }

        var mods = new List<ModDefinition>();
        var content = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string folder in Directory.EnumerateDirectories(Root).Order(StringComparer.OrdinalIgnoreCase))
        {
            string name = Path.GetFileName(folder);
            if (name.StartsWith('_') || name.StartsWith('.'))
                continue;
            try
            {
                if (Describe(folder) is { } described)
                {
                    mods.Add(described.Mod);
                    content[described.Mod.Id] = described.Content;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                problems.Add($"{name}: {ex.Message}");
            }
        }

        lock (Gate)
        {
            current = mods;
            contentByid = content;
        }
        return new ScanReport(added, problems);
    }

    /// <summary>The user's own description of a mod (empty when there is none yet).</summary>
    public static UserModInfo ReadInfo(string id)
    {
        string path = Path.Combine(FolderOf(id), InfoFile);
        if (!File.Exists(path))
            return new UserModInfo();
        return JsonSerializer.Deserialize<UserModInfo>(File.ReadAllText(path), JsonOptions) ?? new UserModInfo();
    }

    /// <summary>Saves what the user wrote about a mod and describes it again.</summary>
    public static void SaveInfo(string id, UserModInfo info)
    {
        string folder = FolderOf(id);
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, InfoFile);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(info, JsonOptions));
        File.Move(tmp, path, overwrite: true);
        if (Describe(folder) is { } described)
            lock (Gate)
            {
                current = [.. current.Select(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase) ? described.Mod : m)];
                contentByid[id] = described.Content;
            }
    }

    /// <summary>Copies a picture into the mod's folder as its image; returns the file name stored in the info.</summary>
    public static string CopyImage(string id, string source)
    {
        string name = "imagen" + Path.GetExtension(source).ToLowerInvariant();
        File.Copy(source, Path.Combine(FolderOf(id), name), overwrite: true);
        return name;
    }

    /// <summary>The image's full path, or null.</summary>
    public static string? ImagePath(string id, UserModInfo info) =>
        info.Image is { Length: > 0 } image && File.Exists(Path.Combine(FolderOf(id), image)) ? Path.Combine(FolderOf(id), image) : null;

    /// <summary>
    /// Brings a mod the user picked (a folder, a .zip/.7z or a .3gx) into <see cref="Root"/>; the next
    /// <see cref="Scan"/> describes it. Returns the folder it went to, or null when it could not be unpacked.
    /// </summary>
    public static string? Add(string source)
    {
        Directory.CreateDirectory(Root);
        if (Directory.Exists(source))
        {
            string target = UniqueFolder(Path.GetFileName(Path.TrimEndingDirectorySeparator(source)));
            CopyDirectory(source, target);
            return target;
        }
        string copy = Path.Combine(Root, Path.GetFileName(source));
        if (!string.Equals(Path.GetFullPath(source), Path.GetFullPath(copy), StringComparison.OrdinalIgnoreCase))
            File.Copy(source, copy, overwrite: true);
        return copy;
    }

    // ------------------------------------------------------------------ describing a folder

    private static (ModDefinition Mod, string Content)? Describe(string folder)
    {
        string id = IdOf(Path.GetFileName(folder));
        string content = FindContent(folder);
        bool hasGame = Directory.Exists(Path.Combine(content, "romfs")) || Directory.Exists(Path.Combine(content, "exefs"));
        string? plugin = FindPlugin(content, folder);
        if (!hasGame && plugin is null)
            return null;   // nothing a game or an emulator would read: not a mod (yet)

        var info = ReadInfo(id);
        var games = info.Games.Count > 0 ? info.Games : GuessGames(content, folder);
        bool gamesUnknown = games.Count == 0;
        var definition = new ModDefinition(
            id,
            string.IsNullOrWhiteSpace(info.Name) ? Path.GetFileName(folder) : info.Name.Trim(),
            info.Author ?? "",
            info.Description ?? "",
            "",
            info.Url ?? "",
            [],
            gamesUnknown ? [.. Enum.GetValues<GameTitle>()] : games,
            plugin is null || hasGame ? [] : [new ModFile(ModFileArea.Plugin, plugin) { Required = true }])
        {
            Kind = hasGame ? ModKind.RomFiles : ModKind.EmulatorPlugin,
            Summary = string.IsNullOrWhiteSpace(info.Description)
                ? (gamesUnknown ? Resources.Strings.UserMods_NoDescriptionNoGame : Resources.Strings.UserMods_NoDescription)
                : info.Description.Split('\n')[0].Trim(),
            IsUserMod = true,
            GamesUnknown = gamesUnknown,
            SetupNote = hasGame ? null : Resources.Strings.UserMods_PluginNote,
        };
        return (definition, content);
    }

    /// <summary>The folder holding <c>romfs/</c>/<c>exefs/</c>: the mod's own, or the first one inside it (3 levels).</summary>
    private static string FindContent(string folder)
    {
        static bool Holds(string dir) => Directory.Exists(Path.Combine(dir, "romfs")) || Directory.Exists(Path.Combine(dir, "exefs"));
        if (Holds(folder))
            return folder;
        var queue = new Queue<(string Dir, int Depth)>([(folder, 0)]);
        while (queue.Count > 0)
        {
            var (dir, depth) = queue.Dequeue();
            foreach (string sub in Directory.EnumerateDirectories(dir).Order(StringComparer.OrdinalIgnoreCase))
            {
                if (Holds(sub))
                    return sub;
                if (depth < 3)
                    queue.Enqueue((sub, depth + 1));
            }
        }
        return folder;
    }

    /// <summary>
    /// A plugin's file name: one already in <c>plugin/</c>, or a loose .3gx anywhere in the folder, which is moved there
    /// so the library reads it as the catalogue's plugins are read.
    /// </summary>
    private static string? FindPlugin(string content, string folder)
    {
        string pluginDir = Path.Combine(content, "plugin");
        if (Directory.Exists(pluginDir) && Directory.EnumerateFiles(pluginDir, "*.3gx").FirstOrDefault() is { } inPlace)
            return Path.GetFileName(inPlace);
        if (Directory.EnumerateFiles(folder, "*.3gx", SearchOption.AllDirectories).FirstOrDefault() is not { } loose)
            return null;
        Directory.CreateDirectory(pluginDir);
        File.Move(loose, Path.Combine(pluginDir, Path.GetFileName(loose)));
        return Path.GetFileName(loose);
    }

    /// <summary>The game a 16-hex title id folder on the way to the content names (<c>00040000001B5100</c> = Ultra Moon).</summary>
    private static List<GameTitle> GuessGames(string content, string folder)
    {
        var found = new List<GameTitle>();
        foreach (string dir in Directory.EnumerateDirectories(folder, "*", SearchOption.AllDirectories).Append(content))
        {
            string name = Path.GetFileName(dir);
            if (name.Length == 16 && ulong.TryParse(name, System.Globalization.NumberStyles.HexNumber, null, out ulong titleId)
                && GameTitleExtensions.FromTitleId(titleId) is { } game && !found.Contains(game))
                found.Add(game);
        }
        return found;
    }

    // ------------------------------------------------------------------ archives

    /// <summary>Unpacks an archive into a folder of its name; null when nothing on this computer can.</summary>
    private static string? Unpack(string archive)
    {
        string folder = UniqueFolder(Path.GetFileNameWithoutExtension(archive));
        try
        {
            if (Path.GetExtension(archive).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                ZipFile.ExtractToDirectory(archive, folder);
                return folder;
            }
            Directory.CreateDirectory(folder);
            foreach (var (program, arguments) in SevenZipTools(archive, folder))
                if (Run(program, arguments) && Directory.EnumerateFileSystemEntries(folder).Any())
                    return folder;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
        }
        if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
            Directory.Delete(folder);
        return null;
    }

    /// <summary>What can open a .7z: 7-Zip where it installs itself, then the system's <c>tar</c> (libarchive).</summary>
    private static IEnumerable<(string Program, string Arguments)> SevenZipTools(string archive, string folder)
    {
        foreach (string root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) }.Where(r => r.Length > 0))
        {
            string sevenZip = Path.Combine(root, "7-Zip", "7z.exe");
            if (File.Exists(sevenZip))
                yield return (sevenZip, $"x -y \"-o{folder}\" \"{archive}\"");
        }
        yield return ("7z", $"x -y \"-o{folder}\" \"{archive}\"");
        yield return ("tar", $"-xf \"{archive}\" -C \"{folder}\"");
    }

    private static bool Run(string program, string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(program, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            if (process is null)
                return false;
            process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            return process.WaitForExit(120_000) && process.ExitCode == 0;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;   // not installed
        }
    }

    /// <summary>The archive goes to <c>_archivos</c> once unpacked, so it is not unpacked again.</summary>
    private static void KeepArchive(string archive)
    {
        string kept = Path.Combine(Root, ArchivesFolder);
        Directory.CreateDirectory(kept);
        string target = Path.Combine(kept, Path.GetFileName(archive));
        File.Move(archive, target, overwrite: true);
    }

    private static string UniqueFolder(string name)
    {
        foreach (char bad in Path.GetInvalidFileNameChars())
            name = name.Replace(bad, '_');
        name = name.Trim().TrimStart('_', '.');
        if (name.Length == 0)
            name = "mod";
        string folder = Path.Combine(Root, name);
        for (int n = 2; Directory.Exists(folder) || File.Exists(folder); n++)
            folder = Path.Combine(Root, $"{name} ({n})");
        return folder;
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (string dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, dir)));
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)), overwrite: true);
    }
}
