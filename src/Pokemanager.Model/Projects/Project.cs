using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Pokemanager.Model.Data;
using Pokemanager.Model.Dump;
using Pokemanager.Model.Edits;
using Pokemanager.Model.Mods;
using Pokemanager.Model.Resources;

namespace Pokemanager.Model.Projects;

/// <summary>Project randomization: the UPR ZX preset (embedded) and the seed.</summary>
public sealed class RandomizationSettings
{
    /// <summary>
    /// Let the item randomization use every item the game has. UPR ZX chooses from a list of its own that leaves out
    /// whatever the game never hands the player — in Generation 7 that is the Z-crystals, the TMs and most of what the
    /// generation added (466 of 960 ids). With this on, every id the game has a name for can come up, and the
    /// randomizer's own filters (ban bad items, ban regular shop items, ban overpowered ones) go on working: what UPR
    /// calls a bad item stays bad.
    /// </summary>
    public bool AllowAllItems { get; set; }

    /// <summary>
    /// Let the game's Mega Stones come up wherever the randomizer places items. They are not forced anywhere: they
    /// simply stop counting as "bad items", which in Generation 7 is what keeps every one of them out of a run whose
    /// preset bans those (UPR ZX allows all 42 and marks all 42 bad; in X they are in both lists already).
    /// </summary>
    public bool MegaStonesInPool { get; set; }

    public bool Enabled { get; set; }

    /// <summary>Display name of the preset (usually the name of the source .rnqs).</summary>
    public string? PresetName { get; set; }

    /// <summary>Contents of the .rnqs. About 100 bytes, so it is stored inside the project to keep it shareable.</summary>
    public byte[]? Preset { get; set; }

    /// <summary>The preset was edited in the app after being imported.</summary>
    public bool PresetModified { get; set; }

    public long Seed { get; set; }

    /// <summary>Seed of the last built ROM, to warn when it changes with a game in progress.</summary>
    public long? InstalledSeed { get; set; }

    /// <summary>
    /// The cache folder of the randomization the installed ROM was built from, so the editor can still read it after the
    /// randomizer is updated (which changes every cache key) and before the next build.
    /// </summary>
    public string? InstalledRandomization { get; set; }

    /// <summary>What decided that randomization besides the randomizer (options, item options, mods), hashed.</summary>
    public string? InstalledRandomizationStamp { get; set; }

    /// <summary>File name (without extension) of the randomized ROM, created next to the base ROM.</summary>
    public string? OutputName { get; set; }

    /// <summary>Path of the last ROM built by this project.</summary>
    public string? LastBuiltRom { get; set; }

    /// <summary>When building, replace this project's previous ROM instead of creating another file.</summary>
    public bool ReplacePreviousRom { get; set; } = true;

    /// <summary>When building, adapt the emulator's save (abilities and party stats) to the new ROM.</summary>
    public bool UpdateSave { get; set; } = true;

    /// <summary>There is a seed. The preset may be missing: UPR ZX defaults are used then.</summary>
    public bool IsReady => Enabled && Seed > 0;

    /// <summary>Takes every value of <paramref name="other"/> (undo keeps the same instance, which views hold).</summary>
    public void CopyFrom(RandomizationSettings other)
    {
        AllowAllItems = other.AllowAllItems;
        MegaStonesInPool = other.MegaStonesInPool;
        Enabled = other.Enabled;
        PresetName = other.PresetName;
        Preset = other.Preset;
        PresetModified = other.PresetModified;
        Seed = other.Seed;
        InstalledSeed = other.InstalledSeed;
        InstalledRandomization = other.InstalledRandomization;
        InstalledRandomizationStamp = other.InstalledRandomizationStamp;
        OutputName = other.OutputName;
        LastBuiltRom = other.LastBuiltRom;
        ReplacePreviousRom = other.ReplacePreviousRom;
        UpdateSave = other.UpdateSave;
    }
}

/// <summary>
/// Shareable project: where the dump is, the game text language, the randomization and the manual edits.
/// It never contains a single byte of the game.
/// </summary>
public sealed class Project
{
    public const int CurrentFormat = 2;

    /// <summary>Folder that contains <c>romfs</c> and <c>exefs</c>.</summary>
    public required string DumpDirectory { get; set; }

    /// <summary>The game (older projects were always X/Y and did not store it; the dump then tells).</summary>
    public GameTitle Game { get; set; } = GameTitle.X;

