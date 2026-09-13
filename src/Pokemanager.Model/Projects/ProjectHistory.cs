using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pokemanager.Model.Projects;

/// <summary>Why a version was recorded.</summary>
public enum VersionKind
{
    /// <summary>A ROM was built with this configuration.</summary>
    Built,

    /// <summary>The user saved it by hand.</summary>
    Manual,

    /// <summary>Automatic, right before restoring another version.</summary>
    BeforeRestore,

    /// <summary>Automatic, right before importing Pokémon data.</summary>
    BeforeImport,

    /// <summary>Automatic, right before writing changes made in the save editor.</summary>
    BeforeSaveEdit,
}

/// <summary>A recorded version of the project: what is needed to rebuild the same ROM, plus the save at that moment.</summary>
public sealed record ProjectVersion(
    string Id,
    DateTime CreatedAt,
    VersionKind Kind,
    string? Note,
    bool Randomized,
    long Seed,
    string? PresetName,
    bool PresetModified,
    int EditCount,
    string? RomPath,
    bool HasSave,
    string Fingerprint);

/// <summary>What differs between a version and the current project.</summary>
public sealed record VersionDifference(bool Randomization, bool Seed, bool Preset, int EditsAdded, int EditsRemoved, int EditsChanged)
{
    public bool Any => Randomization || Seed || Preset || EditsAdded + EditsRemoved + EditsChanged > 0;
}

/// <summary>
/// Version history of a project, stored next to it in <c>&lt;project&gt;.history/</c>. ROMs are not stored: the same
/// configuration rebuilds the same ROM byte for byte. Each version keeps the project snapshot and, when there was one,
/// a copy of the save (a few hundred KB).
/// </summary>
public sealed class ProjectHistory
{
    public const int DefaultKeep = 40;
    private const string ProjectFileName = "project.json";
    private const string InfoFileName = "version.json";
    private const string SaveFileName = "main";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public string Root { get; }

    public ProjectHistory(string projectPath)
    {
        Root = DirectoryFor(projectPath);
    }

    public static string DirectoryFor(string projectPath)
    {
        string full = Path.GetFullPath(projectPath);
        return Path.Combine(Path.GetDirectoryName(full)!, Path.GetFileNameWithoutExtension(full) + ".history");
    }

