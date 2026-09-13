using System.Diagnostics;

namespace Pokemanager.Bridge;

/// <summary>Carpeta de usuario de un emulador de 3DS encontrada en esta máquina.</summary>
/// <param name="LastUsed">Última vez que el emulador guardó su configuración: indica cuál se usa de verdad.</param>
public sealed record EmulatorUserFolder(string Name, string Path, DateTime LastUsed);

/// <summary>
/// Localiza la carpeta de usuario del emulador y, dentro de ella, la carpeta de mods y la partida de un juego.
/// La estructura es la misma en los emuladores derivados de Citra.
/// </summary>
public static class EmulatorUserFolders
{
    /// <summary>Carpetas encontradas, la usada más recientemente primero.</summary>
    public static IReadOnlyList<EmulatorUserFolder> Detect()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME")
                          ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");

        (string Name, string Path)[] candidates =
        [
            ("Azahar", System.IO.Path.Combine(appData, "Azahar")),
            ("Azahar", System.IO.Path.Combine(dataHome, "azahar-emu")),
            ("Citra", System.IO.Path.Combine(appData, "Citra")),
            ("Citra", System.IO.Path.Combine(dataHome, "citra-emu")),
        ];

        return candidates
            .Where(c => Directory.Exists(c.Path))
            .DistinctBy(c => c.Path)
            .Select(c => new EmulatorUserFolder(c.Name, c.Path, LastUsed(c.Path)))
            .OrderByDescending(e => e.LastUsed)
            .ToList();
    }

    private static DateTime LastUsed(string userDirectory)
    {
        string config = System.IO.Path.Combine(userDirectory, "config", "qt-config.ini");
        return File.Exists(config) ? File.GetLastWriteTime(config) : Directory.GetLastWriteTime(userDirectory);
    }

    /// <summary><c>&lt;usuario&gt;/load/mods/&lt;TitleID&gt;</c>.</summary>
    public static string ModDirectory(string userDirectory, string titleIdHex) =>
        System.IO.Path.Combine(userDirectory, "load", "mods", titleIdHex);

    /// <summary>
    /// Archivo de guardado principal del juego en la SD emulada:
    /// <c>sdmc/Nintendo 3DS/&lt;32 ceros&gt;/&lt;32 ceros&gt;/title/&lt;TID alto&gt;/&lt;TID bajo&gt;/data/00000001/main</c>.
    /// </summary>
    public static string SaveFile(string userDirectory, ulong titleId)
    {
        const string zeros = "00000000000000000000000000000000";
        return System.IO.Path.Combine(userDirectory, "sdmc", "Nintendo 3DS", zeros, zeros, "title",
            (titleId >> 32).ToString("x8"), (titleId & 0xFFFFFFFF).ToString("x8"), "data", "00000001", "main");
    }

    /// <summary>
    /// Procesos abiertos del emulador indicado («Citra», «Azahar»); con otro nombre, de cualquiera de los dos.
    /// Escribir la partida con su emulador abierto no sirve: al salir la sobrescribe.
    /// </summary>
    public static IReadOnlyList<string> RunningEmulators(string? emulatorName = null)
    {
        string[] prefixes = emulatorName?.ToLowerInvariant() switch
        {
            "citra" => ["citra"],
            "azahar" => ["azahar"],
            _ => ["citra", "azahar"],
        };
        return Process.GetProcesses()
            .Select(p => { try { return p.ProcessName; } catch (InvalidOperationException) { return ""; } })
            .Where(n => prefixes.Any(prefix => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
