using System.Net;
using PKHeX.Core;
using Pokemanager.Multiplayer;
using Pokemanager.Save;
using Pokemanager.Tests.Editing;
using GameData = Pokemanager.Model.Data.GameData;

namespace Pokemanager.Tests.Multiplayer;

public sealed class RoomSessionTests : IDisposable
{
    private readonly SyntheticRomFs romfs = new();
    private readonly string dir = Directory.CreateTempSubdirectory("pokemanager-room-").FullName;

    /// <summary>Only this computer: no router, no internet.</summary>
    private static readonly RoomOptions Local = new() { MapPort = false, UseStun = false, IncludeLoopback = true };

    public void Dispose()
    {
        romfs.Dispose();
        Directory.Delete(dir, true);
    }

    private TrainerSnapshot Snapshot(string trainer, int partySize)
    {
        var sav = new SAV6XY { OT = trainer, Language = (int)LanguageID.English };
        string path = Path.Combine(dir, trainer);
        File.WriteAllBytes(path, sav.Write().ToArray());
        var doc = SaveDocument.Open(path, GameData.Load(romfs.RomFs));
        for (int i = 0; i < partySize; i++)
            doc.Set(new SaveSlot(null, i), doc.Create((ushort)(1 + (i % 4)), 10 + i));
        doc.Set(new SaveSlot(3, 7), doc.Create(2, 30));
        doc.SetMilestone(0, true);
        return TrainerSnapshot.Read(doc);
    }

