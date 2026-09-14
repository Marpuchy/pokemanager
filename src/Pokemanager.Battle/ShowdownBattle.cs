using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pokemanager.Battle;

/// <summary>Where the battle simulator is: Node.js and the <c>battle</c> folder (launcher + Pokémon Showdown).</summary>
public sealed record ShowdownTools(string Node, string Launcher)
{
    /// <summary>
    /// The bundled runtime and simulator next to the app (<c>tools/node/node.exe</c>, <c>battle/</c>), else Node on the PATH
    /// and a <c>battle</c> folder found upwards from the app (development).
    /// </summary>
    public static ShowdownTools? Locate(string? appDirectory = null)
    {
        appDirectory ??= AppContext.BaseDirectory;
        string? node = Path.Combine(appDirectory, "tools", "node", OperatingSystem.IsWindows() ? "node.exe" : "node") is var bundled && File.Exists(bundled)
            ? bundled
            : FindOnPath(OperatingSystem.IsWindows() ? "node.exe" : "node");
        string? launcher = null;
        for (var dir = new DirectoryInfo(appDirectory); dir is not null && launcher is null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "battle", "pokemanager-battle.js");
            if (File.Exists(candidate) && Directory.Exists(Path.Combine(dir.FullName, "battle", "node_modules", "pokemon-showdown")))
                launcher = candidate;
        }
        return node is not null && launcher is not null ? new ShowdownTools(node, launcher) : null;
    }

    private static string? FindOnPath(string exe) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
        .Select(p => Path.Combine(p.Trim(), exe)).FirstOrDefault(File.Exists);
}

/// <summary>A team that breaks the chosen rules, with Showdown's explanations ("Species Clause: …").</summary>
public sealed record InvalidTeam(string Side, IReadOnlyList<string> Problems);

/// <summary>A message from the simulator (see <c>battle/pokemanager-battle.js</c>).</summary>
/// <param name="Type">update, side, end, invalid (the battle does not start) or error.</param>
public sealed record ShowdownMessage(string Type, string? Side, IReadOnlyList<string> Lines, string? Winner, string? Message,
    IReadOnlyList<InvalidTeam>? Teams = null);

/// <summary>
/// One battle in a Pokémon Showdown simulator process. The host of a room runs it; players' decisions go in with
/// <see cref="Choose"/> and what happens comes out through <see cref="Received"/> (raised on a worker thread), in
/// Showdown's protocol (<c>|move|p1a: Charizard|Flamethrower|p2a: …</c>).
/// </summary>
public sealed class ShowdownBattle : IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly Process process;
    private readonly Task reader;
    private readonly object writeLock = new();

    public event Action<ShowdownMessage>? Received;

    /// <summary>The simulator exited (end of the battle or an error).</summary>
    public Task Exited { get; }

    private ShowdownBattle(Process process)
    {
        this.process = process;
        reader = Task.Run(ReadAsync);
        Exited = process.WaitForExitAsync();
    }

    /// <exception cref="InvalidOperationException">The simulator could not be started.</exception>
    public static ShowdownBattle Start(ShowdownTools tools, BattleRules rules, BattleTeam p1, BattleTeam p2, int[]? seed = null)
    {
        var info = new ProcessStartInfo(tools.Node)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardInputEncoding = new System.Text.UTF8Encoding(false),
            WorkingDirectory = Path.GetDirectoryName(tools.Launcher)!,
        };
        info.ArgumentList.Add(tools.Launcher);
        var process = Process.Start(info) ?? throw new InvalidOperationException("The battle simulator could not be started.");
        var battle = new ShowdownBattle(process);
        battle.Send(new { type = "start", rules, players = new[] { p1, p2 }, seed });
        return battle;
    }

    /// <summary>A player's decision in Showdown's syntax: "move 1", "switch 3", "move 2 mega", "team 123456"…</summary>
    public void Choose(string side, string choice) => Send(new { type = "choose", side, choice });

    public void Forfeit(string side) => Send(new { type = "forfeit", side });

    private void Send(object message)
    {
        lock (writeLock)
        {
            if (process.HasExited)
                return;
            process.StandardInput.WriteLine(JsonSerializer.Serialize(message, Json));
            process.StandardInput.Flush();
        }
    }

    private async Task ReadAsync()
    {
        while (await process.StandardOutput.ReadLineAsync() is { } line)
        {
            ShowdownMessage? message;
            try
            {
                message = JsonSerializer.Deserialize<ShowdownMessage>(line, Json);
            }
            catch (JsonException)
            {
                continue;
            }
            if (message is not null)
                Received?.Invoke(message with { Lines = message.Lines ?? [] });
        }
        string error = await process.StandardError.ReadToEndAsync();
        if (error.Trim().Length > 0)
            Received?.Invoke(new ShowdownMessage("error", null, [], null, error.Trim()));
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!process.HasExited)
            {
                process.StandardInput.Close();
                if (!process.WaitForExit(2000))
                    process.Kill(entireProcessTree: true);
            }
            await reader;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
        }
        process.Dispose();
    }
}
