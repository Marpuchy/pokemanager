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

    /// <summary>
    /// The cap does not stop a Pokémon earning experience, and the game adds it to the coded value. Requiring an exact
    /// multiple made one battle read as level 100, which is what the whole encoding exists to avoid.
    /// </summary>
    [Fact]
    public void LevelOf_ReadsACodedLevelThroughTheExperienceEarnedSince()
    {
        uint coded = ExperienceTable.CappedExperience(20);

        Assert.Equal(20, ExperienceLevels.LevelOf(Mon(coded + 1), MediumSlow));
        Assert.Equal(20, ExperienceLevels.LevelOf(Mon(coded + 68_116), MediumSlow));       // the biggest step between two levels
        Assert.Equal(20, ExperienceLevels.LevelOf(Mon(coded + ExperienceTable.MostOrdinaryExperience), MediumSlow));
        // A whole table of experience away is not that level any more: the step is ten times the biggest table.
        Assert.Null(ExperienceTable.LevelOfCapped(coded + ExperienceTable.MostOrdinaryExperience + 1));
        // And the levels still cannot be confused with each other.
        Assert.Equal(21, ExperienceLevels.LevelOf(Mon(ExperienceTable.CappedExperience(21)), MediumSlow));
    }

    [Fact]
    public void Trim_DropsWhatWasBankedAtTheCapAndKeepsTheRest()
    {
        // A Pokémon the cap held at 18 that went on battling: the ordinary table reads that as level 25.
        var grinder = Mon(Experience.GetEXP(25, MediumSlow));
        Assert.Equal(25, ExperienceLevels.LevelOf(grinder, MediumSlow));

        Assert.Equal((18, 25), ExperienceLevels.Trim(grinder, MediumSlow, playedCap: 18));

        Assert.Equal(Experience.GetEXP(18, MediumSlow), grinder.EXP);
        Assert.Equal(18, ExperienceLevels.LevelOf(grinder, MediumSlow));
        // And nothing more to drop the second time.
        Assert.Null(ExperienceLevels.Trim(grinder, MediumSlow, playedCap: 18));
    }

    [Fact]
    public void Trim_LeavesAloneWhatTheCapNeverHeld()
    {
        // Under the cap, the progress within the level is the player's.
        uint halfway = Experience.GetEXP(12, MediumSlow) + 500;
        var young = Mon(halfway);
        Assert.Null(ExperienceLevels.Trim(young, MediumSlow, playedCap: 18));
        Assert.Equal(halfway, young.EXP);

        // A ROM without a cap banks nothing.
        var grown = Mon(Experience.GetEXP(40, MediumSlow));
        Assert.Null(ExperienceLevels.Trim(grown, MediumSlow, playedCap: null));

        // A level the cap coded is not an amount: a Rare Candy took it over the cap and it keeps its level.
        var candied = Mon(ExperienceTable.CappedExperience(20) + 900);
        Assert.Null(ExperienceLevels.Trim(candied, MediumSlow, playedCap: 18));
        Assert.Equal(20, ExperienceLevels.LevelOf(candied, MediumSlow));
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

    /// <summary>
    /// Level 48 of the encoding is exactly the first prototype's base, and from there the old rule read every coded level
    /// as a prototype value: in a box, where nothing else says the level, that answered 100 (it put six of the user's
    /// boxed Pokémon at level 100).
    /// </summary>
    [Theory]
    [InlineData(47)]
    [InlineData(48)]
    [InlineData(52)]
    [InlineData(60)]
    [InlineData(100)]
    public void LevelOf_ReadsHighCodedLevels_EvenInABoxWithNoStoredLevel(int level)
    {
        var boxed = Mon(ExperienceTable.CappedExperience(level));

        Assert.Equal(level, ExperienceLevels.LevelOf(boxed, MediumSlow));
        Assert.Null(ExperienceLevels.Fit(boxed, MediumSlow, cap: level - 1)); // already right for a ROM capped under it
    }

    [Fact]
    public void Prototype_ValuesAreStillTold_FromTheCodedOnes()
    {
        Assert.True(ExperienceTable.IsPrototype(ExperienceTable.PrototypeBase + 2));
        Assert.False(ExperienceTable.IsPrototype(ExperienceTable.CappedExperience(48)));
        Assert.Equal(48, ExperienceTable.LevelOfCapped(ExperienceTable.CappedExperience(48)));
        Assert.Null(ExperienceTable.LevelOfCapped(ExperienceTable.PrototypeBase + 2));
    }
}
