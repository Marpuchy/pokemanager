using Pokemanager.Model.Difficulty;
using Pokemanager.Model.Dump;
using Pokemanager.Model.Editing;
using Pokemanager.Model.Projects;

namespace Pokemanager.Tests.Editing;

/// <summary>
/// Challenge took the Expert bit from the ordinary trainers Ultra Sun/Ultra Moon ship at 0x05/0x07, and they attacked at
/// random (2026-11-10, user). The harder profiles only add bits now, up to 0x87.
/// </summary>
public sealed class DifficultyAiTests
{
    [Fact]
    public void RealUltraSun_HarderProfilesNeverLowerTheAi()
    {
        string? dump = Environment.GetEnvironmentVariable("POKEMANAGER_USUM_DUMP");
        Assert.SkipWhen(string.IsNullOrEmpty(dump), "POKEMANAGER_USUM_DUMP is not set.");
        var session = EditorSession.Open(new Project { DumpDirectory = dump!, Game = GameTitle.UltraSun }, null);

        foreach (var level in new[] { DifficultyLevel.Challenge, DifficultyLevel.Nightmare })
        {
            DifficultyProfiles.Apply(session, level);
            for (int id = 0; id < session.Current.Trainers.Length; id++)
            {
                var original = session.Original.Trainers[id];
                var now = session.Current.Trainers[id];
                if (original is null || now is null)
                    continue;
                Assert.Equal(0, original.Ai & ~now.Ai);           // nothing the ROM had is taken
                Assert.Equal(0x87, now.Ai & 0x87);                // Expert + bit 7 for everyone
            }
        }

        // Relaxed still lowers: that is what it is for.
        DifficultyProfiles.Apply(session, DifficultyLevel.Relaxed);
        Assert.Contains(Enumerable.Range(0, session.Current.Trainers.Length),
            id => session.Original.Trainers[id] is { } o && session.Current.Trainers[id] is { } n && (n.Ai & 0x07) < (o.Ai & 0x07));
    }

    /// <summary>Trainers at +65 %: Challenge → Normal → Challenge left them at +5 % (2026-11-10, user).</summary>
    [Fact]
    public void ChangingProfile_MovesTheLevelPercentByTheDifference()
    {
        double percent = 65;                                                   // set by the player under Challenge
        percent = DifficultyProfiles.LevelPercentAfter(percent, DifficultyLevel.Challenge, DifficultyLevel.Normal);
        Assert.Equal(60, percent);
        percent = DifficultyProfiles.LevelPercentAfter(percent, null, DifficultyLevel.Challenge);
        Assert.Equal(65, percent);
        Assert.Equal(15, DifficultyProfiles.LevelPercentAfter(0, null, DifficultyLevel.Nightmare));
    }
}
