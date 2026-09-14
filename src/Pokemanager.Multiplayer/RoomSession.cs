using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using LiteNetLib;
using Pokemanager.Multiplayer.Net;

namespace Pokemanager.Multiplayer;

public enum RoomStatus
{
    /// <summary>Guest: trying the host's addresses.</summary>
    Connecting,

    /// <summary>Host: room open. Guest: connected to the host.</summary>
    Online,

    /// <summary>Guest: the direct way failed; the host has to paste this guest's answer code.</summary>
    WaitingForAnswer,

    /// <summary>Guest: the connection dropped and is being retried.</summary>
    Reconnecting,

    Closed,
}

/// <summary>A player of the room as this app knows it.</summary>
/// <param name="Snapshot">The last save they shared (already filtered by the rules), null until they share one.</param>
public sealed record RoomPlayer(string Id, PlayerProfile Profile, bool IsHost, bool IsLocal, bool Online, TrainerSnapshot? Snapshot, DateTime? ReceivedAt)
{
    public string Name => Profile.Name;
}

public sealed class RoomOptions
{
    /// <summary>UDP port; 0 picks a free one.</summary>
    public int Port { get; init; }

    /// <summary>Ask the router to open the port (UPnP / NAT-PMP).</summary>
    public bool MapPort { get; init; } = true;

    /// <summary>Ask public STUN servers for the address the internet sees.</summary>
    public bool UseStun { get; init; } = true;

    public IReadOnlyList<string> StunServers { get; init; } = Stun.DefaultServers;

    /// <summary>Also offer 127.0.0.1 (tests, two apps on one computer).</summary>
    public bool IncludeLoopback { get; init; }

    public TimeSpan DiscoveryTimeout { get; init; } = TimeSpan.FromSeconds(2.5);

    /// <summary>How long a guest tries the direct way before asking for the answer code.</summary>
    public TimeSpan DirectConnectTimeout { get; init; } = TimeSpan.FromSeconds(8);

    /// <summary>The battle simulator. Only the host's matters: it runs the battles of the room. Null: no battles.</summary>
    public Pokemanager.Battle.ShowdownTools? Battles { get; init; }
}

/// <summary>
/// A live room over UDP (LiteNetLib: reliable, ordered, fragmented) with no server: the host's app is the hub and relays
/// what each guest shares to the others, filtered by the rules. Network work runs on its own loop; public methods can be
/// called from any thread and <see cref="Changed"/> is raised on the network thread.
/// </summary>
public sealed partial class RoomSession : IAsyncDisposable
{
    private readonly object gate = new();
    private readonly EventBasedNetListener listener = new();
    private readonly NetManager net;
    private readonly MessageCodec codec;
    private readonly byte[] secret;
    private readonly string connectionKey;
    private readonly RoomOptions options;
    private readonly ConcurrentQueue<Action> work = new();
    private readonly CancellationTokenSource stop = new();
    private readonly Dictionary<string, RoomPlayer> players = [];
    private readonly Dictionary<int, string> peerPlayers = [];
    private readonly List<(IPEndPoint[] Endpoints, DateTime Until)> punches = [];
    private Task? loop;
    private NetPeer? hostPeer;
    private TrainerSnapshot? localSnapshot;
    private InviteCode? invite;
    private DateTime lastConnectAttempt, joinStarted, mappingRenewed;

    public bool IsHost { get; }
    public string LocalPlayerId { get; }
    public string RoomName { get; private set; }
    public RoomRules Rules { get; private set; }
    public RoomStatus Status { get; private set; }
    public int Port { get; private set; }

    /// <summary>Host: the code to give to the others.</summary>
    public string? InviteText { get; private set; }

    /// <summary>Guest: the code for the host when the direct way failed.</summary>
    public string? AnswerText { get; private set; }

    /// <summary>The port opened on the router, if any.</summary>
    public PortMapping? Mapping { get; private set; }

    /// <summary>The public address a STUN server saw, if any.</summary>
    public IPEndPoint? PublicEndpoint { get; private set; }

    /// <summary>Where this app can be reached, best first.</summary>
    public IReadOnlyList<IPEndPoint> Endpoints { get; private set; } = [];

    /// <summary>Something changed (status, players, snapshots, rules). Raised on the network thread.</summary>
    public event Action? Changed;

