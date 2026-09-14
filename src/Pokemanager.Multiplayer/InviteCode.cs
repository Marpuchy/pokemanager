using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace Pokemanager.Multiplayer;

/// <summary>
/// The text players pass around to connect, with no server in between: the addresses where an app can be reached and,
/// in an invite, the room secret.
/// <list type="bullet">
/// <item>Invite (<c>PM-…</c>): the host's addresses + the secret. Enough to join when the host is reachable.</item>
/// <item>Answer (<c>PMR-…</c>): a guest's addresses + a tag of the room, for when routers block the direct way: the host
/// pastes it and both sides open a path at once (hole punching).</item>
/// </list>
/// Binary layout: version, kind, secret (10 bytes) or room tag (4 bytes), endpoint count, endpoints (family, address,
/// port), 2-byte checksum. Written in Crockford base32 in groups of five, so it survives chats and typing.
/// </summary>
public sealed record InviteCode(bool IsAnswer, byte[] Key, IReadOnlyList<IPEndPoint> Endpoints)
{
    private const byte Version = 1;
    public const int SecretLength = 10;
    public const int TagLength = 4;
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public static byte[] NewSecret() => RandomNumberGenerator.GetBytes(SecretLength);

    /// <summary>Identifies the room in answers without giving away its secret.</summary>
    public static byte[] RoomTag(byte[] secret) => SHA256.HashData(secret)[..TagLength];

    public static InviteCode Invite(byte[] secret, IEnumerable<IPEndPoint> endpoints) => new(false, secret, endpoints.ToList());

    public static InviteCode Answer(byte[] secret, IEnumerable<IPEndPoint> endpoints) => new(true, RoomTag(secret), endpoints.ToList());

    public override string ToString()
    {
        var bytes = new List<byte> { Version, (byte)(IsAnswer ? 1 : 0) };
        bytes.AddRange(Key);
        var endpoints = Endpoints.Take(255).ToList();
        bytes.Add((byte)endpoints.Count);
        foreach (var ep in endpoints)
        {
            byte[] address = ep.Address.IsIPv4MappedToIPv6 ? ep.Address.MapToIPv4().GetAddressBytes() : ep.Address.GetAddressBytes();
            bytes.Add((byte)address.Length);
            bytes.AddRange(address);
            bytes.Add((byte)(ep.Port >> 8));
            bytes.Add((byte)ep.Port);
        }
        byte[] sum = SHA256.HashData(bytes.ToArray());
        bytes.Add(sum[0]);
        bytes.Add(sum[1]);

        string text = Base32(bytes.ToArray());
        var grouped = new StringBuilder(IsAnswer ? "PMR" : "PM");
        for (int i = 0; i < text.Length; i += 5)
            grouped.Append('-').Append(text.AsSpan(i, Math.Min(5, text.Length - i)));
        return grouped.ToString();
    }

    /// <summary>Reads a code typed or pasted by a player (case, spaces, dashes and look-alike letters do not matter).</summary>
    /// <exception cref="FormatException">Not a code, or mistyped.</exception>
    public static InviteCode Parse(string text)
    {
        string clean = new(text.Trim().ToUpperInvariant().Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray());
        bool answer = clean.StartsWith("PMR", StringComparison.Ordinal);
        if (!answer && !clean.StartsWith("PM", StringComparison.Ordinal))
            throw new FormatException("Not a Pokemanager code.");
        byte[] bytes = FromBase32(clean[(answer ? 3 : 2)..]);

        if (bytes.Length < 6 || !SHA256.HashData(bytes.AsSpan(0, bytes.Length - 2)).AsSpan(0, 2).SequenceEqual(bytes.AsSpan(bytes.Length - 2)))
            throw new FormatException("The code is incomplete or mistyped.");
        int pos = 0;
        if (bytes[pos++] != Version)
            throw new FormatException("The code comes from another version of Pokemanager.");
        bool isAnswer = bytes[pos++] == 1;
        if (isAnswer != answer)
            throw new FormatException("The code is incomplete or mistyped.");
        int keyLength = isAnswer ? TagLength : SecretLength;
        byte[] key = bytes.AsSpan(pos, keyLength).ToArray();
        pos += keyLength;
        int count = bytes[pos++];
        var endpoints = new List<IPEndPoint>();
        for (int i = 0; i < count; i++)
        {
            int length = bytes[pos++];
            if (length is not (4 or 16) || pos + length + 2 > bytes.Length - 2)
                throw new FormatException("The code is incomplete or mistyped.");
            var address = new IPAddress(bytes.AsSpan(pos, length));
            pos += length;
            int port = (bytes[pos] << 8) | bytes[pos + 1];
            pos += 2;
            endpoints.Add(new IPEndPoint(address, port));
        }
        return new InviteCode(isAnswer, key, endpoints);
    }

    private static string Base32(byte[] data)
    {
        var sb = new StringBuilder();
        int buffer = 0, bits = 0;
        foreach (byte b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                sb.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }
        if (bits > 0)
            sb.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        return sb.ToString();
    }

    private static byte[] FromBase32(string text)
    {
        var bytes = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (char raw in text)
        {
            char c = raw switch { 'O' => '0', 'I' or 'L' => '1', 'U' => 'V', _ => raw };
            int value = Alphabet.IndexOf(c);
            if (value < 0)
                throw new FormatException("The code has characters that do not belong to it.");
            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                bytes.Add((byte)(buffer >> (bits - 8)));
                bits -= 8;
            }
        }
        return bytes.ToArray();
    }

    /// <summary>Whether an address family is worth trying from here.</summary>
    internal static bool Usable(IPEndPoint ep) =>
        ep.AddressFamily == AddressFamily.InterNetwork || (ep.AddressFamily == AddressFamily.InterNetworkV6 && Socket.OSSupportsIPv6);
}
