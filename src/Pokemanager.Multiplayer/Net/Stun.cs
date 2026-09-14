using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace Pokemanager.Multiplayer.Net;

/// <summary>
/// Asks a public STUN server (RFC 5389 binding request) which address and port the internet sees for a local UDP port.
/// Most home routers keep that mapping for any destination, so other players can reach the port there.
/// </summary>
public static class Stun
{
    public static readonly string[] DefaultServers = ["stun.l.google.com:19302", "stun.cloudflare.com:3478", "stun1.l.google.com:19302"];

    private const uint MagicCookie = 0x2112A442;

    /// <summary>The public IPv4 endpoint of <paramref name="socket"/>, or null when no server answered.</summary>
    public static async Task<IPEndPoint?> QueryAsync(UdpClient socket, IEnumerable<string> servers, TimeSpan timeout, CancellationToken cancel = default)
    {
        foreach (string server in servers)
        {
            int colon = server.LastIndexOf(':');
            string host = colon > 0 ? server[..colon] : server;
            int port = colon > 0 && int.TryParse(server[(colon + 1)..], out int p) ? p : 3478;
            IPAddress? address;
            try
            {
                address = (await Dns.GetHostAddressesAsync(host, AddressFamily.InterNetwork, cancel)).FirstOrDefault();
            }
            catch (SocketException)
            {
                continue;
            }
            if (address is null)
                continue;

            byte[] transaction = RandomNumberGenerator.GetBytes(12);
            byte[] request = new byte[20];
            request[1] = 0x01; // binding request
            WriteUInt32(request, 4, MagicCookie);
            transaction.CopyTo(request, 8);

            for (int attempt = 0; attempt < 2; attempt++)
            {
                await socket.SendAsync(request, new IPEndPoint(address, port), cancel);
                using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancel);
                wait.CancelAfter(timeout);
                try
                {
                    while (true)
                    {
                        var result = await socket.ReceiveAsync(wait.Token);
                        if (Parse(result.Buffer, transaction) is { } mapped)
                            return mapped;
                    }
                }
                catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
                {
                }
                catch (SocketException)
                {
                    break;
                }
            }
        }
        return null;
    }

    /// <summary>The (XOR-)MAPPED-ADDRESS of a binding success response for our transaction.</summary>
    internal static IPEndPoint? Parse(byte[] data, byte[] transaction)
    {
        if (data.Length < 20 || data[0] != 0x01 || data[1] != 0x01 || ReadUInt32(data, 4) != MagicCookie
            || !data.AsSpan(8, 12).SequenceEqual(transaction))
            return null;
        IPEndPoint? plain = null;
        int pos = 20, end = Math.Min(data.Length, 20 + ((data[2] << 8) | data[3]));
        while (pos + 4 <= end)
        {
            int type = (data[pos] << 8) | data[pos + 1], length = (data[pos + 2] << 8) | data[pos + 3];
            int value = pos + 4;
            if (value + length > end)
                break;
            if ((type is 0x0020 or 0x0001) && length >= 8 && data[value + 1] == 0x01)
            {
                int port = (data[value + 2] << 8) | data[value + 3];
                byte[] ip = data.AsSpan(value + 4, 4).ToArray();
                if (type == 0x0020)
                {
                    port ^= (int)(MagicCookie >> 16);
                    for (int i = 0; i < 4; i++)
                        ip[i] ^= (byte)(MagicCookie >> (24 - (8 * i)));
                    return new IPEndPoint(new IPAddress(ip), port);
                }
                plain = new IPEndPoint(new IPAddress(ip), port);
            }
            pos = value + ((length + 3) & ~3);
        }
        return plain;
    }

    private static void WriteUInt32(byte[] b, int offset, uint value)
    {
        b[offset] = (byte)(value >> 24);
        b[offset + 1] = (byte)(value >> 16);
        b[offset + 2] = (byte)(value >> 8);
        b[offset + 3] = (byte)value;
    }

    private static uint ReadUInt32(byte[] b, int offset) =>
        (uint)((b[offset] << 24) | (b[offset + 1] << 16) | (b[offset + 2] << 8) | b[offset + 3]);
}