    public IReadOnlyList<RoomPlayer> Players
    {
        get { lock (gate) return players.Values.OrderByDescending(p => p.IsLocal).ThenByDescending(p => p.IsHost).ThenBy(p => p.Name).ToList(); }
    }

    private RoomSession(bool isHost, string roomName, string playerId, PlayerProfile profile, RoomRules rules, byte[] secret, RoomOptions options)
    {
        IsHost = isHost;
        RoomName = roomName;
        LocalPlayerId = playerId;
        Rules = rules;
        this.secret = secret;
        this.options = options;
        codec = new MessageCodec(secret);
        connectionKey = MessageCodec.ConnectionKey(secret);
        players[playerId] = new RoomPlayer(playerId, profile.Sanitized(), isHost, true, true, null, null);
        net = new NetManager(listener)
        {
            IPv6Enabled = true,
            UnconnectedMessagesEnabled = true,
            DisconnectTimeout = 15000,
            MaxConnectAttempts = 20,
            ReconnectDelay = 500,
        };
        listener.ConnectionRequestEvent += OnConnectionRequest;
        listener.PeerConnectedEvent += OnPeerConnected;
        listener.PeerDisconnectedEvent += OnPeerDisconnected;
        listener.NetworkReceiveEvent += OnReceive;
    }

    /// <summary>Opens a room. Reusing <paramref name="secret"/> and <paramref name="port"/> keeps the invite code of an earlier session.</summary>
    public static async Task<RoomSession> HostAsync(string roomName, string playerId, PlayerProfile profile, RoomRules rules,
        RoomOptions options, byte[]? secret = null, CancellationToken cancel = default)
    {
        var session = new RoomSession(true, roomName, playerId, profile, rules, secret ?? InviteCode.NewSecret(), options);
        await session.StartAsync(cancel);
        session.InviteText = InviteCode.Invite(session.secret, session.Endpoints).ToString();
        session.Status = RoomStatus.Online;
        return session;
    }

    /// <summary>Joins with an invite code. Returns at once; <see cref="Status"/> follows the connection.</summary>
    /// <exception cref="FormatException">Not an invite code.</exception>
    public static async Task<RoomSession> JoinAsync(string inviteText, string playerId, PlayerProfile profile, RoomOptions options,
        CancellationToken cancel = default)
    {
        var code = InviteCode.Parse(inviteText);
        if (code.IsAnswer)
            throw new FormatException("This is an answer code: the host pastes it, guests use the invite code.");
        var session = new RoomSession(false, "", playerId, profile, new RoomRules(), code.Key, options) { invite = code };
        await session.StartAsync(cancel);
        session.AnswerText = InviteCode.Answer(code.Key, session.Endpoints).ToString();
        session.Status = RoomStatus.Connecting;
        session.joinStarted = DateTime.UtcNow;
        session.work.Enqueue(session.ConnectToHost);
        return session;
    }

    private async Task StartAsync(CancellationToken cancel)
    {
        // STUN has to use the very port the room will use, before LiteNetLib takes it.
        int port = options.Port;
        using (var probe = new UdpClient(new IPEndPoint(IPAddress.Any, port)))
        {
            port = ((IPEndPoint)probe.Client.LocalEndPoint!).Port;
            if (options.UseStun)
            {
                try { PublicEndpoint = await Stun.QueryAsync(probe, options.StunServers, options.DiscoveryTimeout, cancel); }
                catch (SocketException) { }
            }
        }
        if (!net.Start(port))
            throw new SocketException((int)SocketError.AddressAlreadyInUse);
        Port = net.LocalPort;
        if (options.MapPort)
        {
            Mapping = await PortMapping.OpenAsync(Port, options.DiscoveryTimeout, cancel);
            mappingRenewed = DateTime.UtcNow;
        }
        Endpoints = GatherEndpoints();
        loop = Task.Run(RunAsync);
    }

    private List<IPEndPoint> GatherEndpoints()
    {
        var list = new List<IPEndPoint>();
        void Add(IPEndPoint ep) { if (!list.Contains(ep)) list.Add(ep); }
        if (Mapping is { } m && IsPublic(m.External.Address))
            Add(m.External);
        if (PublicEndpoint is { } stun)
            Add(stun);
        foreach (var address in LocalAddresses())
            Add(new IPEndPoint(address, Port));
        if (options.IncludeLoopback)
            Add(new IPEndPoint(IPAddress.Loopback, Port));
        return list;
    }

