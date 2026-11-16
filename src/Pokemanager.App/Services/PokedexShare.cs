using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace Pokemanager.App.Services;

/// <summary>
/// The Pokédex page served on the home network, for the phone (2026-11-16, user: "in the app, a link to pass to the
/// phone"; chose the home Wi-Fi). A minimal HTTP server on a plain socket — <c>HttpListener</c> needs administrator
/// rights for anything but localhost on Windows — answering <c>/</c> with the page and <c>/stamp</c> with its version, so
/// the page reloads itself when it changes (the game saved). It lives with the application, whatever page is shown,
/// until stopped; Windows asks once whether to let it through the firewall.
/// </summary>
public sealed class PokedexShare : IDisposable
{
    public static PokedexShare? Current { get; private set; }

    /// <summary>The port tried first, so a bookmark on the phone keeps working between sessions.</summary>
    public const int PreferredPort = 8642;

    private readonly TcpListener listener;
    private readonly CancellationTokenSource stop = new();
    private byte[] page = [];
    private string stamp = "0";

    public string ProjectPath { get; }
    public int Port { get; }
    public string Url { get; }

    private PokedexShare(string projectPath, string html)
    {
        ProjectPath = projectPath;
        Update(html);
        try
        {
            listener = new TcpListener(IPAddress.Any, PreferredPort);
            listener.Start();
        }
        catch (SocketException)
        {
            listener = new TcpListener(IPAddress.Any, 0);
            listener.Start();
        }
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        Url = $"http://{LocalAddress()}:{Port}/";
        _ = Task.Run(AcceptLoop);
    }

    /// <summary>Starts serving this project's page (or swaps the page when it is already being served).</summary>
    public static PokedexShare Start(string projectPath, string html)
    {
        if (Current is { } running && string.Equals(running.ProjectPath, projectPath, StringComparison.OrdinalIgnoreCase))
        {
            running.Update(html);
            return running;
        }
        Current?.Dispose();
        return Current = new PokedexShare(projectPath, html);
    }

    public static void StopCurrent()
    {
        Current?.Dispose();
        Current = null;
    }

    public void Update(string html)
    {
        page = Encoding.UTF8.GetBytes(html);
        stamp = DateTime.UtcNow.Ticks.ToString();
    }

    /// <summary>The address the phone reaches this computer at: an IPv4 of a working network card, a home range first.</summary>
    public static string LocalAddress()
    {
        var candidates = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is not NetworkInterfaceType.Loopback
                        && !n.Description.Contains("virtual", StringComparison.OrdinalIgnoreCase)
                        && !n.Name.Contains("vEthernet", StringComparison.OrdinalIgnoreCase))
            .SelectMany(n => n.GetIPProperties().UnicastAddresses.Select(a => (n, a.Address)))
            .Where(p => p.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(p.Address))
            .Select(p => (Score: Score(p.Address) + (p.n.GetIPProperties().GatewayAddresses.Count > 0 ? 10 : 0), p.Address))
            .OrderByDescending(p => p.Score)
            .ToList();
        return candidates.Count > 0 ? candidates[0].Address.ToString() : "127.0.0.1";

        static int Score(IPAddress address)
        {
            byte[] b = address.GetAddressBytes();
            return b[0] == 192 && b[1] == 168 ? 30 : b[0] == 10 ? 20 : b[0] == 172 && b[1] >= 16 && b[1] <= 31 ? 15 : b[0] == 169 ? -10 : 0;
        }
    }

    private async Task AcceptLoop()
    {
        while (!stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(stop.Token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }
            _ = Task.Run(() => Serve(client));
        }
    }

    private async Task Serve(TcpClient client)
    {
        using (client)
        {
            try
            {
                client.ReceiveTimeout = 5000;
                var stream = client.GetStream();
                var buffer = new byte[4096];
                int read = await stream.ReadAsync(buffer).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
                string request = Encoding.ASCII.GetString(buffer, 0, read);
                string path = request.Split(' ') is { Length: > 1 } parts ? parts[1] : "/";
                (int status, string type, byte[] body) = path.Split('?')[0] switch
                {
                    "/" or "/index.html" => (200, "text/html; charset=utf-8", page),
                    "/stamp" => (200, "text/plain", Encoding.ASCII.GetBytes(stamp)),
                    _ => (404, "text/plain", Encoding.ASCII.GetBytes("Not found")),
                };
                string head = $"HTTP/1.1 {status} {(status == 200 ? "OK" : "Not Found")}\r\nContent-Type: {type}\r\nContent-Length: {body.Length}\r\n"
                              + "Cache-Control: no-store\r\nConnection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
                await stream.WriteAsync(body);
            }
            catch (Exception ex) when (ex is IOException or SocketException or TimeoutException or ObjectDisposedException)
            {
            }
        }
    }

    private Pokemanager.Multiplayer.SaveWatcher? watcher;

    /// <summary>Builds the page again whenever the game writes the save, so the phone follows what was seen and caught.</summary>
    public void Watch(string? savePath, Func<string> rebuild)
    {
        watcher?.Dispose();
        watcher = null;
        if (savePath is null || !File.Exists(savePath))
            return;
        watcher = new Pokemanager.Multiplayer.SaveWatcher(savePath);
        watcher.Saved += () => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            try
            {
                Update(rebuild());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
            }
        });
    }

    public void Dispose()
    {
        watcher?.Dispose();
        stop.Cancel();
        listener.Stop();
    }
}
