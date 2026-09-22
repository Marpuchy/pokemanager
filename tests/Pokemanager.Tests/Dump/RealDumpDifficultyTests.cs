using Pokemanager.Model.Data;
using Pokemanager.Model.Difficulty;
using Pokemanager.Model.Dump;
using Pokemanager.Model.Editing;
using Pokemanager.Model.Edits;
using Pokemanager.Model.Projects;

namespace Pokemanager.Tests.Dump;

/// <summary>
/// The difficulty profiles over the real Pokémon X dump (skipped without <c>POKEMANAGER_DUMP</c>): what a profile
/// writes, what it leaves alone, and that one applied over another is that profile and not the two piled up.
/// </summary>
public class RealDumpDifficultyTests
{
    private const string T = GameTables.Trainers;

    private static string RequireDump()
    {
        string? dumpDir = Environment.GetEnvironmentVariable("POKEMANAGER_DUMP");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(dumpDir), "POKEMANAGER_DUMP is not set.");
        Assert.SkipUnless(Directory.Exists(Path.Combine(dumpDir!, "romfs")), $"{dumpDir} does not contain romfs.");
        return dumpDir!;
    }

    private static EditorSession Open() => EditorSession.Open(new Project { DumpDirectory = RequireDump() });

    /// <summary>The first trainer of each kind that has a team, so the assertions are about a real record.</summary>
    private static int FirstOf(EditorSession session, TrainerDifficulty kind) =>
        Enumerable.Range(0, session.Current.Trainers.Length)
            .First(i => !session.Current.Trainers[i].Unreadable && session.Current.Trainers[i].Count > 0
                        && TrainerRoles.DifficultyOf(GameTitle.X, i) == kind);

    [Fact]
    public void Challenge_WritesTheAiAndTheIvsOfEachKindOfTrainer()
    {
        var session = Open();
        int ordinary = FirstOf(session, TrainerDifficulty.Regular);
        int boss = FirstOf(session, TrainerDifficulty.Boss);

        var result = DifficultyProfiles.Apply(session, DifficultyLevel.Challenge);

        Assert.True(result.Values > 1000, $"only {result.Values} values changed");
        Assert.Equal(0, result.PutBack); // nothing was applied before it

        // Bits 0-2 are the AI level; the byte keeps whatever else it held.
        Assert.Equal(0x03, session.GetInt(T, ordinary, "ai") & 0x07);
        Assert.Equal(0x07, session.GetInt(T, boss, "ai") & 0x07);
        // Generation 6 stores one byte per team member: IVs × 8 (12 → 96, 25 → 200).
        Assert.Equal(96, session.GetInt(T, ordinary, "p1.ivs"));
        Assert.Equal(200, session.GetInt(T, boss, "p1.ivs"));
        // A boss carries two Full Restores; an ordinary trainer's bag is not touched by this profile.
        Assert.Equal(DifficultyProfiles.FullRestore, session.GetInt(T, boss, "item1"));
        Assert.Equal(DifficultyProfiles.FullRestore, session.GetInt(T, boss, "item2"));
        Assert.Equal(0, session.GetInt(T, boss, "item3"));
    }

    /// <summary>
    /// A profile owns its fields and starts from the ROM's own values every time, so switching profiles is not the two
    /// of them together — which is what would happen if it wrote over whatever the last one left.
    /// </summary>
    [Fact]
    public void AProfileOverAnother_IsThatProfile_AndNormalPutsEverythingBack()
    {
        var session = Open();
        int boss = FirstOf(session, TrainerDifficulty.Boss);
        int aiBefore = session.GetInt(T, boss, "ai");
        int ivsBefore = session.GetInt(T, boss, "p1.ivs");

        DifficultyProfiles.Apply(session, DifficultyLevel.Nightmare);
        Assert.Equal(255, session.GetInt(T, boss, "p1.ivs"));
        Assert.Equal(DifficultyProfiles.FullRestore, session.GetInt(T, boss, "item4"));

        var relaxed = DifficultyProfiles.Apply(session, DifficultyLevel.Relaxed);
        Assert.True(relaxed.PutBack > 0, "nothing was put back before writing the second profile");
        Assert.Equal(0x01, session.GetInt(T, boss, "ai") & 0x07);
        Assert.Equal(48, session.GetInt(T, boss, "p1.ivs")); // 6 × 8, not Nightmare's 255
        Assert.Equal(DifficultyProfiles.FullRestore, session.GetInt(T, boss, "item1"));
        Assert.Equal(0, session.GetInt(T, boss, "item2")); // Relaxed leaves a boss one item, Nightmare's other three go

        var normal = DifficultyProfiles.Apply(session, DifficultyLevel.Normal);

        Assert.Equal(0, normal.Values);
        Assert.True(normal.PutBack > 0);
        Assert.Equal(aiBefore, session.GetInt(T, boss, "ai"));
        Assert.Equal(ivsBefore, session.GetInt(T, boss, "p1.ivs"));
        // And the project holds no trainer edit at all: the game is back to its own difficulty.
        Assert.DoesNotContain(session.Project.Edits.All, e => e.Table == T);
    }

    /// <summary>
    /// The profile writes the AI, the IVs and the bag. Everything else of a trainer is the player's or the
    /// randomizer's, and a hand-edited level survives a profile.
    /// </summary>
    [Fact]
    public void AProfile_LeavesTheTeamAndTheMoneyAlone()
    {
        var session = Open();
        int boss = FirstOf(session, TrainerDifficulty.Boss);
        int money = session.GetInt(T, boss, "money");
        int species = session.GetInt(T, boss, "p1.species");
        session.SetInt(T, boss, "p1.level", 77);

        DifficultyProfiles.Apply(session, DifficultyLevel.Nightmare);

        Assert.Equal(77, session.GetInt(T, boss, "p1.level"));
        Assert.Equal(money, session.GetInt(T, boss, "money"));
        Assert.Equal(species, session.GetInt(T, boss, "p1.species"));
    }

    /// <summary>
    /// The kinds a profile writes for, measured on X's own records: a "difficulty" that told them apart wrongly would
    /// be changing the wrong fights. Of the 151 trainers UPR ZX tags, 110 are a boss or an important one; the rest of
    /// the tags (a gym's own trainers, for instance) are ordinary fights.
    /// </summary>
    [Fact]
    public void TheThreeKindsOfTrainer_AreTheOnesTheGameShips()
    {
        var session = Open();
        var kinds = Enumerable.Range(0, session.Current.Trainers.Length)
            .Where(i => !session.Current.Trainers[i].Unreadable)
            .GroupBy(i => TrainerRoles.DifficultyOf(GameTitle.X, i))
            .ToDictionary(g => g.Key, g => g.Count());

        // Measured on the dump: 784 of the 785 records parse (the archive's dummy entry does not).
        Assert.Equal(16, kinds[TrainerDifficulty.Boss]);        // eight leaders, the Elite Four, the Champion, the post-game
        Assert.Equal(94, kinds[TrainerDifficulty.Important]);   // the rival, the friends and the named characters
        Assert.Equal(674, kinds[TrainerDifficulty.Regular]);
    }
}
