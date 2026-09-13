using Pokemanager.Model.Projects;
using Pokemanager.Randomizer;

namespace Pokemanager.App.Services;

/// <summary>Java, el jar de UPR ZX y la caché de randomizaciones para la aplicación.</summary>
public sealed class UprService(AppSettings settings)
{
    private (string Path, int Major)? java;
    private bool javaSearched;

    public RandomizationCache Cache { get; } = new(RandomizationCache.DefaultRoot);

    public (string Path, int Major)? Java
    {
        get
        {
            if (!javaSearched)
            {
                java = UprLocator.FindJava();
                javaSearched = true;
            }
            return java;
        }
    }

    /// <summary>Jar elegido antes o, si no, buscado en las carpetas habituales y en las de primer nivel de cada unidad.</summary>
    public string? JarPath
    {
        get
        {
            if (settings.UprJarPath is { } saved && File.Exists(saved))
                return saved;

            var folders = new List<string>
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
            };
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
            {
                try { folders.AddRange(Directory.EnumerateDirectories(drive.RootDirectory.FullName)); }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { }
            }

            if (UprLocator.FindJar(folders) is { } found)
                SetJar(found);
            return settings.UprJarPath is { } p && File.Exists(p) ? p : null;
        }
    }

    public void SetJar(string path)
    {
        settings.UprJarPath = path;
        settings.Save();
    }

    public UprTools? Tools => Java is { } j && JarPath is { } jar ? new UprTools(j.Path, j.Major, jar) : null;

    public string StatusText
    {
        get
        {
            string javaText = Java is { } j ? $"Java {j.Major}" : $"Falta Java {UprLocator.MinimumJava} o superior";
            string jarText = JarPath is { } jar ? jar : "No se encuentra PokeRandoZX.jar";
            return $"{javaText} · {jarText}";
        }
    }

    /// <summary>Salida de UPR ya generada para el proyecto, sin ejecutar nada.</summary>
    public UprResult? TryGetCached(Project project)
    {
        var r = project.Randomization;
        if (!r.IsReady || project.ResolveRomFile() is not { } rom || JarPath is not { } jar)
            return null;
        return Cache.TryGet(rom, r.Preset!, r.Seed, jar);
    }

    /// <summary>Los .rnqs que haya junto al jar (donde UPR ZX los guarda por defecto).</summary>
    public IReadOnlyList<string> PresetsNextToJar() =>
        JarPath is { } jar && Path.GetDirectoryName(jar) is { } dir
            ? Directory.GetFiles(dir, "*.rnqs").Order().ToList()
            : [];
}
