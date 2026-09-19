using System.Buffers.Binary;
using Pokemanager.Model.Data;

namespace Pokemanager.Tests.Editing;

/// <summary>The level cap on hand-made experience tables: what it changes, and what it refuses to touch.</summary>
public class ExperienceTableTests
{
    /// <summary>Medium Fast, as the games ship it: n³, 0 at levels 0 and 1.</summary>
    private static byte[] MediumFast()
    {
        byte[] file = new byte[ExperienceTable.Levels * 4];
        for (int level = 2; level < ExperienceTable.Levels; level++)
            BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(level * 4), (uint)(level * level * level));
        return file;
    }

    [Fact]
    public void Cap_KeepsTheLevelsUpToItAndPutsTheRestOutOfReach()
    {
        byte[][] files = [MediumFast(), MediumFast()];

        Assert.True(ExperienceTable.Cap(files, 20));

        foreach (byte[] file in files)
        {
            Assert.Equal(8000u, ExperienceTable.Read(file, 20));
            Assert.Equal(ExperienceTable.CappedExperience(21), ExperienceTable.Read(file, 21));
            Assert.Equal(ExperienceTable.CappedExperience(100), ExperienceTable.Read(file, 100));
            // A candy past the cap lands on a value that says its level, and the next level is 16.7 million away.
            Assert.Equal(21, ExperienceTable.LevelOfCapped(ExperienceTable.Read(file, 21)));
            Assert.Equal(ExperienceTable.CappedStep, ExperienceTable.Read(file, 22) - ExperienceTable.Read(file, 21));
            Assert.True(ExperienceTable.Read(file, 100) < int.MaxValue);
        }
    }

    [Fact]
    public void Cap_RefusesSomethingThatIsNotATable()
    {
        byte[] good = MediumFast();
        byte[] bad = MediumFast();
        BinaryPrimitives.WriteUInt32LittleEndian(bad.AsSpan(50 * 4), 1); // goes down at level 50
        byte[][] files = [good, bad];

        Assert.False(ExperienceTable.Cap(files, 20));
        Assert.Equal(MediumFast(), good); // not even the good one is changed
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void Cap_RefusesALevelThatIsNoCap(int cap) => Assert.False(ExperienceTable.Cap([MediumFast()], cap));
}
