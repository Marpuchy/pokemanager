using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Pokemanager.Battle;

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
[JsonDerivedType(typeof(BattleChallenge), "battle-challenge")]
[JsonDerivedType(typeof(BattleRulesSet), "battle-rules")]
[JsonDerivedType(typeof(BattleReady), "battle-ready")]
[JsonDerivedType(typeof(BattleCancel), "battle-cancel")]
[JsonDerivedType(typeof(BattleChoice), "battle-choice")]
[JsonDerivedType(typeof(BattleState), "battle-state")]
[JsonDerivedType(typeof(BattleLog), "battle-log")]
[JsonDerivedType(typeof(BattleRequest), "battle-request")]
[JsonDerivedType(typeof(EmulatorRoomOpened), "emulator-room")]
[JsonDerivedType(typeof(EmulatorRoomClosed), "emulator-room-closed")]
public abstract record RoomMessage;

/// <summary>Guest → host, right after connecting.</summary>
public sealed record Hello(int Protocol, string PlayerId, PlayerProfile Profile) : RoomMessage;

/// <summary>Host → a guest that said hello: the room and who is in it (their snapshots follow).</summary>
public sealed record Welcome(string RoomName, string HostPlayerId, RoomRules Rules, List<PlayerJoined> Players) : RoomMessage;

public sealed record PlayerJoined(string PlayerId, PlayerProfile Profile) : RoomMessage;

public sealed record PlayerLeft(string PlayerId) : RoomMessage;

public sealed record SnapshotShared(string PlayerId, TrainerSnapshot Snapshot) : RoomMessage;

public sealed record RulesChanged(RoomRules Rules) : RoomMessage;

// ------------------------------------------------------------------ battles (the host runs the simulator)

[JsonConverter(typeof(JsonStringEnumConverter<BattlePhase>))]
public enum BattlePhase
{
    /// <summary>Challenged: the two players agree on the rules and get ready.</summary>
    Proposed,
    Running,
    Finished,
    Cancelled,
}

/// <summary>Player → host: challenge another player with a first proposal of rules.</summary>
public sealed record BattleChallenge(string BattleId, string OpponentId, BattleRules Rules) : RoomMessage;

/// <summary>Player → host: either player changes the rules of a proposed battle (both have to get ready again).</summary>
public sealed record BattleRulesSet(string BattleId, BattleRules Rules) : RoomMessage;

/// <summary>Player → host: ready with this team (their party), or no longer ready (null).</summary>
public sealed record BattleReady(string BattleId, BattleTeam? Team) : RoomMessage;

/// <summary>Player → host: cancel a proposed battle, or forfeit a running one.</summary>
public sealed record BattleCancel(string BattleId) : RoomMessage;

/// <summary>Player → host: a decision in Showdown's syntax ("move 1", "switch 3", "team 123456"…).</summary>
public sealed record BattleChoice(string BattleId, string Choice) : RoomMessage;

/// <summary>Host → the two players: where the battle stands.</summary>
/// <param name="Winner">Player id of the winner; null for a tie or while it is not over.</param>
/// <param name="Problems">Why it did not start or was cancelled (rules broken by a team, no simulator…).</param>
public sealed record BattleState(string BattleId, string ChallengerId, string OpponentId, BattleRules Rules, List<string> Ready,
    BattlePhase Phase, string? Winner, List<string>? Problems) : RoomMessage;

/// <summary>Host → a player: new battle protocol lines, already reduced to what that player may see.</summary>
public sealed record BattleLog(string BattleId, List<string> Lines) : RoomMessage;

/// <summary>Host → a player: what they have to decide now (Showdown's request JSON).</summary>
public sealed record BattleRequest(string BattleId, string Request) : RoomMessage;

/// <summary>A player changed their name, sprite or color (guest → host → everyone).</summary>
public sealed record ProfileChanged(string PlayerId, PlayerProfile Profile) : RoomMessage;

// ------------------------------------------------------------------ emulator room (in-game link through the room)

/// <summary>An emulator multiplayer room (Citra's room server) run by the host, reached through the Pokemanager room.</summary>
/// <param name="Name">Room name shown in the emulator.</param>
/// <param name="Password">Password of the emulator room (it only listens on the host's computer, the tunnel is encrypted).</param>
/// <param name="Generation">Generation of the host's game: games of the same generation can link (X/Y with ORAS, SM with USUM).</param>
/// <param name="Game">The host's game (PKHeX version code), for display.</param>
public sealed record EmulatorRoomInfo(string Name, string Password, int Generation, string? Game);

/// <summary>Host → players: the emulator room is open; its traffic travels through this room on the tunnel channel.</summary>
public sealed record EmulatorRoomOpened(EmulatorRoomInfo Room) : RoomMessage;

/// <summary>Host → players: the emulator room was closed.</summary>
public sealed record EmulatorRoomClosed : RoomMessage;

/// <summary>
/// Turns messages into bytes and back: JSON, Brotli, then AES-GCM with a key derived from the room secret, so only
/// someone with the invite code can read or forge them.
/// </summary>
public sealed class MessageCodec(byte[] secret)
{
    public const int Protocol = 4;

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
        return Seal(compressed.ToArray());
    }

    /// <summary>Encrypts raw bytes (a tunnelled emulator packet) without JSON or compression.</summary>
    public byte[] Seal(ReadOnlySpan<byte> data)
    {
        byte[] packet = new byte[12 + 16 + data.Length];
        var nonce = packet.AsSpan(0, 12);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, data, packet.AsSpan(28), packet.AsSpan(12, 16));
        return packet;
    }

    /// <exception cref="InvalidDataException">Damaged or forged.</exception>
    public byte[] Open(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < 28)
            throw new InvalidDataException("Packet too short.");
        byte[] plain = new byte[packet.Length - 28];
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(packet[..12], packet[28..], packet.Slice(12, 16), plain);
            return plain;
        }
        catch (CryptographicException ex)
        {
            throw new InvalidDataException("Unreadable packet.", ex);
        }
    }

    /// <exception cref="InvalidDataException">Damaged, forged or not a message.</exception>
    public RoomMessage Decode(ReadOnlySpan<byte> packet)
    {
        byte[] plain = Open(packet);
        try
        {
            using var brotli = new BrotliStream(new MemoryStream(plain), CompressionMode.Decompress);
            return JsonSerializer.Deserialize<RoomMessage>(brotli, Json) ?? throw new InvalidDataException("Empty message.");
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            throw new InvalidDataException("Unreadable message.", ex);
        }
    }
}
