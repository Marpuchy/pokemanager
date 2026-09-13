using Pokemanager.Model.Projects;
using Pokemanager.Randomizer;

namespace Pokemanager.App.Services;

/// <summary>UPR ZX incluido con la app, Java del sistema y la caché de randomizaciones.</summary>
public sealed class UprService
{
    private UprTools? tools;
    private bool searched;
    private UprSettingsDescription? defaultTweaksForRom;

    public RandomizationCache Cache { get; } = new(RandomizationCache.DefaultRoot);

    public UprTools? Tools
    {
        get
        {
            if (!searched)
            {
                tools = UprLocator.Find();
                searched = true;
            }
            return tools;
        }
    }

    public string StatusText => Tools is { } t
        ? $"Universal Pokémon Randomizer ZX incluido · Java {t.JavaMajorVersion}"
        : File.Exists(UprLocator.BundledJar)
            ? $"Falta Java {UprLocator.MinimumJava} o superior para ejecutar el randomizer"
            : $"Falta el randomizer incluido ({UprLocator.BundledJar})";

    /// <summary>Salida de UPR ya generada para el proyecto, sin ejecutar nada.</summary>
    public UprResult? TryGetCached(Project project, byte[]? effectivePreset)
    {
        var r = project.Randomization;
        if (!r.IsReady || effectivePreset is null || project.ResolveRomFile() is not { } rom || Tools is not { } t)
            return null;
        return Cache.TryGet(rom, effectivePreset, r.Seed, t.JarPath);
    }

    /// <summary>
    /// Qué ajustes varios admite la ROM. Se calcula una vez por sesión (UPR tiene que cargar la ROM).
    /// </summary>
    public async Task<IReadOnlySet<string>> AvailableTweaksAsync(string romFile)
    {
        if (Tools is not { } t)
            return new HashSet<string>();
        defaultTweaksForRom ??= await new UprRunner(t).DescribeSettingsAsync(null, romFile);
        return defaultTweaksForRom.Tweaks.Where(x => x.Available).Select(x => x.Name).ToHashSet();
    }
}
