using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Pokemanager.Model.Dump;
using Pokemanager.Model.Edits;

namespace Pokemanager.Model.Projects;

/// <summary>
/// Proyecto compartible: dónde está el volcado, el idioma, la carpeta de usuario del emulador y las
/// ediciones manuales. No contiene ni un byte del juego.
/// </summary>
public sealed class Project
{
    public const int CurrentFormat = 1;

    /// <summary>Carpeta que contiene <c>romfs</c> y <c>exefs</c>.</summary>
    public required string DumpDirectory { get; set; }

    public GameLanguage Language { get; set; } = GameLanguage.Spanish;

    /// <summary>Carpeta de usuario del emulador (la que contiene <c>load/mods</c>). Null si aún no se ha elegido.</summary>
    public string? EmulatorUserDirectory { get; set; }

    public EditSet Edits { get; } = new();

    public string RomFsPath => Path.Combine(DumpDirectory, "romfs");
    public string ExeFsPath => Path.Combine(DumpDirectory, "exefs");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public void Save(string path)
    {
        var file = new ProjectFile(CurrentFormat, DumpDirectory, Language, EmulatorUserDirectory,
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
            Language = file.Language,
            EmulatorUserDirectory = file.EmulatorUserDirectory,
        };
        foreach (var e in file.Edits ?? [])
            project.Edits.Set(e.Table, e.Id, e.Field, e.Value);
        return project;
    }

    private sealed record ProjectFile(
        int Format,
        string DumpDirectory,
        GameLanguage Language,
        string? EmulatorUserDirectory,
        List<EditEntry>? Edits);

    private sealed record EditEntry(string Table, int Id, string Field, JsonNode Value);
}
