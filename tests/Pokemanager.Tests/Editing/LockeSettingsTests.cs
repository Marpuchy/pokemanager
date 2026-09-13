using Pokemanager.Model.Projects;

namespace Pokemanager.Tests.Editing;

public sealed class LockeSettingsTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("pokemanager-locke-").FullName;

    public void Dispose() => Directory.Delete(dir, true);

    [Fact]
    public void Locke_RoundTripsWithTheProject_OldProjectsGetDefaults()
    {
        var project = new Project { DumpDirectory = @"C:\dump" };
        project.Locke.MaxLives = 5;
        project.Locke.LivesLost = 2;
        project.Locke.Prizes = [new(LockePrizeKind.Item, 50, 3, 4), new(LockePrizeKind.Money, Amount: 500)];
        project.Locke.RecordWin(0, project.Locke.Prizes[1], new DateTime(2026, 9, 14));
        string path = Path.Combine(dir, "p.json");
        project.Save(path);

        var loaded = Project.Load(path);

        Assert.Equal((5, 2, 3), (loaded.Locke.MaxLives, loaded.Locke.LivesLost, loaded.Locke.LivesLeft));
        Assert.Equal(project.Locke.Prizes, loaded.Locke.Prizes);
        Assert.Equal(LockePrizeKind.Money, loaded.Locke.SpinOf(0)!.Prize.Kind);
        Assert.Null(loaded.Locke.SpinOf(1));

        string old = Path.Combine(dir, "old.json");
        File.WriteAllText(old, """{"format":2,"dumpDirectory":"C:\\dump","language":"English"}""");
        Assert.Equal(LockeSettings.DefaultPrizes(), Project.Load(old).Locke.Prizes);
    }

    [Fact]
    public void Roll_FollowsTheWeights_AndIgnoresZeroWeights()
    {
        var locke = new LockeSettings { Prizes = [new(LockePrizeKind.Nothing, Weight: 3), new(LockePrizeKind.Life, Weight: 1), new(LockePrizeKind.Money, Weight: 0)] };
        var random = new Random(1234);
        var counts = new int[3];

        for (int i = 0; i < 40_000; i++)
            counts[locke.Roll(random)]++;

        Assert.Equal(0, counts[2]);
        Assert.InRange(counts[0] / (double)counts[1], 2.7, 3.3);
        Assert.Equal(-1, new LockeSettings { Prizes = [new(LockePrizeKind.Nothing, Weight: 0)] }.Roll(random));
    }

    [Fact]
    public void ItemPrize_StaysPendingUntilClaimed_AndCannotBeReRolledAway()
    {
        var locke = new LockeSettings();
        var candy = new LockePrize(LockePrizeKind.Item, 50, 3);

        locke.RecordWin(1, candy, DateTime.Now);
        Assert.False(locke.SpinOf(1)!.Claimed);

        locke.RecordWin(1, new LockePrize(LockePrizeKind.Money, Amount: 100), DateTime.Now); // replaces the unclaimed one only
        Assert.Single(locke.Spins);
        locke.MarkClaimed(1);
        Assert.True(locke.SpinOf(1)!.Claimed);

        locke.ForgetSpin(1);
        Assert.Null(locke.SpinOf(1));
    }

    [Fact]
    public void LifePrize_GivesBackALostLife_OrRaisesTheMaximum()
    {
        var locke = new LockeSettings { MaxLives = 3, LivesLost = 1 };
        var life = new LockePrize(LockePrizeKind.Life);

        locke.RecordWin(2, life, DateTime.Now);
        Assert.Equal((3, 0), (locke.MaxLives, locke.LivesLost));

        locke.RecordWin(3, life, DateTime.Now);
        Assert.Equal((4, 0), (locke.MaxLives, locke.LivesLost));
        Assert.Equal(2, locke.Spins.Count);
    }
}