    /// <summary>Versions, newest first.</summary>
    public IReadOnlyList<ProjectVersion> List()
    {
        if (!Directory.Exists(Root))
            return [];
        var versions = new List<ProjectVersion>();
        foreach (string dir in Directory.GetDirectories(Root))
        {
            try
            {
                string info = Path.Combine(dir, InfoFileName);
                if (File.Exists(info) && File.Exists(Path.Combine(dir, ProjectFileName))
                    && JsonSerializer.Deserialize<ProjectVersion>(File.ReadAllText(info), JsonOptions) is { } v)
                    versions.Add(v with { Id = Path.GetFileName(dir) });
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                // A damaged version is left out of the list, not fatal.
            }
        }
        return versions.OrderByDescending(v => v.CreatedAt).ThenByDescending(v => v.Id, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Records the project as a new version. When the newest version has the same configuration and kind, it is replaced
    /// instead of stacking identical entries (for example, rebuilding the same ROM twice).
    /// </summary>
    /// <param name="savePath">Save to copy with the version, if it exists.</param>
    public ProjectVersion Record(Project project, VersionKind kind, string? note = null, string? romPath = null, string? savePath = null)
    {
        string fingerprint = Fingerprint(project);
        if (List().FirstOrDefault() is { } newest && newest.Fingerprint == fingerprint && newest.Kind == kind && newest.Note == note)
            Delete(newest);

        DateTime now = DateTime.Now;
        string id = now.ToString("yyyyMMdd-HHmmss-fff");
        string dir = Path.Combine(Root, id);
        for (int n = 1; Directory.Exists(dir); n++)
            dir = Path.Combine(Root, $"{id}-{n}");
        string staging = dir + ".partial";
        Directory.CreateDirectory(staging);

        project.Save(Path.Combine(staging, ProjectFileName));
        bool hasSave = savePath is not null && File.Exists(savePath);
        if (hasSave)
            File.Copy(savePath!, Path.Combine(staging, SaveFileName));

        var r = project.Randomization;
        var version = new ProjectVersion(Path.GetFileName(dir), now, kind, string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            r.Enabled, r.Seed, r.PresetName, r.PresetModified, project.Edits.Count, romPath, hasSave, fingerprint);
        File.WriteAllText(Path.Combine(staging, InfoFileName), JsonSerializer.Serialize(version, JsonOptions));
        Directory.Move(staging, dir);
        return version;
    }

    public Project LoadProject(ProjectVersion version) => Project.Load(Path.Combine(Root, version.Id, ProjectFileName));

    /// <summary>Copy of the save stored with the version, or null.</summary>
    public string? SaveFile(ProjectVersion version)
    {
        string path = Path.Combine(Root, version.Id, SaveFileName);
        return File.Exists(path) ? path : null;
    }

    public void Delete(ProjectVersion version)
    {
        string dir = Path.Combine(Root, version.Id);
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
    }

    /// <summary>Keeps the <paramref name="keep"/> newest automatic versions; manual versions are never pruned.</summary>
    public void Prune(int keep = DefaultKeep)
    {
        foreach (var old in List().Where(v => v.Kind != VersionKind.Manual).Skip(keep))
        {
            try { Delete(old); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// Puts a version's configuration (randomization and edits) into <paramref name="target"/>. Paths, the output ROM
    /// and the other preferences of the current project are kept.
    /// </summary>
    public static void RestoreInto(Project target, Project version)
    {
        var from = version.Randomization;
        var to = target.Randomization;
        to.Enabled = from.Enabled;
        to.Preset = from.Preset;
        to.PresetName = from.PresetName;
        to.PresetModified = from.PresetModified;
        to.Seed = from.Seed;

        target.Edits.Clear();
        foreach (var edit in version.Edits.All)
            target.Edits.Set(edit.Table, edit.Id, edit.Field, edit.Value);
    }

    public static VersionDifference Compare(Project current, Project version)
    {
        var a = current.Randomization;
        var b = version.Randomization;
        var mine = current.Edits.All.ToDictionary(e => e.Key);
        var theirs = version.Edits.All.ToDictionary(e => e.Key);
        return new VersionDifference(
            Randomization: a.Enabled != b.Enabled,
            Seed: a.Seed != b.Seed,
            Preset: !(a.Preset ?? []).AsSpan().SequenceEqual(b.Preset ?? []),
            EditsAdded: theirs.Keys.Count(k => !mine.ContainsKey(k)),
            EditsRemoved: mine.Keys.Count(k => !theirs.ContainsKey(k)),
            EditsChanged: theirs.Count(t => mine.TryGetValue(t.Key, out var m) && !System.Text.Json.Nodes.JsonNode.DeepEquals(m.Value, t.Value.Value)));
    }

    /// <summary>Hash of what determines the built ROM: randomization (enabled, preset, seed) and edits.</summary>
    public static string Fingerprint(Project project)
    {
        var r = project.Randomization;
        var sb = new StringBuilder();
        sb.Append(r.Enabled).Append('|').Append(r.Seed).Append('|').Append(Convert.ToBase64String(r.Preset ?? [])).Append('|');
        foreach (var edit in project.Edits.All.OrderBy(e => e.Table, StringComparer.Ordinal).ThenBy(e => e.Id).ThenBy(e => e.Field, StringComparer.Ordinal))
            sb.Append(edit.Table).Append('/').Append(edit.Id).Append('/').Append(edit.Field).Append('=').Append(edit.Value.ToJsonString()).Append(';');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())))[..16];
    }
}
