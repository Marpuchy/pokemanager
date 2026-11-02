using System.Text.Json.Nodes;
using Pokemanager.Model.Editing;
using Pokemanager.Model.Edits;
using Pokemanager.Model.Projects;

namespace Pokemanager.Tests.Editing;

/// <summary>
/// Starting the game over, on the project's side (<see cref="RunReset"/>): the run's progress always goes back, its
/// rules never do, and the ROM's rules only when they are asked for.
/// </summary>
public class RunResetTests
{
    private const string Label = "the run put back";

    /// <summary>
    /// A project in the middle of a run: four lives lost, two milestones spun, the cap on the fourth step of the plan,
    /// hand edits in three tables, a randomization, the shops changed and every game option set.
    /// </summary>
    private static EditorSession Played(SyntheticRomFs romfs)
    {
        var project = new Project { DumpDirectory = romfs.DumpDirectory };

        project.Locke.MaxLives = 6;
        project.Locke.LivesLost = 4;
        project.Locke.RouletteFirst = 2;
        project.Locke.RouletteLast = 7;
        project.Locke.RouletteEvery = 3;
        project.Locke.Prizes = [new(LockePrizeKind.Money, Amount: 500, Weight: 2), new(LockePrizeKind.Nothing)];
        project.Locke.Spins.Add(new LockeSpin(1, project.Locke.Prizes[0], new DateTime(2026, 9, 14)));
        project.Locke.Spins.Add(new LockeSpin(4, project.Locke.Prizes[1], new DateTime(2026, 9, 20)));

        // The plan's own levels are 1 here (the synthetic romfs has no trainer archive, so every boss reads 0 and the
        // step is clamped to 1): the player's own levels are what makes the steps tell each other apart.
        project.Tweaks.LevelCapByMilestones = true;
        project.Tweaks.LevelCapOverrides = new Dictionary<int, int> { [0] = 12, [3] = 37 };
        project.Tweaks.LevelCap = 37;
        project.Tweaks.InstalledLevelCap = 37;
        project.Tweaks.TrimOverCap = false;
        project.Tweaks.AlwaysShiny = true;
        project.Tweaks.TrainerLevelPercent = 20;
        project.Tweaks.WildLevelPercent = 10;
        project.Tweaks.StaticLevelPercent = 5;
        project.Tweaks.Difficulty = Pokemanager.Model.Difficulty.DifficultyLevel.Challenge;

        project.Randomization.Enabled = true;
        project.Randomization.Seed = 4242;
        project.Randomization.Preset = [1, 2, 3];
        project.Randomization.PresetName = "alex";
        project.Randomization.PresetModified = true;
        project.Randomization.AllowAllItems = true;
        project.Randomization.MegaStonesInPool = true;
        project.Randomization.OutputName = "run";
        project.Randomization.LastBuiltRom = @"C:\roms\run.cxi";
        project.Randomization.InstalledSeed = 4242;

        project.Shops.FreeRareCandies = true;
        project.Shops.MegaStonesOnSale = true;

        var session = EditorSession.Open(project);
        session.SetInt(GameTables.Personal, 1, "hp", 150);
        session.SetInt(GameTables.Personal, 2, "atk", 99);
        session.SetInt(GameTables.Moves, 1, "power", 120);
        session.Set(GameTables.MoveTexts, 1, GameTables.Description, JsonValue.Create("a move"));
        return session;
    }

    private static readonly RunResetOptions Nothing = new();

    private static readonly RunResetOptions Everything =
        new(TableEdits: true, Randomization: true, Shops: true, GameRules: true);

    [Fact]
    public void Reset_PutsBackTheRunsProgress_AndLeavesItsRulesAlone()
    {
        using var romfs = new SyntheticRomFs();
        var session = Played(romfs);
        var locke = session.Project.Locke;

        var report = RunReset.Apply(session, Nothing);

        Assert.Equal(0, locke.LivesLost);
        Assert.Empty(locke.Spins);
        Assert.Equal(6, locke.LivesLeft); // every life is there again

        // The rules of the run are how the player wants to play, not how far they got.
        Assert.Equal(6, locke.MaxLives);
        Assert.Equal((2, 7, 3), (locke.RouletteFirst, locke.RouletteLast, locke.RouletteEvery));
        Assert.Equal(2, locke.Prizes.Count);
        Assert.True(session.Project.Tweaks.LevelCapByMilestones);
        Assert.False(session.Project.Tweaks.TrimsOverCap);

        Assert.Equal((4, 2, true), (report.LivesLost, report.Spins, report.LevelCapStep));
        Assert.Equal(4, report.Count); // the lives, the two spins and the cap's step
    }

    /// <summary>
    /// A milestone that was spun can be spun again, which is the point of forgetting the spins: a new adventure has to
    /// earn its prizes.
    /// </summary>
    [Fact]
    public void Reset_LetsEveryMilestoneBeSpunAgain()
    {
        using var romfs = new SyntheticRomFs();
        var session = Played(romfs);
        Assert.NotNull(session.Project.Locke.SpinOf(1));

        RunReset.Apply(session, Nothing);

        Assert.Null(session.Project.Locke.SpinOf(1));
        Assert.Null(session.Project.Locke.SpinOf(4));
    }

