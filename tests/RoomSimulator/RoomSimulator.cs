#:project ../../src/Pokemanager.Multiplayer/Pokemanager.Multiplayer.csproj
#:property JsonSerializerIsReflectionEnabledByDefault=true

// A pretend player for trying multiplayer rooms without a second person. It shares a made-up save (no game data needed)
// and "saves in the game" every few seconds: a party Pokémon levels up, now and then a new capture lands in a box.
//
//   dotnet run tests/RoomSimulator/RoomSimulator.cs -- join <invite code> [--name Nico] [--sprite acetrainer-gen7] [--color #2E8B57] [--every 15] [--lan]
//   dotnet run tests/RoomSimulator/RoomSimulator.cs -- host [--name Nico] [--sprite acetrainer-gen7] [--color #2E8B57] [--every 15] [--lan]
//
// --lan: skip the router and STUN (only this computer / network). While hosting, paste a guest's answer code (PMR-…) and
// press Enter to accept it. Ctrl+C leaves the room.
using Pokemanager.Multiplayer;

if (args.Length == 0 || args[0] is not ("join" or "host") || (args[0] == "join" && args.Length < 2))
{
    Console.WriteLine("Usage: join <invite code> | host   [--name Nico] [--sprite acetrainer-gen7] [--color #2E8B57] [--every 15] [--lan]");
    return 1;
}

string Option(string name, string fallback)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
}

string name = Option("--name", "Nico");
int every = int.Parse(Option("--every", "15"));
bool lan = args.Contains("--lan");
var options = new RoomOptions { MapPort = !lan, UseStun = !lan, IncludeLoopback = lan };
var random = new Random();
string id = "sim-" + Guid.NewGuid().ToString("N")[..8];
var profile = new PlayerProfile(name, Sprite: Option("--sprite", "acetrainer-gen7"), Color: Option("--color", "#2E8B57"));

Console.WriteLine("Preparing the connection…");
await using var session = args[0] == "host"
    ? await RoomSession.HostAsync($"Sala de {name}", id, profile, new RoomRules(), options)
    : await RoomSession.JoinAsync(args[1], id, profile, options);

Console.WriteLine($"UDP port {session.Port} · STUN {session.PublicEndpoint?.ToString() ?? "none"} · router {session.Mapping?.Method ?? "no port opened"}");
if (session.IsHost)
    Console.WriteLine($"\nINVITE CODE:\n{session.InviteText}\n");

var status = session.Status;
var seenPlayers = new HashSet<string>();
session.Changed += () =>
{
    if (session.Status != status)
    {
        status = session.Status;
        Console.WriteLine($"[{DateTime.Now:T}] status: {status}");
        if (status == RoomStatus.WaitingForAnswer)
            Console.WriteLine($"\nThe host is not reachable directly. Give the host this ANSWER CODE:\n{session.AnswerText}\n");
    }
    foreach (var p in session.Players.Where(p => !p.IsLocal))
    {
        string line = $"{p.Name} {(p.Online ? "online" : "offline")} party {p.Snapshot?.Party?.Count.ToString() ?? "-"} boxes {(p.Snapshot?.Boxes is null ? "hidden/none" : "shared")}";
        if (seenPlayers.Add(p.Id + line))
            Console.WriteLine($"[{DateTime.Now:T}] {line}");
    }
};

// A made-up run: 6 in the party, 3 boxes partly filled.
ushort[] species = [6, 9, 25, 94, 130, 131, 143, 149, 248, 257, 282, 373, 445, 448, 658, 700, 724, 745, 784, 800];
SharedPokemon Mon(int level)
{
    // Like a real save: each move with its type in the owner's ROM and current/max PP.
    int[] maxPp = [.. Enumerable.Range(0, 4).Select(_ => new[] { 5, 10, 15, 20, 25, 30, 35, 40 }[random.Next(8)])];
    return new SharedPokemon(
        species[random.Next(species.Length)], 0, (byte)random.Next(2), random.Next(40) == 0, false, "", level,
        random.Next(3) == 0 ? 0 : random.Next(1, 230), random.Next(1, 190), random.Next(25),
        [(ushort)random.Next(1, 600), (ushort)random.Next(1, 600), (ushort)random.Next(1, 600), (ushort)random.Next(1, 600)],
        [.. Enumerable.Range(0, 6).Select(_ => random.Next(40, 250))], [random.Next(18), random.Next(18)], random.Next(40, 250),
        MoveTypes: [.. Enumerable.Range(0, 4).Select(_ => random.Next(18))],
        MovePp: [.. maxPp.SelectMany(max => new[] { random.Next(max + 1), max })]);
}

var party = Enumerable.Range(0, 6).Select(_ => Mon(random.Next(20, 40))).ToList();
var boxes = Enumerable.Range(0, 3).Select(b => new SharedBox($"Box {b + 1}",
    [.. Enumerable.Range(0, 30).Select(_ => random.Next(3) == 0 ? Mon(random.Next(5, 30)) : null)])).ToList();
var milestones = new List<bool> { true, true, false, false, false, false, false, false };
int hours = 12, minutes = 0;

void Save()
{
    int slot = random.Next(party.Count);
    party[slot] = party[slot] with { Level = Math.Min(100, party[slot].Level + 1) };
    if (random.Next(3) == 0)
    {
        var box = boxes[random.Next(boxes.Count)];
        int empty = box.Slots.FindIndex(s => s is null);
        if (empty >= 0)
            box.Slots[empty] = Mon(random.Next(10, 35));
    }
    minutes += every / 5 + 1;
    var snapshot = new TrainerSnapshot(name, "UM", 7, 5, milestones.Take(5).ToList(), (uint)random.Next(10_000, 90_000),
        hours + (minutes / 60), minutes % 60, [.. party], [.. boxes.Select(b => b with { Slots = [.. b.Slots] })], DateTime.Now);
    session.Publish(snapshot);
    Console.WriteLine($"[{DateTime.Now:T}] saved in the game: {party[slot].Species} is now level {party[slot].Level}");
}

var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
if (session.IsHost)
{
    _ = Task.Run(() =>
    {
        while (!stop.IsCancellationRequested && Console.ReadLine() is { } line)
        {
            if (line.Trim().Length == 0)
                continue;
            try { session.AcceptAnswer(line); Console.WriteLine("Answer accepted: opening the way (45 s)."); }
            catch (FormatException ex) { Console.WriteLine("Not valid: " + ex.Message); }
        }
    });
}

Save();
try
{
    while (!stop.IsCancellationRequested)
    {
        await Task.Delay(TimeSpan.FromSeconds(every), stop.Token);
        Save();
    }
}
catch (OperationCanceledException)
{
}
Console.WriteLine("Leaving the room.");
return 0;
