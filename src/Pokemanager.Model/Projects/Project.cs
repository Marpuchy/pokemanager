using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Pokemanager.Model.Dump;
using Pokemanager.Model.Edits;
using Pokemanager.Model.Resources;

namespace Pokemanager.Model.Projects;

/// <summary>Project randomization: the UPR ZX preset (embedded) and the seed.</summary>
public sealed class RandomizationSettings
{
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
        Enabled = other.Enabled;
        PresetName = other.PresetName;
        Preset = other.Preset;
        PresetModified = other.PresetModified;
        Seed = other.Seed;
        InstalledSeed = other.InstalledSeed;
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

    public EditSet Edits { get; } = new();

    public string RomFsPath => Path.Combine(DumpDirectory, "romfs");
    public string ExeFsPath => Path.Combine(DumpDirectory, "exefs");

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
            Edits.All.Select(e => new EditEntry(e.Table, e.Id, e.Field, e.Value)).ToList(), Locke, Game);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(file, JsonOptions));
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>
    /// What the user changes in a project — edits, randomization and Locke rules — as bytes, for undo. Edits are sorted,
    /// so the same state always gives the same bytes.
    /// </summary>
    public byte[] CaptureState() => JsonSerializer.SerializeToUtf8Bytes(new ProjectState(
        Edits.All.OrderBy(e => e.Table, StringComparer.Ordinal).ThenBy(e => e.Id).ThenBy(e => e.Field, StringComparer.Ordinal)
            .Select(e => new EditEntry(e.Table, e.Id, e.Field, e.Value)).ToList(),
        Randomization, Locke, Shops), JsonOptions);

    /// <summary>A state captured with <see cref="CaptureState"/>.</summary>
    public static (IReadOnlyList<Edit> Edits, RandomizationSettings Randomization, LockeSettings Locke, ShopSettings Shops) ReadState(byte[] state)
    {
        var s = JsonSerializer.Deserialize<ProjectState>(state, JsonOptions) ?? throw new InvalidDataException("Empty project state.");
        return ([.. s.Edits.Select(e => new Edit(e.Table, e.Id, e.Field, e.Value))], s.Randomization, s.Locke, s.Shops ?? new ShopSettings());
    }

    /// <param name="Shops">Null in states captured before 3.0.</param>
    private sealed record ProjectState(List<EditEntry> Edits, RandomizationSettings Randomization, LockeSettings Locke, ShopSettings? Shops = null);

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
        };
        foreach (var e in file.Edits ?? [])
            project.Edits.Set(e.Table, e.Id, e.Field, e.Value);
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
        GameTitle? Game = null);

    private sealed record EditEntry(string Table, int Id, string Field, JsonNode Value);
}
