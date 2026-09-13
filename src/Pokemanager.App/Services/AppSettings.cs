using System.Text.Json;
using System.Text.Json.Serialization;
using Pokemanager.App.Resources;
using Pokemanager.Bridge;

namespace Pokemanager.App.Services;

/// <summary>A project opened at some point on this machine.</summary>
public sealed record RecentProject(string Path, DateTime LastOpened);

/// <summary>Pokemanager settings on this machine (not per project).</summary>
public sealed class AppSettings
{
    public const int MaxRecentProjects = 15;
    public const string DefaultLanguage = "en";

    /// <summary>UI languages with a translation.</summary>
    public static IReadOnlySet<string?> SupportedLanguages { get; } = new HashSet<string?> { "en", "es" };

    /// <summary>UI language code ("en", "es"). Applied when the app starts.</summary>
    public string UiLanguage { get; set; } = DefaultLanguage;

    public string? LastProject { get; set; }

    /// <summary>Recently opened projects, most recent first.</summary>
    public List<RecentProject> RecentProjects { get; set; } = [];

    /// <summary>Emulator user folder chosen in Settings. Null: the most recently used emulator.</summary>
    public string? EmulatorUserDirectory { get; set; }

    /// <summary>Emulator program chosen in Settings, used by "Play". Null: detected.</summary>
    public string? EmulatorExecutable { get; set; }

    /// <summary>
    /// Emulator program to play with: the chosen one, or a detected one — preferably of the same emulator as the user folder
    /// in use (Citra's program for Citra's save).
    /// </summary>
    public string? EffectiveEmulatorExecutable(params string?[] hints)
    {
        if (EmulatorExecutable is { } chosen && File.Exists(chosen))
            return chosen;

        string name = EffectiveEmulatorName;
        string key = name + "|" + string.Join("|", hints);
        lock (DetectedPrograms)
        {
            if (DetectedPrograms.TryGetValue(key, out string? cached) && (cached is null || File.Exists(cached)))
                return cached;
        }

        var configHints = EmulatorUserFolders.Detect().SelectMany(e => EmulatorExecutables.ConfigHints(e.Path));
        var found = EmulatorExecutables.Detect(hints.Concat(configHints));
        string? path = (found.FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? found.FirstOrDefault())?.Path;
        lock (DetectedPrograms)
            DetectedPrograms[key] = path;
        return path;
    }

    /// <summary>Detection results of this session: searching folders takes a moment.</summary>
    private static readonly Dictionary<string, string?> DetectedPrograms = [];

    /// <summary>
    /// File these settings were loaded from. Only settings coming from <see cref="Load"/> are written to disk:
    /// settings created with <c>new</c> (tests, previews) never touch the user's file.
    /// </summary>
    [JsonIgnore]
    public string? FilePath { get; private set; }

    /// <summary>Emulator user folder in use: the chosen one or the detected one.</summary>
    [JsonIgnore]
    public string? EffectiveEmulatorDirectory =>
        EmulatorUserDirectory is { } chosen && Directory.Exists(chosen)
            ? chosen
            : EmulatorUserFolders.Detect().FirstOrDefault()?.Path;

    /// <summary>Display name of the emulator in use ("Citra", "Azahar" or the folder name).</summary>
    [JsonIgnore]
    public string EffectiveEmulatorName =>
        EffectiveEmulatorDirectory is not { } dir
            ? Strings.Emulator_None
            : EmulatorUserFolders.Detect().FirstOrDefault(e => string.Equals(e.Path, dir, StringComparison.OrdinalIgnoreCase))?.Name
              ?? Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar));

    /// <summary>Caches and save backups. <c>POKEMANAGER_DATA</c> moves it (tests and harnesses must not use the real one).</summary>
    public static string DataRoot => Environment.GetEnvironmentVariable("POKEMANAGER_DATA") is { Length: > 0 } custom
        ? custom
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pokemanager");

    /// <summary>Save backups made before modifying a save.</summary>
    public static string BackupRoot => Path.Combine(DataRoot, "save-backups");

    public static string DefaultFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pokemanager", "settings.json");

    public static AppSettings Load(string? path = null)
    {
        path ??= DefaultFilePath;
        AppSettings settings;
        try
        {
            settings = File.Exists(path)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings()
                : new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            settings = new AppSettings();
        }
        settings.FilePath = path;
        return settings;
    }

    /// <summary>Records a project as opened now (and as the last project).</summary>
    public void TouchProject(string projectPath)
    {
        string full = Path.GetFullPath(projectPath);
        LastProject = full;
        RecentProjects.RemoveAll(r => string.Equals(r.Path, full, StringComparison.OrdinalIgnoreCase));
        RecentProjects.Insert(0, new RecentProject(full, DateTime.Now));
        if (RecentProjects.Count > MaxRecentProjects)
            RecentProjects.RemoveRange(MaxRecentProjects, RecentProjects.Count - MaxRecentProjects);
        Save();
    }

    /// <summary>Recent projects whose file still exists.</summary>
    public IReadOnlyList<RecentProject> ExistingRecentProjects()
    {
        // Projects from before the recent list existed: the last opened one joins it.
        if (LastProject is { } last && File.Exists(last) && RecentProjects.All(r => !string.Equals(r.Path, last, StringComparison.OrdinalIgnoreCase)))
            RecentProjects.Add(new RecentProject(last, File.GetLastWriteTime(last)));
        return RecentProjects.Where(r => File.Exists(r.Path)).ToList();
    }

    public void ForgetProject(string projectPath)
    {
        RecentProjects.RemoveAll(r => string.Equals(r.Path, projectPath, StringComparison.OrdinalIgnoreCase));
        if (string.Equals(LastProject, projectPath, StringComparison.OrdinalIgnoreCase))
            LastProject = null;
        Save();
    }

    public void Save()
    {
        if (FilePath is null)
            return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Preferences are a convenience: if they cannot be saved, the app carries on without them.
        }
    }
}