    /// <summary>Decrypted ROM (.3ds/.cxi) used by the randomizer. Null: looked up in <see cref="DumpDirectory"/>.</summary>
    public string? RomFile { get; set; }

    public GameLanguage Language { get; set; } = GameLanguage.English;

    /// <summary>Legacy (format 1–2): the emulator folder now lives in the application settings.</summary>
    public string? EmulatorUserDirectory { get; set; }

    public RandomizationSettings Randomization { get; set; } = new();

    /// <summary>Nuzlocke rules: lives and the badge roulette.</summary>
    public LockeSettings Locke { get; set; } = new();

    /// <summary>What the shops of the built ROM sell beyond what the game does. Defaults to nothing.</summary>
    public ShopSettings Shops { get; set; } = new();

    /// <summary>How the built ROM behaves (shiny odds and the like). Defaults to the game's own behaviour.</summary>
    public GameSettings Tweaks { get; set; } = new();

    /// <summary>
    /// Community mods this project plays with, by id. Only the ids are in the file: the files themselves are the
    /// user's own (<see cref="ModLibrary"/>). Defaults to none, which is the behaviour the application always had.
    /// </summary>
    public ModSettings Mods { get; set; } = new();

    public EditSet Edits { get; } = new();

    public string RomFsPath => Path.Combine(DumpDirectory, "romfs");
    public string ExeFsPath => Path.Combine(DumpDirectory, "exefs");

    /// <summary>
    /// The romfs the application reads for this project, as a stack: the randomization (when there is one) over the
    /// enabled mods over the dump. This is the one place that order is decided, so every reader — the editing session,
    /// the icons, the trainer portraits — sees the same thing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The mod layer sits <b>under</b> the randomization and <b>over</b> the dump: the randomizer worked on a base ROM
    /// that already had the mod, so its output is the newer word on any file it wrote, while a file the mod replaces
    /// and the randomizer did not touch has to come from the mod and not from the dump. With no mods the stack is the
    /// single dump folder it always was.
    /// </para>
    /// <para>
    /// <b>Only a mod that brings files to the game is a layer</b> (<see cref="ModSettings.EnabledRomMods"/>): a 3GX
    /// plugin mod changes no byte of the ROM, so a project with nothing but one of those reads exactly the single dump
    /// folder it read before mods existed.
    /// </para>
    /// </remarks>
    /// <param name="randomizedRomFs">romfs the randomizer produced, or null when there is no randomization.</param>
    /// <param name="library">The mod library to read from; <see cref="ModLibrary.Default"/> when not given.</param>
    public RomFsLayers ReadLayers(string? randomizedRomFs = null, ModLibrary? library = null)
    {
        if (randomizedRomFs is null && Mods.HasNoRomMods)
            return new RomFsLayers(RomFsPath); // the ordinary case: not a single file is looked at
        var roots = new List<string>();
        if (randomizedRomFs is not null)
            roots.Add(randomizedRomFs);
        roots.AddRange((library ?? ModLibrary.Default).RomFsRoots(Mods));
        roots.Add(RomFsPath);
        return new RomFsLayers([.. roots]);
    }

    /// <summary>
    /// The executable the application reads: a mod's <c>code.bin</c> when one replaces it, the dump's otherwise. A mod
    /// that adds species carries the code that reads them, so taking the dump's would undo it.
    /// </summary>
    public string CodePath(ModLibrary? library = null) =>
        (Mods.HasNoRomMods ? null : (library ?? ModLibrary.Default).CodePath(Mods)) ?? Path.Combine(ExeFsPath, "code.bin");

    /// <summary><see cref="RomFile"/> or, when not set, the original ROM in the dump folder.</summary>
    public string? ResolveRomFile()
    {
        if (!string.IsNullOrWhiteSpace(RomFile))
            return File.Exists(RomFile) ? RomFile : null;
        return FindBaseRom(DumpDirectory);
    }

