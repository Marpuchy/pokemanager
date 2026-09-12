namespace Pokemanager.Bridge;

/// <summary>Carpeta de usuario de un emulador de 3DS encontrada en esta máquina.</summary>
public sealed record EmulatorUserFolder(string Name, string Path);

/// <summary>
/// Localiza la carpeta de usuario del emulador y la carpeta de mods de un juego dentro de ella.
/// La estructura <c>load/mods/&lt;TitleID&gt;</c> es la misma en los emuladores derivados de Citra.
/// </summary>
public static class EmulatorUserFolders
{
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
            .Select(c => new EmulatorUserFolder(c.Name, c.Path))
            .ToList();
    }

    /// <summary><c>&lt;usuario&gt;/load/mods/&lt;TitleID&gt;</c>.</summary>
    public static string ModDirectory(string userDirectory, string titleIdHex) =>
        System.IO.Path.Combine(userDirectory, "load", "mods", titleIdHex);
}
