using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Pokemanager.Multiplayer;

namespace Pokemanager.Tests.Multiplayer;

public sealed class EmulatorTunnelTests
{
    private static RoomOptions Local() => new() { MapPort = false, UseStun = false, IncludeLoopback = true };

    private static async Task Until(Func<bool> condition, string what, int seconds = 20)
    {
        var limit = DateTime.UtcNow.AddSeconds(seconds);
        while (!condition())
        {
            if (DateTime.UtcNow > limit)
                Assert.Fail("Timed out waiting for: " + what);
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>Stands in for the emulator's room server: answers every packet with "echo:" + the packet, and notes who sent it.</summary>
    private sealed class EchoServer : IDisposable
    {
        private readonly UdpClient socket = new(new IPEndPoint(IPAddress.Loopback, 0));
        private readonly CancellationTokenSource stop = new();
        public ConcurrentDictionary<IPEndPoint, int> Clients { get; } = new();
        public int Port => ((IPEndPoint)socket.Client.LocalEndPoint!).Port;

        public EchoServer() => _ = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    var r = await socket.ReceiveAsync(stop.Token);
                    Clients.AddOrUpdate(r.RemoteEndPoint, 1, (_, n) => n + 1);
                    byte[] reply = [.. "echo:"u8.ToArray(), .. r.Buffer];
                    await socket.SendAsync(reply, r.RemoteEndPoint);
                }
                catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
                {
                    if (stop.IsCancellationRequested)
                        return;
                }
            }
        });

        public void Dispose()
        {
            stop.Cancel();
            socket.Dispose();
        }
    }

    private static async Task<byte[]> RoundTrip(UdpClient emulator, int port, byte[] data)
    {
        // The first packets may arrive before the tunnel is set up on the host: send again until an answer comes.
        for (int attempt = 0; attempt < 40; attempt++)
        {
            await emulator.SendAsync(data, new IPEndPoint(IPAddress.Loopback, port));
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            wait.CancelAfter(500);
            try
            {
                return (await emulator.ReceiveAsync(wait.Token)).Buffer;
            }
            catch (OperationCanceledException) when (!TestContext.Current.CancellationToken.IsCancellationRequested)
            {
            }
        }
        Assert.Fail("No answer through the tunnel.");
        return [];
    }

    [Fact]
    public async Task EmulatorPackets_TravelThroughTheRoom_OneServerClientPerGuest()
    {
        var cancel = TestContext.Current.CancellationToken;
        using var server = new EchoServer();
        await using var host = await RoomSession.HostAsync("Link", "host", new PlayerProfile("Oak"), new RoomRules(), Local(), cancel: cancel);
        await using var misty = await RoomSession.JoinAsync(host.InviteText!, "misty", new PlayerProfile("Misty"), Local(), cancel);
        await Until(() => misty.Status == RoomStatus.Online, "Misty in");

        var info = new EmulatorRoomInfo("Pokemanager", "secret", 6, "X");
        host.OpenEmulatorRoom(info, server.Port);
        await Until(() => host.EmulatorRoom is not null && misty.EmulatorRoom is not null, "room known");
        Assert.Equal(server.Port, host.EmulatorRoom!.Port);
        Assert.Equal(info, misty.EmulatorRoom!.Info);
        Assert.NotEqual(0, misty.EmulatorRoom.Port);

        // A guest that joins later is told about the open room too.
        await using var brock = await RoomSession.JoinAsync(host.InviteText!, "brock", new PlayerProfile("Brock"), Local(), cancel);
        await Until(() => brock.EmulatorRoom is not null, "late joiner knows the room");

        using var mistyEmulator = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var brockEmulator = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        Assert.Equal("echo:hello"u8.ToArray(), await RoundTrip(mistyEmulator, misty.EmulatorRoom.Port, "hello"u8.ToArray()));

        // Packets of an emulator's full size (more than one packet of the room connection) arrive whole.
        byte[] big = new byte[1400];
        Random.Shared.NextBytes(big);
        byte[] expected = [.. "echo:"u8.ToArray(), .. big];
        Assert.Equal(expected, await RoundTrip(brockEmulator, brock.EmulatorRoom!.Port, big));
        Assert.Equal(2, server.Clients.Count);

        host.CloseEmulatorRoom();
        await Until(() => misty.EmulatorRoom is null && brock.EmulatorRoom is null && host.EmulatorRoom is null, "room closed for everyone");
    }

    [Fact]
    public void TunnelPackets_AreEncryptedWithTheRoomSecret()
    {
        byte[] secret = InviteCode.NewSecret();
        var codec = new MessageCodec(secret);
        byte[] packet = codec.Seal("enet"u8);
        Assert.Equal("enet"u8.ToArray(), codec.Open(packet));
        Assert.Throws<InvalidDataException>(() => new MessageCodec(InviteCode.NewSecret()).Open(packet));
        packet[^1] ^= 1;
        Assert.Throws<InvalidDataException>(() => codec.Open(packet));
    }
}
