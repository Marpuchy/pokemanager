using System.Text.Json;
using System.Text.Json.Serialization;
using Pokemanager.Bridge;

namespace Pokemanager.App.Services;

/// <summary>Un proyecto abierto alguna vez en este equipo.</summary>
public sealed record RecentProject(string Path, DateTime LastOpened);

/// <summary>Ajustes de Pokemanager en este equipo (no del proyecto).</summary>
public sealed class AppSettings
{
    public const int MaxRecentProjects = 15;

    public string? LastProject { get; set; }

    /// <summary>Proyectos abiertos recientemente, el más reciente primero.</summary>
    public List<RecentProject> RecentProjects { get; set; } = [];

    /// <summary>Carpeta de usuario del emulador elegida en Ajustes. Null: la del emulador usado más recientemente.</summary>
    public string? EmulatorUserDirectory { get; set; }

    /// <summary>
    /// Archivo desde el que se cargaron estos ajustes. Solo se guarda en disco si viene de <see cref="Load"/>:
    /// unos ajustes creados con <c>new</c> (pruebas, previsualizaciones) nunca tocan los del usuario.
    /// </summary>
    [JsonIgnore]
    public string? FilePath { get; private set; }

    /// <summary>Carpeta de usuario del emulador que se usa: la elegida o la detectada.</summary>
    [JsonIgnore]
    public string? EffectiveEmulatorDirectory =>
        EmulatorUserDirectory is { } chosen && Directory.Exists(chosen)
            ? chosen
            : EmulatorUserFolders.Detect().FirstOrDefault()?.Path;

    /// <summary>Nombre para mostrar del emulador en uso («Citra», «Azahar» o la carpeta).</summary>
    [JsonIgnore]
    public string EffectiveEmulatorName =>
        EffectiveEmulatorDirectory is not { } dir
            ? "ningún emulador"
            : EmulatorUserFolders.Detect().FirstOrDefault(e => string.Equals(e.Path, dir, StringComparison.OrdinalIgnoreCase))?.Name
              ?? Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar));

    public static string DataRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pokemanager");

    /// <summary>Copias de seguridad de partidas antes de modificarlas.</summary>
    public static string BackupRoot => Path.Combine(DataRoot, "copias-partida");

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

    /// <summary>Registra un proyecto como abierto ahora (y como último proyecto).</summary>
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

    /// <summary>Quita de la lista los proyectos cuyo archivo ya no existe.</summary>
    public IReadOnlyList<RecentProject> ExistingRecentProjects()
    {
        // Proyectos anteriores a la lista de recientes: el último abierto entra en ella.
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
            // Las preferencias son una comodidad: si no se pueden guardar, se sigue sin ellas.
        }
    }
}