    /// <summary>
    /// Original ROM in a folder: .3ds/.cci are preferred over .cxi, and randomized ROMs are skipped because they have
    /// their log next to them (<c>&lt;rom&gt;.log</c>, as Pokemanager and UPR ZX create them). A ROM built in the same
    /// folder is therefore never taken as the base.
    /// </summary>
    public static string? FindBaseRom(string directory)
    {
        if (!Directory.Exists(directory))
            return null;
        return Directory.EnumerateFiles(directory)
            .Where(f => Path.GetExtension(f).ToLowerInvariant() is ".3ds" or ".cci" or ".cxi")
            .Where(f => !File.Exists(f + ".log"))
            .OrderBy(f => Path.GetExtension(f).Equals(".cxi", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenBy(f => f, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public void Save(string path)
    {
        var file = new ProjectFile(CurrentFormat, DumpDirectory, RomFile, Language, EmulatorUserDirectory, Randomization,
            Edits.All.Select(e => new EditEntry(e.Table, e.Id, e.Field, e.Value)).ToList(), Locke, Game, Shops, Tweaks, Mods);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(file, JsonOptions));
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>
    /// What the user changes in a project — edits, randomization and Locke rules — as bytes, for undo. Edits are sorted,
    /// so the same state always gives the same bytes.
    /// </summary>
    /// <remarks>
    /// <see cref="Mods"/> is deliberately not part of it, for the same reason the randomizer's own options are not:
    /// turning a mod on or off changes the base data the editing session was opened over, so putting it back needs the
    /// session reopened and not a value restored. The project file keeps them all the same.
    /// </remarks>
    public byte[] CaptureState() => JsonSerializer.SerializeToUtf8Bytes(new ProjectState(
        Edits.All.OrderBy(e => e.Table, StringComparer.Ordinal).ThenBy(e => e.Id).ThenBy(e => e.Field, StringComparer.Ordinal)
            .Select(e => new EditEntry(e.Table, e.Id, e.Field, e.Value)).ToList(),
        Randomization, Locke, Shops, Tweaks), JsonOptions);

    /// <summary>A state captured with <see cref="CaptureState"/>.</summary>
    public static (IReadOnlyList<Edit> Edits, RandomizationSettings Randomization, LockeSettings Locke, ShopSettings Shops, GameSettings Tweaks) ReadState(byte[] state)
    {
        var s = JsonSerializer.Deserialize<ProjectState>(state, JsonOptions) ?? throw new InvalidDataException("Empty project state.");
        return ([.. s.Edits.Select(e => new Edit(e.Table, e.Id, e.Field, e.Value))], s.Randomization, s.Locke,
            s.Shops ?? new ShopSettings(), s.Tweaks ?? new GameSettings());
    }

    /// <param name="Shops">Null in states captured before 3.0.</param>
    /// <param name="Tweaks">Null in states captured before the game options existed.</param>
    private sealed record ProjectState(List<EditEntry> Edits, RandomizationSettings Randomization, LockeSettings Locke,
        ShopSettings? Shops = null, GameSettings? Tweaks = null);

    /// <summary>Generation 7 Locke milestones are the story's Z-crystals (<see cref="LockeSettings.UseCrystalMilestones"/>).</summary>
    public void UpgradeLocke()
    {
        if (LockeMilestones.ByCrystals(Game))
            Locke.UseCrystalMilestones(LockeMilestones.Of(Game).Count);
    }

    public static Project Load(string path)
    {
        var file = JsonSerializer.Deserialize<ProjectFile>(File.ReadAllText(path), JsonOptions)
                   ?? throw new InvalidDataException(string.Format(Strings.Project_Empty, path));
        if (file.Format > CurrentFormat)
            throw new InvalidDataException(string.Format(Strings.Project_NewerFormat, file.Format, CurrentFormat));

        var project = new Project
        {
            DumpDirectory = file.DumpDirectory,
            RomFile = file.RomFile,
            Language = file.Language,
            EmulatorUserDirectory = file.EmulatorUserDirectory,
            Randomization = file.Randomization ?? new RandomizationSettings(), // format 1 did not have it
            Locke = file.Locke ?? new LockeSettings(), // added later in format 2
            Game = file.Game ?? GameTitle.X, // added with other games; earlier projects were X/Y
            Shops = file.Shops ?? new ShopSettings(), // 3.0
            Tweaks = file.Tweaks ?? new GameSettings(), // 3.0
            Mods = file.Mods ?? new ModSettings(), // added with the community mods
        };
        foreach (var e in file.Edits ?? [])
            project.Edits.Set(e.Table, e.Id, e.Field, e.Value);
        project.UpgradeLocke();
        return project;
    }

    private sealed record ProjectFile(
        int Format,
        string DumpDirectory,
        string? RomFile,
        GameLanguage Language,
        string? EmulatorUserDirectory,
        RandomizationSettings? Randomization,
        List<EditEntry>? Edits,
        LockeSettings? Locke = null,
        GameTitle? Game = null,
        ShopSettings? Shops = null,
        GameSettings? Tweaks = null,
        ModSettings? Mods = null);

    private sealed record EditEntry(string Table, int Id, string Field, JsonNode Value);
}
