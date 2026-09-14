using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pokemanager.Multiplayer;

/// <summary>What travels between the apps of a room. The host is the hub: guests only talk to it.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(Hello), "hello")]
[JsonDerivedType(typeof(Welcome), "welcome")]
[JsonDerivedType(typeof(PlayerJoined), "joined")]
[JsonDerivedType(typeof(PlayerLeft), "left")]
[JsonDerivedType(typeof(SnapshotShared), "snapshot")]
[JsonDerivedType(typeof(RulesChanged), "rules")]
[JsonDerivedType(typeof(ProfileChanged), "profile")]
public abstract record RoomMessage;

/// <summary>Guest → host, right after connecting.</summary>
public sealed record Hello(int Protocol, string PlayerId, PlayerProfile Profile) : RoomMessage;

/// <summary>Host → a guest that said hello: the room and who is in it (their snapshots follow).</summary>
public sealed record Welcome(string RoomName, string HostPlayerId, RoomRules Rules, List<PlayerJoined> Players) : RoomMessage;

public sealed record PlayerJoined(string PlayerId, PlayerProfile Profile) : RoomMessage;

public sealed record PlayerLeft(string PlayerId) : RoomMessage;

public sealed record SnapshotShared(string PlayerId, TrainerSnapshot Snapshot) : RoomMessage;

public sealed record RulesChanged(RoomRules Rules) : RoomMessage;

/// <summary>A player changed their name, sprite or color (guest → host → everyone).</summary>
public sealed record ProfileChanged(string PlayerId, PlayerProfile Profile) : RoomMessage;

/// <summary>
/// Turns messages into bytes and back: JSON, Brotli, then AES-GCM with a key derived from the room secret, so only
/// someone with the invite code can read or forge them.
/// </summary>
public sealed class MessageCodec(byte[] secret)
{
    public const int Protocol = 2;

    private readonly byte[] key = HKDF.DeriveKey(HashAlgorithmName.SHA256, secret, 32, "pokemanager-room"u8.ToArray(), "encryption"u8.ToArray());

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Sent in the clear with the connection request: lets the host refuse strangers before any work.</summary>
    public static string ConnectionKey(byte[] secret) => Convert.ToHexString(
        HKDF.DeriveKey(HashAlgorithmName.SHA256, secret, 16, "pokemanager-room"u8.ToArray(), "connection"u8.ToArray()));

    public byte[] Encode(RoomMessage message)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(message, Json);
        using var compressed = new MemoryStream();
        using (var brotli = new BrotliStream(compressed, CompressionLevel.Fastest))
            brotli.Write(json);
        byte[] plain = compressed.ToArray();

        byte[] packet = new byte[12 + 16 + plain.Length];
        var nonce = packet.AsSpan(0, 12);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, plain, packet.AsSpan(28), packet.AsSpan(12, 16));
        return packet;
    }

    /// <exception cref="InvalidDataException">Damaged, forged or not a message.</exception>
    public RoomMessage Decode(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < 28)
            throw new InvalidDataException("Message too short.");
        byte[] plain = new byte[packet.Length - 28];
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(packet[..12], packet[28..], packet.Slice(12, 16), plain);
            using var brotli = new BrotliStream(new MemoryStream(plain), CompressionMode.Decompress);
            return JsonSerializer.Deserialize<RoomMessage>(brotli, Json) ?? throw new InvalidDataException("Empty message.");
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or NotSupportedException)
        {
            throw new InvalidDataException("Unreadable message.", ex);
        }
    }
}
