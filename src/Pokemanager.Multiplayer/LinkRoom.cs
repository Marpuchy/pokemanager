using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pokemanager.Multiplayer;

/// <summary>
/// A multiplayer Locke: the rules chosen by the host, the players and the log of everything that happened. Every player
/// keeps a copy; the network layer only has to agree on the order of <see cref="Events"/>.
/// </summary>
public sealed class LinkRoom
{
    public const string Magic = "pokemanager-link-room";
    public const int CurrentFormat = 1;

    public string Format { get; set; } = Magic;
    public int Version { get; set; } = CurrentFormat;
    public string RoomId { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string HostPlayerId { get; set; } = "";

    /// <summary>The local player of this copy (each player has a different one).</summary>
    public string LocalPlayerId { get; set; } = "";

    public LinkRules Rules { get; set; } = new();
    public List<RoomEvent> Events { get; set; } = [];

    /// <summary>Last snapshot of the local save, to find out what changed at the next synchronization.</summary>
    public SaveSnapshot? LastSnapshot { get; set; }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>A new room hosted by the local player, who joins it with their save.</summary>
    public static LinkRoom Create(string name, string playerName, SaveSnapshot save, LinkRules rules)
    {
        var room = new LinkRoom { Name = name, Rules = rules };
        room.HostPlayerId = room.LocalPlayerId = Guid.NewGuid().ToString("N");
        room.Join(playerName, save);
        return room;
    }

    /// <summary>Announces the local player (again, when the name or game changed).</summary>
    public PlayerJoined Join(string playerName, SaveSnapshot save)
    {
        var joined = new PlayerJoined(playerName, save.Game, save.Generation, save.MilestoneCount)
            { PlayerId = LocalPlayerId, When = save.Taken };
        Append([joined]);
        return joined;
    }

    public RoomState State() => RoomState.Build(Rules, Events);

    /// <summary>Adds events not seen yet (by id). Returns how many were new.</summary>
    public int Append(IEnumerable<RoomEvent> events)
    {
        var known = Events.Select(e => e.Id).ToHashSet();
        int added = 0;
        foreach (var e in events)
        {
            if (known.Add(e.Id))
            {
                Events.Add(e);
                added++;
            }
        }
        return added;
    }

    /// <summary>Reads the local save and records what changed since the last time.</summary>
    public List<RoomEvent> Synchronize(SaveSnapshot current)
    {
        var events = SaveSync.Detect(Rules, LocalPlayerId, LastSnapshot, current);
        Append(events);
        LastSnapshot = current;
        return events;
    }

    public void Save(string path)
    {
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, Json));
        File.Move(temp, path, overwrite: true);
    }

    /// <exception cref="InvalidDataException">Not a room file.</exception>
    public static LinkRoom Load(string path)
    {
        var room = JsonSerializer.Deserialize<LinkRoom>(File.ReadAllText(path), Json);
        if (room is null || room.Format != Magic)
            throw new InvalidDataException("Not a Pokemanager room file.");
        return room;
    }
}
