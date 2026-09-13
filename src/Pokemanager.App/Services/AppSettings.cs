using System.Text.Json;
using Pokemanager.Bridge;

namespace Pokemanager.App.Services;

/// <summary>Ajustes de Pokemanager en este equipo (no del proyecto).</summary>
public sealed class AppSettings
{
    public string? LastProject { get; set; }

    /// <summary>Carpeta de usuario del emulador elegida en Ajustes. Null: la del emulador usado más recientemente.</summary>
    public string? EmulatorUserDirectory { get; set; }

    /// <summary>Carpeta de usuario del emulador que se usa: la elegida o la detectada.</summary>
    public string? EffectiveEmulatorDirectory =>
        EmulatorUserDirectory is { } chosen && Directory.Exists(chosen)
            ? chosen
            : EmulatorUserFolders.Detect().FirstOrDefault()?.Path;

    /// <summary>Nombre para mostrar del emulador en uso («Citra», «Azahar» o la carpeta).</summary>
    public string EffectiveEmulatorName =>
        EffectiveEmulatorDirectory is not { } dir
            ? "ningún emulador"
            : EmulatorUserFolders.Detect().FirstOrDefault(e => string.Equals(e.Path, dir, StringComparison.OrdinalIgnoreCase))?.Name
              ?? Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar));

    public static string DataRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pokemanager");

    /// <summary>Copias de seguridad de partidas antes de modificarlas.</summary>
    public static string BackupRoot => Path.Combine(DataRoot, "copias-partida");

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pokemanager", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings()
                : new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Las preferencias son una comodidad: si no se pueden guardar, se sigue sin ellas.
        }
    }
}