    /// <summary>LAN IPv4 addresses and global IPv6 addresses of the interfaces that are up.</summary>
    internal static IEnumerable<IPAddress> LocalAddresses() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses.Select(u => u.Address))
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork
                ? !a.ToString().StartsWith("169.254.", StringComparison.Ordinal)
                : a.AddressFamily == AddressFamily.InterNetworkV6 && !a.IsIPv6LinkLocal && !a.IsIPv6SiteLocal && IsPublic(a))
            .OrderBy(a => a.AddressFamily == AddressFamily.InterNetworkV6);

    /// <summary>Not private, carrier-grade NAT, link-local or unique-local.</summary>
    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        byte[] b = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
            return !(b[0] == 10 || b[0] == 127 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168)
                     || (b[0] == 100 && b[1] is >= 64 and <= 127) || (b[0] == 169 && b[1] == 254) || b[0] == 0);
        return !(IPAddress.IsLoopback(address) || address.IsIPv6LinkLocal || (b[0] & 0xFE) == 0xFC);
    }

    // ------------------------------------------------------------------ public actions (any thread)

    /// <summary>Shares the local save with the room (the rules decide what the others get).</summary>
    public void Publish(TrainerSnapshot snapshot) => work.Enqueue(() =>
    {
        localSnapshot = snapshot;
        SetPlayer(LocalPlayerId, p => p with { Snapshot = snapshot, ReceivedAt = DateTime.Now });
        var message = new SnapshotShared(LocalPlayerId, snapshot.FilteredBy(Rules));
        if (IsHost)
            Broadcast(message, except: null);
        else if (hostPeer is { } peer)
            Send(peer, message);
        RaiseChanged();
    });

    private PlayerProfile LocalProfile { get { lock (gate) return players[LocalPlayerId].Profile; } }

    /// <summary>Changes how the local player appears to the others (name, sprite, color).</summary>
    public void UpdateProfile(PlayerProfile profile) => work.Enqueue(() =>
    {
        var clean = profile.Sanitized();
        SetPlayer(LocalPlayerId, p => p with { Profile = clean });
        var message = new ProfileChanged(LocalPlayerId, clean);
        if (IsHost)
            Broadcast(message, except: null);
        else if (hostPeer is { } peer)
            Send(peer, message);
        RaiseChanged();
    });

    /// <summary>Host: changes what players can see of each other.</summary>
    public void SetRules(RoomRules rules) => work.Enqueue(() =>
    {
        if (!IsHost || rules == Rules)
            return;
        Rules = rules;
        // The rules bind the host too; what they hid comes back when guests share again after RulesChanged.
        lock (gate)
        {
            foreach (var p in players.Values.Where(p => !p.IsLocal && p.Snapshot is not null).ToList())
                players[p.Id] = p with { Snapshot = p.Snapshot!.FilteredBy(rules) };
        }
        Broadcast(new RulesChanged(rules), except: null);
        if (localSnapshot is { } mine)
            Broadcast(new SnapshotShared(LocalPlayerId, mine.FilteredBy(rules)), except: null);
        RaiseChanged();
    });

    /// <summary>Host: a guest's answer code. Both sides send packets to each other for a while, which opens most routers.</summary>
    /// <exception cref="FormatException">Not an answer code of this room.</exception>
    public void AcceptAnswer(string answerText)
    {
        var answer = InviteCode.Parse(answerText);
        if (!IsHost || !answer.IsAnswer || !answer.Key.AsSpan().SequenceEqual(InviteCode.RoomTag(secret)))
            throw new FormatException("This answer code belongs to another room.");
        var endpoints = answer.Endpoints.Where(InviteCode.Usable).ToArray();
        work.Enqueue(() => punches.Add((endpoints, DateTime.UtcNow.AddSeconds(45))));
    }

    // ------------------------------------------------------------------ network loop

    private async Task RunAsync()
    {
        var token = stop.Token;
        byte[] punch = "pokemanager-punch"u8.ToArray();
        DateTime lastPunch = default;
        while (!token.IsCancellationRequested)
        {
            try
            {
                while (work.TryDequeue(out var action))
                    action();
                net.PollEvents();

                var now = DateTime.UtcNow;
                if (!IsHost && hostPeer is null)
                {
                    if (now - lastConnectAttempt > TimeSpan.FromSeconds(10))
                        ConnectToHost();
                    if (Status == RoomStatus.Connecting && now - joinStarted > options.DirectConnectTimeout)
                    {
                        Status = RoomStatus.WaitingForAnswer;
                        RaiseChanged();
                    }
                }
                if (punches.Count > 0 && now - lastPunch > TimeSpan.FromMilliseconds(300))
                {
                    lastPunch = now;
                    punches.RemoveAll(p => p.Until < now);
                    foreach (var ep in punches.SelectMany(p => p.Endpoints))
                        net.SendUnconnectedMessage(punch, ep);
                }
                if (Mapping is not null && now - mappingRenewed > TimeSpan.FromMinutes(30))
                {
                    mappingRenewed = now;
                    _ = PortMapping.OpenAsync(Port, options.DiscoveryTimeout, token);
                }
            }
            catch (Exception ex) when (ex is SocketException or InvalidOperationException or ObjectDisposedException)
            {
            }
            try { await Task.Delay(15, token); }
            catch (OperationCanceledException) { break; }
        }
    }

    private void ConnectToHost()
    {
        lastConnectAttempt = DateTime.UtcNow;
        foreach (var ep in invite!.Endpoints.Where(InviteCode.Usable))
            net.Connect(ep, connectionKey);
    }

    private void OnConnectionRequest(ConnectionRequest request)
    {
        if (IsHost)
            request.AcceptIfKey(connectionKey);
        else
            request.Reject();
    }

    private void OnPeerConnected(NetPeer peer)
    {
        if (IsHost)
            return; // the guest introduces itself with Hello
        if (hostPeer is not null)
        {
            peer.Disconnect(); // another of the host's addresses answered too
            return;
        }
        hostPeer = peer;
        Send(peer, new Hello(MessageCodec.Protocol, LocalPlayerId, LocalProfile));
    }

    private void OnPeerDisconnected(NetPeer peer, DisconnectInfo info)
    {
        if (IsHost)
        {
            if (peerPlayers.Remove(peer.Id, out string? id))
            {
                SetPlayer(id, p => p with { Online = false });
                Broadcast(new PlayerLeft(id), except: null);
                PlayerLeftBattles(id);
                RaiseChanged();
            }
        }
        else if (peer == hostPeer)
        {
            hostPeer = null;
            Status = RoomStatus.Reconnecting;
            lock (gate)
            {
                foreach (var p in players.Values.Where(p => !p.IsLocal).ToList())
                    players[p.Id] = p with { Online = false };
            }
            lastConnectAttempt = DateTime.UtcNow.AddSeconds(-8); // retry soon
            RaiseChanged();
        }
    }

    private void OnReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod method)
    {
        RoomMessage message;
        try
        {
            message = codec.Decode(reader.GetRemainingBytes());
        }
        catch (InvalidDataException)
        {
            return;
        }
        finally
        {
            reader.Recycle();
        }
        if (IsHost)
            HostReceive(peer, message);
        else if (peer == hostPeer)
            GuestReceive(message);
        RaiseChanged();
    }

    private void HostReceive(NetPeer peer, RoomMessage message)
    {
        switch (message)
        {
            case Hello hello when hello.Protocol != MessageCodec.Protocol:
                peer.Disconnect();
                break;
            case Hello hello when hello.PlayerId != LocalPlayerId:
                peerPlayers[peer.Id] = hello.PlayerId;
                RoomPlayer[] others;
                lock (gate)
                {
                    var profile = hello.Profile.Sanitized();
                    players[hello.PlayerId] = players.TryGetValue(hello.PlayerId, out var known)
                        ? known with { Profile = profile, Online = true }
                        : new RoomPlayer(hello.PlayerId, profile, false, false, true, null, null);
                    others = players.Values.Where(p => p.Id != hello.PlayerId).ToArray();
                }
                Send(peer, new Welcome(RoomName, LocalPlayerId, Rules,
                    others.Where(p => p.Online).Select(p => new PlayerJoined(p.Id, p.Profile)).ToList()));
                foreach (var other in others.Where(p => p.Online && p.Snapshot is not null))
                    Send(peer, new SnapshotShared(other.Id, other.Snapshot!.FilteredBy(Rules)));
                Broadcast(new PlayerJoined(hello.PlayerId, hello.Profile.Sanitized()), except: peer);
                break;
            case SnapshotShared shared when peerPlayers.TryGetValue(peer.Id, out string? id):
                var filtered = shared.Snapshot.FilteredBy(Rules);
                SetPlayer(id, p => p with { Snapshot = filtered, ReceivedAt = DateTime.Now });
                Broadcast(new SnapshotShared(id, filtered), except: peer);
                break;
            case ProfileChanged changed when peerPlayers.TryGetValue(peer.Id, out string? who):
                var clean = changed.Profile.Sanitized();
                SetPlayer(who, p => p with { Profile = clean });
                Broadcast(new ProfileChanged(who, clean), except: peer);
                break;
            case var battleMessage when peerPlayers.TryGetValue(peer.Id, out string? from):
                HostBattleMessage(from, battleMessage);
                break;
        }
    }

    private void GuestReceive(RoomMessage message)
    {
        switch (message)
        {
            case Welcome welcome:
                RoomName = welcome.RoomName;
                Rules = welcome.Rules;
                Status = RoomStatus.Online;
                lock (gate)
                {
                    foreach (var p in welcome.Players.Where(p => p.PlayerId != LocalPlayerId))
                    {
                        bool isHost = p.PlayerId == welcome.HostPlayerId;
                        players[p.PlayerId] = players.TryGetValue(p.PlayerId, out var known)
                            ? known with { Profile = p.Profile, Online = true, IsHost = isHost }
                            : new RoomPlayer(p.PlayerId, p.Profile, isHost, false, true, null, null);
                    }
                }
                if (localSnapshot is { } mine)
                    Send(hostPeer!, new SnapshotShared(LocalPlayerId, mine.FilteredBy(Rules)));
                break;
            case PlayerJoined joined when joined.PlayerId != LocalPlayerId:
                lock (gate)
                {
                    players[joined.PlayerId] = players.TryGetValue(joined.PlayerId, out var known)
                        ? known with { Profile = joined.Profile, Online = true }
                        : new RoomPlayer(joined.PlayerId, joined.Profile, false, false, true, null, null);
                }
                break;
            case ProfileChanged changed when changed.PlayerId != LocalPlayerId:
                SetPlayer(changed.PlayerId, p => p with { Profile = changed.Profile });
                break;
            case PlayerLeft left:
                SetPlayer(left.PlayerId, p => p with { Online = false });
                break;
            case SnapshotShared shared when shared.PlayerId != LocalPlayerId:
                SetPlayer(shared.PlayerId, p => p with { Snapshot = shared.Snapshot.FilteredBy(Rules), ReceivedAt = DateTime.Now }, create: true);
                break;
            case RulesChanged changed:
                Rules = changed.Rules;
                lock (gate)
                {
                    foreach (var p in players.Values.Where(p => !p.IsLocal && p.Snapshot is not null).ToList())
                        players[p.Id] = p with { Snapshot = p.Snapshot!.FilteredBy(Rules) };
                }
                if (localSnapshot is { } own)
                    Send(hostPeer!, new SnapshotShared(LocalPlayerId, own.FilteredBy(Rules)));
                break;
            default:
                ReceiveBattleMessage(message);
                break;
        }
    }

    private void SetPlayer(string id, Func<RoomPlayer, RoomPlayer> change, bool create = false)
    {
        lock (gate)
        {
            if (players.TryGetValue(id, out var player))
                players[id] = change(player);
            else if (create)
                players[id] = change(new RoomPlayer(id, new PlayerProfile("?"), false, false, true, null, null));
        }
    }

    private void Send(NetPeer peer, RoomMessage message) => peer.Send(codec.Encode(message), DeliveryMethod.ReliableOrdered);

    private void Broadcast(RoomMessage message, NetPeer? except)
    {
        byte[] data = codec.Encode(message);
        foreach (int peerId in peerPlayers.Keys.ToList())
        {
            if (net.TryGetPeerById(peerId, out var peer) && peer != except)
                peer.Send(data, DeliveryMethod.ReliableOrdered);
        }
    }

    private void RaiseChanged() => Changed?.Invoke();

    public async ValueTask DisposeAsync()
    {
        if (Status == RoomStatus.Closed)
            return;
        Status = RoomStatus.Closed;
        stop.Cancel();
        if (loop is not null)
        {
            try { await loop; }
            catch (OperationCanceledException) { }
        }
        await DisposeBattlesAsync();
        net.Stop(true); // tells the peers, so they notice at once
        if (Mapping is { } mapping)
            await mapping.DisposeAsync();
        stop.Dispose();
        RaiseChanged();
    }
}
