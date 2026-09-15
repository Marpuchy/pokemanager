using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace Pokemanager.Bridge;

/// <summary>
/// The emulator's multiplayer room server (<c>citra-room</c> and forks) running on this computer, listening on a free UDP
/// port. Players reach it through the Pokemanager room; their emulators join with <see cref="LaunchInRoom"/>.
/// </summary>
public sealed partial class EmulatorRoomServer : IDisposable
{
    private static readonly string[] ServerPrograms = ["citra-room.exe", "azahar-room.exe", "lime3ds-room.exe", "citra-room", "azahar-room", "lime3ds-room"];

    /// <summary>Emulator programs that join a room from the command line (<c>-m nick:password@address:port</c>).</summary>
    private static readonly string[] JoinPrograms = ["citra.exe", "citra"];

    private readonly Process process;

    public string Program { get; }
    public int Port { get; }
    public string Password { get; }

    public bool IsRunning
    {
        get
        {
            try { return !process.HasExited; }
            catch (InvalidOperationException) { return false; }
        }
    }

    private EmulatorRoomServer(Process process, string program, int port, string password)
    {
        this.process = process;
        Program = program;
        Port = port;
        Password = password;
    }

    /// <summary>The room server next to one of the emulator programs, if any.</summary>
    public static string? LocateServer(IEnumerable<string?> emulatorPrograms) => Sibling(emulatorPrograms, ServerPrograms);

    /// <summary>The emulator program that can start already joined to a room (Citra's SDL program), if any.</summary>
    public static string? LocateJoinProgram(IEnumerable<string?> emulatorPrograms) => Sibling(emulatorPrograms, JoinPrograms);

    private static string? Sibling(IEnumerable<string?> programs, string[] names)
    {
        foreach (string? program in programs)
        {
            if (string.IsNullOrWhiteSpace(program) || Path.GetDirectoryName(program) is not { } dir)
                continue;
            foreach (string name in names)
            {
                string path = Path.Combine(dir, name);
                if (File.Exists(path))
                    return path;
            }
        }
        return null;
    }

    /// <param name="preferredGame">Game shown by the room (the server requires one).</param>
    /// <param name="preferredGameId">Title id of that game.</param>
    /// <param name="logFile">Where the server writes its log (otherwise next to the program).</param>
    /// <exception cref="InvalidOperationException">The server could not be started.</exception>
    public static EmulatorRoomServer Start(string program, string roomName, string preferredGame, ulong preferredGameId,
        string? logFile = null, int maxMembers = 8)
    {
        int port = FreeUdpPort();
        string password = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();
        var info = new ProcessStartInfo(program)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true, // it reads commands; kept open so it does not see the end of its input
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(program)!,
        };
        foreach (string arg in new[] { "--room-name", CleanName(roomName, "Pokemanager"), "--port", port.ToString(), "--password", password,
                                       "--max_members", maxMembers.ToString(), "--preferred-game", preferredGame,
                                       "--preferred-game-id", preferredGameId.ToString("X16") })
            info.ArgumentList.Add(arg);
        if (logFile is not null)
        {
            info.ArgumentList.Add("--log-file");
            info.ArgumentList.Add(logFile);
        }
        var process = Process.Start(info) ?? throw new InvalidOperationException("The emulator room server could not be started.");
        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived += (_, _) => { };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        if (process.WaitForExit(700))
            throw new InvalidOperationException($"The emulator room server stopped at once (exit code {process.ExitCode}).");
        return new EmulatorRoomServer(process, program, port, password);
    }

    /// <summary>
    /// Starts the emulator with the ROM, joined to a room at <paramref name="address"/>:<paramref name="port"/>.
    /// </summary>
    public static Process? LaunchInRoom(string joinProgram, string romPath, string nickname, string password, string address, int port) =>
        Process.Start(new ProcessStartInfo(joinProgram)
        {
            ArgumentList = { "-m", $"{CleanName(nickname, "Trainer")}:{password}@{address}:{port}", romPath },
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(joinProgram)!,
        });

    /// <summary>What the emulator accepts as a nickname or room name: letters, digits, spaces, dots, dashes; 4 to 20 characters.</summary>
    public static string CleanName(string name, string fallback)
    {
        // "Ibáñez" → "Ibanez": accents dropped, not the letters.
        string plain = new(name.Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());
        string clean = NotAllowed().Replace(plain, "").Trim();
        if (clean.Length < 4)
            clean = (clean + " " + fallback).Trim();
        return clean.Length > 20 ? clean[..20].Trim() : clean;
    }

    [GeneratedRegex(@"[^A-Za-z0-9 ._\-]")]
    private static partial Regex NotAllowed();

    private static int FreeUdpPort()
    {
        using var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)probe.Client.LocalEndPoint!).Port;
    }

    public void Dispose()
    {
        try
        {
            if (!process.HasExited)
            {
                try
                {
                    process.StandardInput.WriteLine("/exit");
                    process.StandardInput.Flush();
                }
                catch (IOException)
                {
                }
                if (!process.WaitForExit(1500))
                    process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
        process.Dispose();
    }
}
