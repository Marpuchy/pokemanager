using Pokemanager.App.Resources;
using Pokemanager.Model.Data;
using Pokemanager.Model.Mods;
using Pokemanager.Model.Projects;
using Pokemanager.Randomizer;

namespace Pokemanager.App.Services;

/// <summary>The UPR ZX bundled with the app, the system Java and the randomization cache.</summary>
public sealed class UprService
{
    private UprTools? tools;
    private bool searched;
    private UprSettingsDescription? defaultTweaksForRom;

    /// <summary>Mega Stones per romfs stack: reading them opens two archives, and every lookup needs them.</summary>
    private readonly Dictionary<string, int[]> megaStones = new(StringComparer.OrdinalIgnoreCase);

    public UprService()
    {
    }

    /// <summary>A cache of its own and ready-made tools, so a test needs neither the user's cache nor Java.</summary>
    internal UprService(string cacheRoot, UprTools toolsForTest, ModLibrary? modLibrary = null)
    {
        Cache = new RandomizationCache(cacheRoot);
        ModdedRoms = new ModdedRomCache(Path.Combine(cacheRoot, "modded-roms"));
        Mods = modLibrary ?? ModLibrary.Default;
        tools = toolsForTest;
        searched = true;
    }

    public RandomizationCache Cache { get; } = new(RandomizationCache.DefaultRoot);

    /// <summary>Base ROMs with a project's mods packed into them, which is what UPR and the build are handed.</summary>
    public ModdedRomCache ModdedRoms { get; } = new(ModdedRomCache.DefaultRoot);

    /// <summary>The user's own copies of the mod files. Pokemanager ships none of them and hosts none of them.</summary>
    public ModLibrary Mods { get; } = ModLibrary.Default;

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
    /// The game's Mega Stones, read from the project's own dump and its mods. Not from the editing session: the
    /// session's base is the randomization itself, which is what the key is for, so taking the list from there would
    /// need the key before the key could be built. What is read is the project's read stack without the randomization
    /// — nothing randomizes the mega evolution table and no editable table touches it — so both sides of the cache get
    /// the same list.
    /// </summary>
    public IReadOnlyList<int> MegaStones(Project project)
    {
        // Read through the project's own read stack, so a mod that replaces the mega evolution table is seen. The
        // randomization is deliberately left out of the stack (it is what the key is for, so taking the list from
        // there would need the key before the key could be built), and the key is the whole stack, so two projects
        // with different mods over the same dump do not share an answer.
        var layers = project.ReadLayers(null, Mods);
        string key = string.Join('|', layers.Roots);
        if (!megaStones.TryGetValue(key, out int[]? stones))
            megaStones[key] = stones = GameData.ReadMegaStones(layers, project.Game);
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

    /// <summary>
    /// The base ROM the rest of the pipeline must work on: the project's own ROM with its mods packed into it, or the
    /// plain ROM when it has none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the stage that runs <b>before</b> the randomization. A mod of this kind replaces whole files, so it
    /// cannot share the tables with UPR: applied afterwards it wipes the randomization, applied to the dump alone it
    /// leaves UPR randomizing the species the game shipped with. Packing a base ROM that already has the mod and
    /// handing UPR that one is what makes the randomization cover the species the mod added.
    /// </para>
    /// <para>
    /// <b>A project with no mods gets its own ROM back, untouched</b>, so nothing of the build changes for it — and
    /// so does a project whose only mods are 3GX plugins, which are loaded by the emulator and put nothing in the ROM
    /// (<see cref="ModSettings.EnabledRomMods"/>).
    /// </para>
    /// </remarks>
    /// <returns>The ROM to randomize and to pack from, and the installations it was made of (empty without mods).</returns>
    /// <exception cref="InvalidOperationException">
    /// A mod the project names is unknown to this version, or its files are missing or are not the files they claim to
    /// be. The message says which, and the build stops instead of quietly producing a ROM without the mod.
    /// </exception>
    public async Task<(string Rom, IReadOnlyList<ModInstallation> Installed)> BaseRomAsync(
        Project project, string baseRom, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (project.Mods.HasNoRomMods)
            return (baseRom, []);

        var installed = new List<ModInstallation>();
        var files = new List<ModdedRomFile>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string id in project.Mods.EnabledRomMods)
        {
            var definition = ModCatalog.Find(id)
                             ?? throw new InvalidOperationException(string.Format(Strings.Mods_UnknownMod, id));
            var installation = Mods.Inspect(definition);
            if (installation.Problem is { } problem)
                throw new InvalidOperationException(problem);
            installed.Add(installation);
            // Earlier mods in the list win, the same order the read layers use.
            foreach (var content in installation.Contents.Where(c => seen.Add(c.TitlePath)))
                files.Add(new ModdedRomFile(content.TitlePath, content.LocalPath, content.Sha256));
        }

        if (files.Count == 0)
            return (baseRom, installed);
        if (Tools is not { } t)
            throw new InvalidOperationException(StatusText);
        string rom = await ModdedRoms.GetOrCreateAsync(new UprRunner(t), baseRom, files, progress, cancellationToken);
        return (rom, installed);
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
