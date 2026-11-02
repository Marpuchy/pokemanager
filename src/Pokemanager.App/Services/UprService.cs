using Pokemanager.App.Resources;
using Pokemanager.Model.Data;
using Pokemanager.Model.Projects;
using Pokemanager.Randomizer;

namespace Pokemanager.App.Services;

/// <summary>The UPR ZX bundled with the app, the system Java and the randomization cache.</summary>
public sealed class UprService
{
    private UprTools? tools;
    private bool searched;
    private UprSettingsDescription? defaultTweaksForRom;

    /// <summary>Mega Stones per dump folder: reading them opens two archives, and every lookup needs them.</summary>
    private readonly Dictionary<string, int[]> megaStones = new(StringComparer.OrdinalIgnoreCase);

    public UprService()
    {
    }

    /// <summary>A cache of its own and ready-made tools, so a test needs neither the user's cache nor Java.</summary>
    internal UprService(string cacheRoot, UprTools toolsForTest)
    {
        Cache = new RandomizationCache(cacheRoot);
        tools = toolsForTest;
        searched = true;
    }

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

    /// <summary>
    /// The options that are part of the cache key besides the ROM, the preset, the seed and the jar
    /// (<see cref="RandomizationCache.Key"/>). Every path goes through here, so a randomization is looked up under the
    /// key it was written with: reading them from somewhere else left the item options out of the key on the way in,
    /// and the lookup then missed every time the user had turned one on.
    /// </summary>
    public (bool AllItems, IReadOnlyList<int>? GoodItems) CacheOptions(Project project)
    {
        var r = project.Randomization;
        return (r.AllowAllItems, r.MegaStonesInPool ? MegaStones(project) : null);
    }

    /// <summary>
    /// The game's Mega Stones, read from the project's own dump. Not from the editing session: the session's base is
    /// the randomization itself, which is what the key is for, so taking the list from there would need the key before
    /// the key could be built. The dump holds the mega evolution table either way — nothing randomizes it, and no
    /// editable table touches it — so both sides of the cache get the same list.
    /// </summary>
    public IReadOnlyList<int> MegaStones(Project project)
    {
        string romFs = Path.GetFullPath(project.RomFsPath);
        if (!megaStones.TryGetValue(romFs, out int[]? stones))
            megaStones[romFs] = stones = GameData.ReadMegaStones(new RomFsLayers(romFs), project.Game);
        return stones;
    }

    /// <summary>UPR output already generated for the project, without running anything.</summary>
    public UprResult? TryGetCached(Project project, byte[]? effectivePreset)
    {
        var r = project.Randomization;
        if (!r.IsReady || effectivePreset is null || project.ResolveRomFile() is not { } rom || Tools is not { } t)
            return null;
        var (allItems, goodItems) = CacheOptions(project);
        return Cache.TryGet(rom, effectivePreset, r.Seed, t.JarPath, allItems, goodItems);
    }

    /// <summary>
    /// The project's randomization, from the cache or run now. The counterpart of <see cref="TryGetCached"/>: both
    /// build the key from <see cref="CacheOptions"/>, so what the build stores is what the lookup finds.
    /// </summary>
    public Task<UprResult> RandomizeAsync(UprTools t, Project project, string romFile, byte[] preset,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var (allItems, goodItems) = CacheOptions(project);
        return Cache.GetOrCreateAsync(new UprRunner(t), t, romFile, preset, project.Randomization.Seed, progress,
            cancellationToken, allItems, goodItems);
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
