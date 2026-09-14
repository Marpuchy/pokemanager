using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Pokemanager.Randomizer;

/// <summary>Java and the Universal Pokémon Randomizer ZX jar, ready to use.</summary>
public sealed record UprTools(string JavaPath, int JavaMajorVersion, string JarPath);

/// <summary>
/// Locates Java (11 or later) and the UPR ZX bundled with the app. The installer ships a trimmed Java runtime in
/// <c>&lt;app&gt;/tools/java</c>, which is preferred; a development build uses the Java installed on the system.
/// </summary>
public static partial class UprLocator
{
    public const int MinimumJava = 11;

    /// <summary>UPR ZX bundled with the application: <c>&lt;app&gt;/tools/upr/PokeRandoZX.jar</c>.</summary>
    public static string BundledJar => Path.Combine(AppContext.BaseDirectory, "tools", "upr", "PokeRandoZX.jar");

    /// <summary>A usable Java and the bundled jar, or null if either is missing.</summary>
    public static UprTools? Find() =>
        FindJava() is { } java && File.Exists(BundledJar) ? new UprTools(java.Path, java.Major, BundledJar) : null;

    /// <summary>Java runtime bundled by the installer (<c>jlink</c>): <c>&lt;app&gt;/tools/java/bin/java</c>.</summary>
    public static string BundledJava => Path.Combine(AppContext.BaseDirectory, "tools", "java", "bin", OperatingSystem.IsWindows() ? "java.exe" : "java");

    /// <summary>
    /// Looks for a usable Java: <c>JAVA_HOME</c>, installed JDKs and <c>java</c> on the PATH. Returns the highest version
    /// that meets the minimum, or null.
    /// </summary>
    public static (string Path, int Major)? FindJava()
    {
        string exe = OperatingSystem.IsWindows() ? "java.exe" : "java";
        if (File.Exists(BundledJava) && ReadJavaMajor(BundledJava) is var bundled and >= MinimumJava)
            return (BundledJava, bundled);
        var candidates = new List<string>();

        if (Environment.GetEnvironmentVariable("JAVA_HOME") is { Length: > 0 } home)
            candidates.Add(Path.Combine(home, "bin", exe));

        if (OperatingSystem.IsWindows())
        {
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            foreach (string vendor in new[] { "Java", "Eclipse Adoptium", "Microsoft", "Zulu", "BellSoft" })
            {
                string dir = Path.Combine(programFiles, vendor);
                if (Directory.Exists(dir))
                    candidates.AddRange(Directory.GetDirectories(dir).Select(d => Path.Combine(d, "bin", exe)));
            }
        }

        foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            candidates.Add(Path.Combine(dir, exe));

        return candidates
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(p => (Path: p, Major: ReadJavaMajor(p)))
            .Where(j => j.Major >= MinimumJava)
            .OrderByDescending(j => j.Major)
            .Select(j => ((string Path, int Major)?)j)
            .FirstOrDefault();
    }

    internal static int ReadJavaMajor(string javaPath)
    {
        try
        {
            var psi = new ProcessStartInfo(javaPath, "-version")
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi)!;
            string text = p.StandardError.ReadToEnd() + p.StandardOutput.ReadToEnd();
            p.WaitForExit(15000);
            return ParseJavaMajor(text);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return 0;
        }
    }

    /// <summary><c>version "1.8.0_481"</c> → 8; <c>version "25.0.2"</c> → 25.</summary>
    internal static int ParseJavaMajor(string versionOutput)
    {
        var m = VersionRegex().Match(versionOutput);
        if (!m.Success)
            return 0;
        int first = int.Parse(m.Groups[1].Value);
        return first == 1 && m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : first;
    }

    [GeneratedRegex("version \"(\\d+)(?:\\.(\\d+))?")]
    private static partial Regex VersionRegex();
}