    [Fact]
    public void Reset_WithNothingTicked_KeepsTheEditsTheRandomizationTheShopsAndTheGameOptions()
    {
        using var romfs = new SyntheticRomFs();
        var session = Played(romfs);

        var report = RunReset.Apply(session, Nothing);

        Assert.Equal(4, session.Project.Edits.Count);
        Assert.Equal(150, session.GetInt(GameTables.Personal, 1, "hp"));
        Assert.Equal(120, session.GetInt(GameTables.Moves, 1, "power"));

        var r = session.Project.Randomization;
        Assert.True(r.Enabled);
        Assert.Equal(4242, r.Seed);
        Assert.Equal(new byte[] { 1, 2, 3 }, r.Preset!);
        Assert.Equal("alex", r.PresetName);
        Assert.True(r.AllowAllItems);

        Assert.False(session.Project.Shops.IsEmpty);
        var tweaks = session.Project.Tweaks;
        Assert.True(tweaks.AlwaysShiny);
        Assert.Equal(20, tweaks.TrainerLevelPercent);
        Assert.Equal(Pokemanager.Model.Difficulty.DifficultyLevel.Challenge, tweaks.Difficulty);
        Assert.Equal(2, tweaks.LevelCapOverrides.Count);

        Assert.Equal((0, false, false, 0), (report.TableEdits, report.Randomization, report.Shops, report.GameRules));
    }

    [Fact]
    public void Reset_WithEverythingTicked_PutsBackTheRomsRules_KeepingWhatDescribesTheFileOnDisk()
    {
        using var romfs = new SyntheticRomFs();
        var session = Played(romfs);

        var report = RunReset.Apply(session, Everything);

        Assert.Equal(0, session.Project.Edits.Count);
        Assert.Equal(20, session.GetInt(GameTables.Personal, 1, "hp")); // what the romfs has
        Assert.Equal(41, session.GetInt(GameTables.Moves, 1, "power"));

        var r = session.Project.Randomization;
        Assert.False(r.Enabled);
        Assert.Equal(0, r.Seed);
        Assert.Null(r.Preset);
        Assert.Null(r.PresetName);
        Assert.False(r.PresetModified);
        Assert.False(r.AllowAllItems);
        Assert.False(r.MegaStonesInPool);
        // These say what the file on disk is, not how the run is played: a reset builds no ROM.
        Assert.Equal(@"C:\roms\run.cxi", r.LastBuiltRom);
        Assert.Equal(4242, r.InstalledSeed);
        Assert.Equal("run", r.OutputName);

        Assert.True(session.Project.Shops.IsEmpty);
        var tweaks = session.Project.Tweaks;
        Assert.False(tweaks.AlwaysShiny);
        Assert.Equal(0, tweaks.TrainerLevelPercent);
        Assert.Equal(0, tweaks.WildLevelPercent);
        Assert.Equal(0, tweaks.StaticLevelPercent);
        Assert.Null(tweaks.Difficulty);
        Assert.Empty(tweaks.LevelCapOverrides);
        // Still the rules of the cap, and still the truth about the ROM that was built.
        Assert.True(tweaks.LevelCapByMilestones);
        Assert.Equal(37, tweaks.InstalledLevelCap);

        Assert.Equal(4, report.TableEdits);
        Assert.True(report.Randomization && report.Shops);
        Assert.Equal(7, report.GameRules); // shiny, three percentages, the profile and two cap levels
        Assert.True(report.AffectsRom);
    }

    /// <summary>
    /// The cap goes back to the first step of the plan, not to null: a new adventure is capped at the first boss's
    /// level, and taking the cap off would be the opposite of what the plan is for.
    /// </summary>
    [Fact]
    public void Reset_TakesTheLevelCapBackToTheFirstStepOfThePlan()
    {
        using var romfs = new SyntheticRomFs();
        var session = Played(romfs);

        var report = RunReset.Apply(session, Nothing);

        Assert.Equal(12, session.Project.Tweaks.LevelCap); // the player's level for the first step
        Assert.True(report.LevelCapStep);
        // The ROM on disk still has the cap it was built with, and SaveUpdater trims experience against it.
        Assert.Equal(37, session.Project.Tweaks.InstalledLevelCap);
    }

    /// <summary>With the game options put back the plan has no levels of the player's, so the first step is its own.</summary>
    [Fact]
    public void Reset_WithTheGameOptionsTicked_TakesTheCapBackToThePlansOwnFirstStep()
    {
        using var romfs = new SyntheticRomFs();
        var session = Played(romfs);

        RunReset.Apply(session, Everything);

        Assert.Equal(1, session.Project.Tweaks.LevelCap); // no trainer archive here, so every computed step is 1
        Assert.Empty(session.Project.Tweaks.LevelCapOverrides);
    }

    /// <summary>A cap written by hand is a rule the player chose, not progress: starting over does not move it.</summary>
    [Fact]
    public void Reset_LeavesACapSetByHandAlone()
    {
        using var romfs = new SyntheticRomFs();
        var session = Played(romfs);
        session.Project.Tweaks.LevelCapByMilestones = false;
        session.Project.Tweaks.LevelCap = 50;

        var report = RunReset.Apply(session, Nothing);

        Assert.Equal(50, session.Project.Tweaks.LevelCap);
        Assert.False(report.LevelCapStep);
    }

    /// <summary>
    /// The whole reset is one thing the player did, so it is one step of undo — the same contract the difficulty
    /// profiles and the table sweeps have, and the reason the view model wraps it in a batch.
    /// </summary>
    [Fact]
    public void TheWholeReset_IsOneUndoStep_ThatBringsEverythingBack()
    {
        using var romfs = new SyntheticRomFs();
        var session = Played(romfs);
        byte[]? restored = null;
        var history = new SnapshotHistory(() => session.Project.CaptureState(), bytes => restored = bytes);
        history.Reset();
        byte[] before = session.Project.CaptureState();

        RunReset.Apply(session, Everything);
        history.Record(Label);

        Assert.Equal(1, history.UndoCount);
        Assert.NotEqual(before, session.Project.CaptureState());

        Assert.Equal(Label, history.Undo());
        Assert.Equal(0, history.UndoCount);
        Assert.Equal(before, restored!); // lives, spins, cap, edits, randomization and shops, all in one go
    }
}
