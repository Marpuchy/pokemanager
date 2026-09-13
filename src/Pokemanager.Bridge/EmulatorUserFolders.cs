using System.Diagnostics;

namespace Pokemanager.Bridge;

/// <summary>User folder of a 3DS emulator found on this machine.</summary>
/// <param name="LastUsed">Last time the emulator saved its configuration: tells which one is actually in use.</param>
public sealed record EmulatorUserFolder(string Name, string Path, DateTime LastUsed);

/// <summary>
/// Locates the emulator user folder and, inside it, a game's mods folder and save file. The layout is the same in
/// every Citra-derived emulator.
/// </summary>
public static class EmulatorUserFolders
{
    /// <summary>Folders found, most recently used first.</summary>
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

    /// <summary><c>&lt;user&gt;/load/mods/&lt;TitleID&gt;</c>.</summary>
    public static string ModDirectory(string userDirectory, string titleIdHex) =>
        System.IO.Path.Combine(userDirectory, "load", "mods", titleIdHex);

    /// <summary>
    /// Main save file of the game on the emulated SD card:
    /// <c>sdmc/Nintendo 3DS/&lt;32 zeros&gt;/&lt;32 zeros&gt;/title/&lt;TID high&gt;/&lt;TID low&gt;/data/00000001/main</c>.
    /// </summary>
    public static string SaveFile(string userDirectory, ulong titleId)
    {
        const string zeros = "00000000000000000000000000000000";
        return System.IO.Path.Combine(userDirectory, "sdmc", "Nintendo 3DS", zeros, zeros, "title",
            (titleId >> 32).ToString("x8"), (titleId & 0xFFFFFFFF).ToString("x8"), "data", "00000001", "main");
    }

    /// <summary>
    /// Running processes of the given emulator ("Citra", "Azahar"); for any other name, of either of them.
    /// Writing a save while its emulator is open is pointless: the emulator overwrites it when it exits.
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
