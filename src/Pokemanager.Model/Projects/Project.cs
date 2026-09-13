using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Pokemanager.Model.Dump;
using Pokemanager.Model.Edits;

namespace Pokemanager.Model.Projects;

/// <summary>Randomización del proyecto: preset de UPR ZX (embebido) y semilla.</summary>
public sealed class RandomizationSettings
{
    public bool Enabled { get; set; }

    /// <summary>Nombre para mostrar del preset (normalmente el nombre del .rnqs de origen).</summary>
    public string? PresetName { get; set; }

    /// <summary>Contenido del .rnqs. Pesa ~100 bytes: se guarda dentro del proyecto para poder compartirlo.</summary>
    public byte[]? Preset { get; set; }

    public long Seed { get; set; }

    /// <summary>Semilla de la última ROM creada, para avisar si se cambia con partida en curso.</summary>
    public long? InstalledSeed { get; set; }

    /// <summary>Nombre (sin extensión) de la ROM randomizada, que se crea junto a la ROM base.</summary>
    public string? OutputName { get; set; }

    /// <summary>Al crear la ROM, adaptar la partida del emulador (habilidades y stats del equipo) a ella.</summary>
    public bool UpdateSave { get; set; } = true;

    /// <summary>Hay semilla. El preset puede faltar: entonces se usan los ajustes por defecto de UPR ZX.</summary>
    public bool IsReady => Enabled && Seed > 0;
}

/// <summary>
/// Proyecto compartible: dónde está el volcado, el idioma, la carpeta de usuario del emulador, la
/// randomización y las ediciones manuales. No contiene ni un byte del juego.
/// </summary>
public sealed class Project
{
    public const int CurrentFormat = 2;

    /// <summary>Carpeta que contiene <c>romfs</c> y <c>exefs</c>.</summary>
    public required string DumpDirectory { get; set; }

    /// <summary>ROM descifrada (.3ds/.cxi) que usa el randomizer. Null: se busca en <see cref="DumpDirectory"/>.</summary>
    public string? RomFile { get; set; }

    public GameLanguage Language { get; set; } = GameLanguage.Spanish;

    /// <summary>Carpeta de usuario del emulador (la que contiene <c>load/mods</c>). Null si aún no se ha elegido.</summary>
    public string? EmulatorUserDirectory { get; set; }

    public RandomizationSettings Randomization { get; set; } = new();

    public EditSet Edits { get; } = new();

    public string RomFsPath => Path.Combine(DumpDirectory, "romfs");
    public string ExeFsPath => Path.Combine(DumpDirectory, "exefs");

    /// <summary><see cref="RomFile"/> o el primer .3ds/.cci/.cxi de la carpeta del volcado.</summary>
    public string? ResolveRomFile()
    {
        if (!string.IsNullOrWhiteSpace(RomFile))
            return File.Exists(RomFile) ? RomFile : null;
        if (!Directory.Exists(DumpDirectory))
            return null;
        return Directory.EnumerateFiles(DumpDirectory)
            .FirstOrDefault(f => Path.GetExtension(f).ToLowerInvariant() is ".3ds" or ".cci" or ".cxi");
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
            Edits.All.Select(e => new EditEntry(e.Table, e.Id, e.Field, e.Value)).ToList());

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(file, JsonOptions));
        File.Move(tmp, path, overwrite: true);
    }

    public static Project Load(string path)
    {
        var file = JsonSerializer.Deserialize<ProjectFile>(File.ReadAllText(path), JsonOptions)
                   ?? throw new InvalidDataException($"Proyecto vacío: {path}");
        if (file.Format > CurrentFormat)
            throw new InvalidDataException($"El proyecto usa el formato {file.Format}; esta versión solo entiende hasta el {CurrentFormat}.");

        var project = new Project
        {
            DumpDirectory = file.DumpDirectory,
            RomFile = file.RomFile,
            Language = file.Language,
            EmulatorUserDirectory = file.EmulatorUserDirectory,
            Randomization = file.Randomization ?? new RandomizationSettings(), // formato 1 no la tenía
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
        List<EditEntry>? Edits);

    private sealed record EditEntry(string Table, int Id, string Field, JsonNode Value);
}
