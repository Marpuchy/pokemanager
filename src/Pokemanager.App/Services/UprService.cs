using Pokemanager.App.Resources;
using Pokemanager.Model.Projects;
using Pokemanager.Randomizer;

namespace Pokemanager.App.Services;

/// <summary>The UPR ZX bundled with the app, the system Java and the randomization cache.</summary>
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
        ? string.Format(Strings.Upr_Status_Ok, t.JavaMajorVersion)
        : File.Exists(UprLocator.BundledJar)
            ? string.Format(Strings.Upr_Status_NoJava, UprLocator.MinimumJava)
            : string.Format(Strings.Upr_Status_NoJar, UprLocator.BundledJar);

    /// <summary>UPR output already generated for the project, without running anything.</summary>
    public UprResult? TryGetCached(Project project, byte[]? effectivePreset)
    {
        var r = project.Randomization;
        if (!r.IsReady || effectivePreset is null || project.ResolveRomFile() is not { } rom || Tools is not { } t)
            return null;
        return Cache.TryGet(rom, effectivePreset, r.Seed, t.JarPath);
    }

    /// <summary>Which misc tweaks the ROM supports. Computed once per session (UPR has to load the ROM).</summary>
    public async Task<IReadOnlySet<string>> AvailableTweaksAsync(string romFile)
    {
        if (Tools is not { } t)
            return new HashSet<string>();
        defaultTweaksForRom ??= await new UprRunner(t).DescribeSettingsAsync(null, romFile);
        return defaultTweaksForRom.Tweaks.Where(x => x.Available).Select(x => x.Name).ToHashSet();
    }
}
