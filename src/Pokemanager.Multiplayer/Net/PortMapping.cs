using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Xml.Linq;

namespace Pokemanager.Multiplayer.Net;

/// <summary>A UDP port opened on the router, removed on dispose.</summary>
public sealed class PortMapping(string method, IPEndPoint external, Func<Task> remove) : IAsyncDisposable
{
    /// <summary>"UPnP" or "NAT-PMP".</summary>
    public string Method { get; } = method;

    /// <summary>Where the internet reaches the port. Private or carrier-grade addresses mean another NAT sits in front.</summary>
    public IPEndPoint External { get; } = external;

    public async ValueTask DisposeAsync()
    {
        try { await remove(); }
        catch (Exception ex) when (ex is HttpRequestException or SocketException or TaskCanceledException or IOException) { }
    }

    /// <summary>Asks the router to forward <paramref name="port"/> (UDP) to this computer: UPnP first, then NAT-PMP.</summary>
    public static async Task<PortMapping?> OpenAsync(int port, TimeSpan timeout, CancellationToken cancel = default)
    {
        try
        {
            if (await Upnp.MapAsync(port, timeout, cancel) is { } upnp)
                return upnp;
        }
        catch (Exception ex) when (ex is HttpRequestException or SocketException or TaskCanceledException or IOException
                                       or System.Xml.XmlException or FormatException)
        {
        }
        try
        {
            return await NatPmp.MapAsync(port, timeout, cancel);
        }
        catch (Exception ex) when (ex is SocketException or TaskCanceledException or OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>The default IPv4 gateways of the interfaces that are up.</summary>
    internal static IEnumerable<(IPAddress Gateway, IPAddress Local)> Gateways() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .Select(n => n.GetIPProperties())
            .SelectMany(p => p.GatewayAddresses.Where(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any))
                .SelectMany(g => p.UnicastAddresses.Where(u => u.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(u => (g.Address, u.Address))));
}

/// <summary>UPnP Internet Gateway Device: SSDP discovery, then SOAP AddPortMapping / GetExternalIPAddress.</summary>
internal static class Upnp
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(4) };

    public static async Task<PortMapping?> MapAsync(int port, TimeSpan timeout, CancellationToken cancel)
    {
        foreach (var (location, local) in await DiscoverAsync(timeout, cancel))
        {
            if (await ControlUrlAsync(location, cancel) is not { } control)
                continue;
            // A lease, so a crash does not leave the port open for ever (the session renews it); some routers only accept 0.
            string Add(int lease) =>
                $"<NewRemoteHost></NewRemoteHost><NewExternalPort>{port}</NewExternalPort><NewProtocol>UDP</NewProtocol>" +
                $"<NewInternalPort>{port}</NewInternalPort><NewInternalClient>{local}</NewInternalClient><NewEnabled>1</NewEnabled>" +
                $"<NewPortMappingDescription>Pokemanager</NewPortMappingDescription><NewLeaseDuration>{lease}</NewLeaseDuration>";
            try
            {
                await SoapAsync(control.Url, control.Service, "AddPortMapping", Add(7200), cancel);
            }
            catch (HttpRequestException)
            {
                await SoapAsync(control.Url, control.Service, "AddPortMapping", Add(0), cancel);
            }
            var reply = await SoapAsync(control.Url, control.Service, "GetExternalIPAddress", "", cancel);
            string? ip = reply.Descendants().FirstOrDefault(e => e.Name.LocalName == "NewExternalIPAddress")?.Value;
            if (!IPAddress.TryParse(ip, out var external))
                continue;
            return new PortMapping("UPnP", new IPEndPoint(external, port), () => SoapAsync(control.Url, control.Service, "DeletePortMapping",
                $"<NewRemoteHost></NewRemoteHost><NewExternalPort>{port}</NewExternalPort><NewProtocol>UDP</NewProtocol>", CancellationToken.None));
        }
        return null;
    }

