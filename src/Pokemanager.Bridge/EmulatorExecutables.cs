using System.Diagnostics;

namespace Pokemanager.Bridge;

/// <summary>An emulator program found on this machine.</summary>
public sealed record EmulatorExecutable(string Name, string Path);

/// <summary>Finds the emulator program (Citra, Azahar and forks) and starts it with a ROM.</summary>
public static class EmulatorExecutables
{
    /// <summary>Program file names, preferred first. The Qt front ends accept a ROM path as argument.</summary>
    private static readonly (string File, string Name)[] Programs =
    [
        ("azahar.exe", "Azahar"), ("azahar-qt.exe", "Azahar"), ("azahar", "Azahar"),
        ("citra-qt.exe", "Citra"), ("citra-qt", "Citra"), ("lime3ds-gui.exe", "Lime3DS"), ("lime3ds", "Lime3DS"),
        ("citra.exe", "Citra"), ("citra", "Citra"),
    ];

    /// <summary>
    /// Emulator programs found: running ones first, then in the usual install folders and near <paramref name="hints"/>
    /// (for example the ROM folder: portable emulators are often unpacked next to the games).
    /// </summary>
    public static IReadOnlyList<EmulatorExecutable> Detect(IEnumerable<string?> hints)
    {
        var found = new List<EmulatorExecutable>();

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (Programs.Any(p => System.IO.Path.GetFileNameWithoutExtension(p.File).Equals(process.ProcessName, StringComparison.OrdinalIgnoreCase))
                    && process.MainModule?.FileName is { } file)
                    Add(found, file);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
            {
                // Processes of other users or already exited.
            }
        }

        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var folders = new List<string>
        {
            System.IO.Path.Combine(local, "Citra"), System.IO.Path.Combine(local, "Azahar"),
            System.IO.Path.Combine(local, "Programs", "Azahar"), System.IO.Path.Combine(local, "Programs", "Citra"),
            System.IO.Path.Combine(programFiles, "Azahar"), System.IO.Path.Combine(programFiles, "Citra"),
            System.IO.Path.Combine(programFiles, "Lime3DS"), "/usr/bin", "/usr/local/bin", "/Applications",
        };
        foreach (string? hint in hints)
        {
            if (string.IsNullOrWhiteSpace(hint))
                continue;
            // The folder itself, its parent and grandparent (e.g. D:\citra\roms → D:\citra\citra-windows-msvc-…).
            DirectoryInfo? dir;
            try
            {
                dir = new DirectoryInfo(File.Exists(hint) ? System.IO.Path.GetDirectoryName(hint)! : hint);
            }
            catch (Exception ex) when (ex is ArgumentException or System.Security.SecurityException or PathTooLongException)
            {
                continue;
            }
            // Never a drive root: searching a whole disk would be slow.
            for (int up = 0; up < 3 && dir?.Parent is not null; up++, dir = dir.Parent)
                folders.Add(dir.FullName);
        }

        foreach (string folder in folders.Distinct(StringComparer.OrdinalIgnoreCase))
            Search(found, folder, depth: 1);

        return found
            .DistinctBy(e => e.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void Search(List<EmulatorExecutable> found, string folder, int depth)
    {
        if (!Directory.Exists(folder))
            return;
        try
        {
            foreach (var (file, _) in Programs)
            {
                string path = System.IO.Path.Combine(folder, file);
                if (File.Exists(path))
                    Add(found, path);
            }
            if (depth > 0)
                foreach (string sub in Directory.EnumerateDirectories(folder).Take(150))
                    Search(found, sub, depth - 1);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
        }
    }

    private static void Add(List<EmulatorExecutable> found, string path)
    {
        string file = System.IO.Path.GetFileName(path);
        var program = Programs.FirstOrDefault(p => p.File.Equals(file, StringComparison.OrdinalIgnoreCase));
        found.Add(new EmulatorExecutable(program.Name ?? System.IO.Path.GetFileNameWithoutExtension(path), System.IO.Path.GetFullPath(path)));
    }

    /// <summary>
    /// Folders the emulator itself remembers in <c>config/qt-config.ini</c> — game directories, ROMs path and recent
    /// files. Portable emulators are usually unpacked near them, so they are good places to look for the program.
    /// </summary>
    public static IReadOnlyList<string> ConfigHints(string? userDirectory)
    {
        if (userDirectory is null)
            return [];
        string config = System.IO.Path.Combine(userDirectory, "config", "qt-config.ini");
        if (!File.Exists(config))
            return [];

        var hints = new List<string>();
        try
        {
            foreach (string line in File.ReadLines(config))
            {
                int eq = line.IndexOf('=');
                if (eq < 0)
                    continue;
                string key = line[..eq], value = line[(eq + 1)..].Trim();
                if (key.StartsWith(@"Paths\gamedirs\", StringComparison.OrdinalIgnoreCase) && key.EndsWith(@"\path", StringComparison.OrdinalIgnoreCase)
                    || key.Equals(@"Paths\romsPath", StringComparison.OrdinalIgnoreCase))
                    hints.Add(value);
                else if (key.Equals(@"Paths\recentFiles", StringComparison.OrdinalIgnoreCase))
                    hints.AddRange(value.Split(',').Select(v => v.Trim().Trim('"')));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return hints
            .Where(h => h.Length > 2 && (h.Contains('/') || h.Contains('\\')))
            .Select(h => h.Replace('/', System.IO.Path.DirectorySeparatorChar))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Name to show for a program path.</summary>
    public static string NameOf(string path)
    {
        string file = System.IO.Path.GetFileName(path);
        return Programs.FirstOrDefault(p => p.File.Equals(file, StringComparison.OrdinalIgnoreCase)).Name
               ?? System.IO.Path.GetFileNameWithoutExtension(path);
    }

    /// <summary>Starts the emulator with the ROM loaded.</summary>
    public static Process? Launch(string executable, string romPath) =>
        Process.Start(new ProcessStartInfo(executable)
        {
            ArgumentList = { romPath },
            UseShellExecute = false,
            WorkingDirectory = System.IO.Path.GetDirectoryName(executable)!,
        });
}
