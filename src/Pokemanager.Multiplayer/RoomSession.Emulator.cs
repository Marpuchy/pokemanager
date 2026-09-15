using System.Net;
using System.Net.Sockets;
using LiteNetLib;

namespace Pokemanager.Multiplayer;

/// <summary>The emulator room as this app sees it.</summary>
/// <param name="Port">
/// Where an emulator on this computer connects (address 127.0.0.1): the host's room server, or the local end of a guest's
/// tunnel to it.
/// </param>
public sealed record EmulatorRoomState(EmulatorRoomInfo Info, int Port);

/// <summary>
/// In-game link through the room: the host runs the emulator's room server on its own computer and every guest's emulator
/// reaches it through the room connection, which already works through the routers. The emulator's UDP packets travel
/// encrypted on their own channel, one socket per guest on the host (so the room server tells the guests apart).
/// </summary>
public sealed partial class RoomSession
{
    private const byte TunnelChannel = 1;

    private EmulatorRoomInfo? emulatorRoom;
    private int emulatorServerPort;
    private readonly Dictionary<int, UdpClient> hostTunnels = [];
    private UdpClient? guestTunnel;
    private IPEndPoint? guestEmulator;

    public EmulatorRoomState? EmulatorRoom
    {
        get
        {
            lock (gate)
            {
                if (emulatorRoom is null)
                    return null;
                int port = IsHost ? emulatorServerPort : guestTunnel is { } tunnel ? ((IPEndPoint)tunnel.Client.LocalEndPoint!).Port : 0;
                return new EmulatorRoomState(emulatorRoom, port);
            }
        }
    }

    /// <summary>Host: the emulator's room server is listening on <paramref name="serverPort"/> of this computer.</summary>
    public void OpenEmulatorRoom(EmulatorRoomInfo info, int serverPort) => work.Enqueue(() =>
    {
        if (!IsHost)
            return;
        CloseHostTunnels();
        lock (gate)
        {
            emulatorRoom = info;
            emulatorServerPort = serverPort;
        }
        Broadcast(new EmulatorRoomOpened(info), except: null);
        RaiseChanged();
    });

    /// <summary>Host: the emulator room is gone.</summary>
    public void CloseEmulatorRoom() => work.Enqueue(() =>
    {
        if (!IsHost || emulatorRoom is null)
            return;
        CloseHostTunnels();
        lock (gate)
            emulatorRoom = null;
        Broadcast(new EmulatorRoomClosed(), except: null);
        RaiseChanged();
    });

    private void SendEmulatorRoom(NetPeer peer)
    {
        if (emulatorRoom is { } room)
            Send(peer, new EmulatorRoomOpened(room));
    }

    // ------------------------------------------------------------------ host side

    private void HostTunnelReceive(NetPeer peer, byte[] data)
    {
        if (emulatorRoom is null || !peerPlayers.ContainsKey(peer.Id))
            return;
        if (!hostTunnels.TryGetValue(peer.Id, out var socket))
        {
            socket = LocalSocket();
            hostTunnels[peer.Id] = socket;
            _ = Task.Run(() => PumpToPeer(socket, peer));
        }
        try
        {
            socket.Send(data, data.Length, new IPEndPoint(IPAddress.Loopback, emulatorServerPort));
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
        {
        }
    }

    /// <summary>Room server → a guest.</summary>
    private async Task PumpToPeer(UdpClient socket, NetPeer peer)
    {
        while (!stop.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await socket.ReceiveAsync(stop.Token);
            }
            catch (SocketException)
            {
                continue; // an ICMP "port unreachable" from an earlier send: keep listening
            }
            catch (Exception ex) when (ex is ObjectDisposedException or OperationCanceledException)
            {
                return;
            }
            SendTunnel(peer, result.Buffer);
        }
    }

    private void CloseHostTunnel(int peerId)
    {
        if (hostTunnels.Remove(peerId, out var socket))
            socket.Dispose();
    }

    private void CloseHostTunnels()
    {
        foreach (var socket in hostTunnels.Values)
            socket.Dispose();
        hostTunnels.Clear();
    }

    // ------------------------------------------------------------------ guest side

    private void GuestEmulatorRoomOpened(EmulatorRoomInfo info)
    {
        lock (gate)
        {
            emulatorRoom = info;
            if (guestTunnel is null)
            {
                guestTunnel = LocalSocket();
                var socket = guestTunnel;
                _ = Task.Run(() => PumpToHost(socket));
            }
        }
    }

    private void GuestEmulatorRoomClosed()
    {
        lock (gate)
        {
            emulatorRoom = null;
            guestTunnel?.Dispose();
            guestTunnel = null;
            guestEmulator = null;
        }
    }

    /// <summary>The local emulator → the host's room server.</summary>
    private async Task PumpToHost(UdpClient socket)
    {
        while (!stop.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await socket.ReceiveAsync(stop.Token);
            }
            catch (SocketException)
            {
                continue;
            }
            catch (Exception ex) when (ex is ObjectDisposedException or OperationCanceledException)
            {
                return;
            }
            // Only emulators on this computer: the socket listens on 127.0.0.1.
            guestEmulator = result.RemoteEndPoint;
            if (hostPeer is { } peer)
                SendTunnel(peer, result.Buffer);
        }
    }

    private void GuestTunnelReceive(byte[] data)
    {
        if (guestTunnel is { } socket && guestEmulator is { } emulator)
        {
            try
            {
                socket.Send(data, data.Length, emulator);
            }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
            {
            }
        }
    }

    // ------------------------------------------------------------------ both

    private void OnTunnelPacket(NetPeer peer, ReadOnlySpan<byte> packet)
    {
        byte[] data;
        try
        {
            data = codec.Open(packet);
        }
        catch (InvalidDataException)
        {
            return;
        }
        if (IsHost)
            HostTunnelReceive(peer, data);
        else if (peer == hostPeer)
            GuestTunnelReceive(data);
    }

    /// <summary>
    /// Small packets go unreliable, as the emulator sent them (it retransmits by itself); what does not fit in one UDP packet
    /// of the room connection goes reliable, which LiteNetLib fragments.
    /// </summary>
    private void SendTunnel(NetPeer peer, byte[] data)
    {
        byte[] packet = codec.Seal(data);
        try
        {
            var method = packet.Length <= peer.GetMaxSinglePacketSize(DeliveryMethod.Unreliable)
                ? DeliveryMethod.Unreliable
                : DeliveryMethod.ReliableUnordered;
            peer.Send(packet, TunnelChannel, method);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
        }
    }

    /// <summary>A UDP socket on 127.0.0.1 with a free port, ignoring Windows' "connection reset" errors of UDP.</summary>
    private static UdpClient LocalSocket()
    {
        var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        if (OperatingSystem.IsWindows())
        {
            const int SioUdpConnReset = -1744830452;
            socket.Client.IOControl(SioUdpConnReset, [0, 0, 0, 0], null);
        }
        return socket;
    }

    private void DisposeEmulatorTunnels()
    {
        CloseHostTunnels();
        GuestEmulatorRoomClosed();
    }
}