    /// <summary>Gateway description URLs and the local address that reaches each one.</summary>
    private static async Task<List<(Uri Location, IPAddress Local)>> DiscoverAsync(TimeSpan timeout, CancellationToken cancel)
    {
        var found = new List<(Uri, IPAddress)>();
        foreach (var (_, local) in PortMapping.Gateways().DistinctBy(g => g.Local))
        {
            using var udp = new UdpClient(new IPEndPoint(local, 0));
            byte[] search = Encoding.ASCII.GetBytes(
                "M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nMAN: \"ssdp:discover\"\r\nMX: 2\r\n" +
                "ST: urn:schemas-upnp-org:device:InternetGatewayDevice:1\r\n\r\n");
            await udp.SendAsync(search, new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900), cancel);
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            wait.CancelAfter(timeout);
            try
            {
                while (true)
                {
                    var result = await udp.ReceiveAsync(wait.Token);
                    string text = Encoding.ASCII.GetString(result.Buffer);
                    string? line = text.Split("\r\n").FirstOrDefault(l => l.StartsWith("LOCATION:", StringComparison.OrdinalIgnoreCase));
                    if (line is not null && Uri.TryCreate(line[9..].Trim(), UriKind.Absolute, out var uri) && found.All(f => f.Item1 != uri))
                        found.Add((uri, local));
                }
            }
            catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
            {
            }
        }
        return found;
    }

    private static async Task<(Uri Url, string Service)?> ControlUrlAsync(Uri location, CancellationToken cancel)
    {
        var doc = XDocument.Parse(await Http.GetStringAsync(location, cancel));
        foreach (var service in doc.Descendants().Where(e => e.Name.LocalName == "service"))
        {
            string type = service.Elements().FirstOrDefault(e => e.Name.LocalName == "serviceType")?.Value ?? "";
            string? control = service.Elements().FirstOrDefault(e => e.Name.LocalName == "controlURL")?.Value;
            if (control is not null && (type.Contains(":WANIPConnection:", StringComparison.Ordinal) || type.Contains(":WANPPPConnection:", StringComparison.Ordinal)))
                return (new Uri(location, control), type);
        }
        return null;
    }

    private static async Task<XDocument> SoapAsync(Uri url, string service, string action, string arguments, CancellationToken cancel)
    {
        string body = "<?xml version=\"1.0\"?><s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" " +
                      "s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\"><s:Body>" +
                      $"<u:{action} xmlns:u=\"{service}\">{arguments}</u:{action}></s:Body></s:Envelope>";
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(body, Encoding.UTF8, "text/xml") };
        request.Headers.Add("SOAPAction", $"\"{service}#{action}\"");
        using var response = await Http.SendAsync(request, cancel);
        string text = await response.Content.ReadAsStringAsync(cancel);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"UPnP {action}: {(int)response.StatusCode}");
        return XDocument.Parse(text);
    }
}

/// <summary>NAT-PMP (RFC 6886): external address and a UDP mapping from the default gateway, port 5351.</summary>
internal static class NatPmp
{
    public static async Task<PortMapping?> MapAsync(int port, TimeSpan timeout, CancellationToken cancel)
    {
        foreach (var (gateway, _) in PortMapping.Gateways().DistinctBy(g => g.Gateway))
        {
            var target = new IPEndPoint(gateway, 5351);
            using var udp = new UdpClient(AddressFamily.InterNetwork);
            var address = await RequestAsync(udp, target, [0, 0], 12, timeout, cancel);
            if (address is null || address[3] != 0)
                continue;
            var external = new IPAddress(address.AsSpan(8, 4));

            var map = await RequestAsync(udp, target, Mapping(port, 7200), 16, timeout, cancel);
            if (map is null || map[3] != 0)
                continue;
            int externalPort = (map[10] << 8) | map[11];
            return new PortMapping("NAT-PMP", new IPEndPoint(external, externalPort),
                async () => await RequestAsync(udp, target, Mapping(port, 0), 16, timeout, CancellationToken.None));
        }
        return null;
    }

    private static byte[] Mapping(int port, uint lifetime) =>
        [0, 1, 0, 0, (byte)(port >> 8), (byte)port, (byte)(port >> 8), (byte)port,
         (byte)(lifetime >> 24), (byte)(lifetime >> 16), (byte)(lifetime >> 8), (byte)lifetime];

    private static async Task<byte[]?> RequestAsync(UdpClient udp, IPEndPoint target, byte[] request, int length, TimeSpan timeout, CancellationToken cancel)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            await udp.SendAsync(request, target, cancel);
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            wait.CancelAfter(timeout / 2);
            try
            {
                var result = await udp.ReceiveAsync(wait.Token);
                if (result.Buffer.Length >= length && result.Buffer[1] == request[1] + 128)
                    return result.Buffer;
            }
            catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
            {
            }
            catch (SocketException)
            {
                return null;
            }
        }
        return null;
    }
}