    private static async Task Until(Func<bool> condition, string what)
    {
        var limit = DateTime.UtcNow.AddSeconds(15);
        while (!condition())
        {
            if (DateTime.UtcNow > limit)
                Assert.Fail("Timed out waiting for: " + what);
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public void InviteCode_RoundTripsAndRejectsTypos()
    {
        byte[] secret = InviteCode.NewSecret();
        IPEndPoint[] endpoints =
        [
            new(IPAddress.Parse("88.12.34.56"), 47800), new(IPAddress.Parse("192.168.1.20"), 47800),
            new(IPAddress.Parse("2a01:4f8:1c17:abcd::1"), 47800),
        ];
        string text = InviteCode.Invite(secret, endpoints).ToString();

        var parsed = InviteCode.Parse(" " + text.ToLowerInvariant().Replace('0', 'o') + "\n");

        Assert.StartsWith("PM-", text);
        Assert.False(parsed.IsAnswer);
        Assert.Equal(secret, parsed.Key);
        Assert.Equal(endpoints, parsed.Endpoints);
        char wrong = text[10] == 'A' ? 'B' : 'A';
        Assert.Throws<FormatException>(() => InviteCode.Parse(text[..10] + wrong + text[11..]));
        Assert.Throws<FormatException>(() => InviteCode.Parse(text[..^4]));

        var answer = InviteCode.Parse(InviteCode.Answer(secret, endpoints[..1]).ToString());
        Assert.True(answer.IsAnswer);
        Assert.Equal(InviteCode.RoomTag(secret), answer.Key);
    }

    [Fact]
    public void Codec_RoundTrips_AndRejectsOtherRoomsAndTampering()
    {
        byte[] secret = InviteCode.NewSecret();
        var codec = new MessageCodec(secret);
        var snapshot = Snapshot("Ash", 3);

        byte[] packet = codec.Encode(new SnapshotShared("p1", snapshot));
        var back = Assert.IsType<SnapshotShared>(codec.Decode(packet));

        Assert.Equal(3, back.Snapshot.Party!.Count);
        Assert.Equal(snapshot.Party![1].Stats, back.Snapshot.Party[1].Stats);
        Assert.Equal(2, back.Snapshot.Boxes![3].Slots[7]!.Species);
        Assert.Throws<InvalidDataException>(() => new MessageCodec(InviteCode.NewSecret()).Decode(packet));
        packet[^1] ^= 1;
        Assert.Throws<InvalidDataException>(() => codec.Decode(packet));
    }

    [Fact]
    public async Task Room_RelaysSavesLive_FollowsTheRules_AndNoticesWhoLeaves()
    {
        await using var host = await RoomSession.HostAsync("Duo", "host", new PlayerProfile("Ash", Sprite: "red", Color: "#D13438"), new RoomRules(), Local, cancel: TestContext.Current.CancellationToken);
        host.Publish(Snapshot("Ash", 2));

        await using var misty = await RoomSession.JoinAsync(host.InviteText!, "misty", new PlayerProfile("Misty", Sprite: "misty"), Local, TestContext.Current.CancellationToken);
        await Until(() => misty.Status == RoomStatus.Online, "Misty online");
        Assert.Equal("Duo", misty.RoomName);
        await Until(() => misty.Players.Any(p => p.Id == "host" && p.Snapshot?.Party?.Count == 2), "Misty sees Ash's party");
        Assert.True(misty.Players.Single(p => p.Id == "host").IsHost);
        Assert.Equal(("red", "#D13438"), (misty.Players.Single(p => p.Id == "host").Profile.Sprite, misty.Players.Single(p => p.Id == "host").Profile.Color));

        misty.Publish(Snapshot("Misty", 4));
        await using var brock = await RoomSession.JoinAsync(host.InviteText!, "brock", new PlayerProfile("Brock"), Local, TestContext.Current.CancellationToken);
        await Until(() => brock.Players.FirstOrDefault(p => p.Id == "misty")?.Snapshot?.Party?.Count == 4, "Brock gets Misty's earlier save");
        await Until(() => misty.Players.Any(p => p.Id == "brock" && p.Online), "Misty sees Brock join");

        // A new save is relayed at once to everyone.
        brock.Publish(Snapshot("Brock", 6));
        await Until(() => misty.Players.FirstOrDefault(p => p.Id == "brock")?.Snapshot?.Party?.Count == 6, "Misty sees Brock's save");
        await Until(() => host.Players.FirstOrDefault(p => p.Id == "brock")?.Snapshot?.Boxes?.Count > 0, "Ash sees Brock's boxes");

        // Boxes hidden by the host: they disappear for everyone, parties stay; allowed again: they come back.
        host.SetRules(new RoomRules(ShareParty: true, ShareBoxes: false));
        await Until(() => misty.Rules.ShareBoxes == false && misty.Players.Single(p => p.Id == "brock").Snapshot!.Boxes is null, "boxes hidden");
        Assert.NotNull(misty.Players.Single(p => p.Id == "brock").Snapshot!.Party);
        Assert.Null(host.Players.Single(p => p.Id == "brock").Snapshot!.Boxes);
        host.SetRules(new RoomRules());
        await Until(() => misty.Players.Single(p => p.Id == "brock").Snapshot!.Boxes is not null, "boxes shown again");

        // A new name and sprite reach everyone.
        misty.UpdateProfile(new PlayerProfile("Misty ★", Sprite: "misty-gen1"));
        await Until(() => brock.Players.Single(p => p.Id == "misty").Profile is { Name: "Misty ★", Sprite: "misty-gen1" }, "Brock sees Misty's new profile");
        Assert.Equal("Misty ★", host.Players.Single(p => p.Id == "misty").Name);

        await brock.DisposeAsync();
        await Until(() => misty.Players.Single(p => p.Id == "brock").Online == false, "Misty sees Brock leave");
        Assert.False(host.Players.Single(p => p.Id == "brock").Online);
    }

    [Fact]
    public async Task Join_WithoutAReachableHost_AsksForTheAnswerCode()
    {
        byte[] secret = InviteCode.NewSecret();
        string unreachable = InviteCode.Invite(secret, [new IPEndPoint(IPAddress.Loopback, 9)]).ToString();
        var options = new RoomOptions { MapPort = false, UseStun = false, IncludeLoopback = true, DirectConnectTimeout = TimeSpan.FromSeconds(1) };

        await using var guest = await RoomSession.JoinAsync(unreachable, "g", new PlayerProfile("Guest"), options, TestContext.Current.CancellationToken);
        await Until(() => guest.Status == RoomStatus.WaitingForAnswer, "answer code requested");

        var answer = InviteCode.Parse(guest.AnswerText!);
        Assert.True(answer.IsAnswer);
        Assert.Contains(answer.Endpoints, ep => ep.Port == guest.Port);
        await using var otherHost = await RoomSession.HostAsync("Other", "h", new PlayerProfile("Host"), new RoomRules(), options, cancel: TestContext.Current.CancellationToken);
        Assert.Throws<FormatException>(() => otherHost.AcceptAnswer(guest.AnswerText!));
    }
}
