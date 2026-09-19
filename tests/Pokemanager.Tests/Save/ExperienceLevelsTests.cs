using PKHeX.Core;
using Pokemanager.Model.Data;
using Pokemanager.Save;

namespace Pokemanager.Tests.Save;

/// <summary>Experience under a level cap: what level it means, and how a save is fitted to a ROM's cap.</summary>
public class ExperienceLevelsTests
{
    private const byte MediumSlow = 3;

    private static PK7 Mon(uint exp, byte storedLevel = 0) => new() { Species = 498, EXP = exp, Stat_Level = storedLevel };

    [Fact]
    public void LevelOf_ReadsTheCodedLevels()
    {
        Assert.Equal(17, ExperienceLevels.LevelOf(Mon(Experience.GetEXP(17, MediumSlow)), MediumSlow));
        Assert.Equal(20, ExperienceLevels.LevelOf(Mon(ExperienceTable.CappedExperience(20)), MediumSlow));
        // The first prototype's values: the level stored in the party (the user's Tepig, exp 1073741826, level 20).
        Assert.Equal(20, ExperienceLevels.LevelOf(Mon(1073741826, storedLevel: 20), MediumSlow));
    }

    [Fact]
    public void Fit_KeepsAPokemonAboveTheCapAtItsLevel()
    {
        var tepig = Mon(1073741826, storedLevel: 20); // three Rare Candies over a cap of 18

        Assert.Equal(20, ExperienceLevels.Fit(tepig, MediumSlow, cap: 18));
        Assert.Equal(ExperienceTable.CappedExperience(20), tepig.EXP);
        Assert.Null(ExperienceLevels.Fit(tepig, MediumSlow, cap: 18)); // nothing more to do
    }

    [Fact]
    public void Fit_GivesTheOrdinaryExperienceBackWhenTheCapIsRaised()
    {
        var tepig = Mon(ExperienceTable.CappedExperience(20));

        Assert.Equal(20, ExperienceLevels.Fit(tepig, MediumSlow, cap: 24));
        Assert.Equal(Experience.GetEXP(20, MediumSlow), tepig.EXP);
    }

    [Fact]
    public void Fit_LeavesOrdinaryExperienceUnderTheCapAlone_AndCodesItAbove()
    {
        uint midLevel = Experience.GetEXP(17, MediumSlow) + 100;
        var under = Mon(midLevel);
        Assert.Null(ExperienceLevels.Fit(under, MediumSlow, cap: 18));
        Assert.Equal(midLevel, under.EXP);

        var over = Mon(Experience.GetEXP(30, MediumSlow));
        Assert.Equal(30, ExperienceLevels.Fit(over, MediumSlow, cap: 18));
        Assert.Equal(ExperienceTable.CappedExperience(30), over.EXP);
    }
}
