using System.Text.Json.Serialization;
using Pokemanager.Model.Projects;

namespace Pokemanager.Multiplayer;

/// <summary>
/// Something that happened to one player of a room. The room is an append-only log of these: every player applies the
/// same events in the same order and gets the same <see cref="RoomState"/>, which is what the network layer will
/// replicate. Events only carry facts; what they mean under the rules is decided when the state is built.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(PlayerJoined), "joined")]
[JsonDerivedType(typeof(PokemonCaptured), "captured")]
[JsonDerivedType(typeof(PokemonUpdated), "updated")]
[JsonDerivedType(typeof(PokemonDied), "died")]
[JsonDerivedType(typeof(DeathUndone), "undone")]
[JsonDerivedType(typeof(MilestoneEarned), "milestone")]
[JsonDerivedType(typeof(SaveSynced), "synced")]
[JsonDerivedType(typeof(LivesChanged), "lives")]
[JsonDerivedType(typeof(ManualLink), "link")]
[JsonDerivedType(typeof(ManualUnlink), "unlink")]
[JsonDerivedType(typeof(RouletteSpun), "roulette")]
public abstract record RoomEvent
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string PlayerId { get; init; }
    public DateTime When { get; init; }
}

public sealed record PlayerJoined(string Name, string Game, int Generation, int MilestoneCount) : RoomEvent;

/// <param name="Location">Met location id of the game (hatch location for eggs).</param>
/// <param name="LocationName">Location name in the reporter's language, for players on other games.</param>
/// <param name="Area">
/// Language-neutral area of the location: its English name without the sub-area in parentheses, so "Route 1 (Trainers'
/// School)" and "Route 1" are the same area for every player whatever their language. Empty when unknown.
/// </param>
public sealed record PokemonCaptured(
    uint Key, ushort Species, byte Form, string Nickname, int Level, int Location, string LocationName,
    bool WasEgg, bool IsShiny, int PrimaryType, string Area = "") : RoomEvent;

/// <summary>Evolved, renamed, levelled up or changed type (another ROM).</summary>
public sealed record PokemonUpdated(uint Key, ushort Species, byte Form, string Nickname, int Level, int PrimaryType) : RoomEvent;

[JsonConverter(typeof(JsonStringEnumConverter<DeathCause>))]
public enum DeathCause
{
    FaintedInParty,
    GraveyardBox,
    Released,

    /// <summary>Marked by the player.</summary>
    Manual,
}

public sealed record PokemonDied(uint Key, DeathCause Cause) : RoomEvent;

/// <summary>A death recorded by mistake.</summary>
public sealed record DeathUndone(uint Key) : RoomEvent;

/// <param name="Index">0-based badge or trial.</param>
public sealed record MilestoneEarned(int Index) : RoomEvent;

/// <summary>The player's save was read: who is in the party now.</summary>
public sealed record SaveSynced(List<uint> Party, int MilestonesEarned) : RoomEvent;

/// <summary>Lives added (positive) or lost (negative) by hand, for example on a whiteout.</summary>
public sealed record LivesChanged(int Delta, string Reason) : RoomEvent;

/// <summary>Links a capture of the event's player with captures of other players (manual matching).</summary>
public sealed record ManualLink(uint Key, List<LinkMember> With) : RoomEvent;

public sealed record ManualUnlink(uint Key) : RoomEvent;

/// <param name="ForTeam">A team spin (<see cref="RouletteMode.TeamSpin"/>): the milestone is spun for every team member.</param>
public sealed record RouletteSpun(int Milestone, LockePrize Prize, bool ForTeam) : RoomEvent;

/// <summary>A Pokémon of a player.</summary>
public readonly record struct LinkMember(string PlayerId, uint Key);
