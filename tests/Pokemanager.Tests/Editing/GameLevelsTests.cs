using pk3DS.Core.CTR;
using Pokemanager.Model.Data;

namespace Pokemanager.Tests.Editing;

/// <summary>The level modifiers on hand-made Generation 7 tables.</summary>
public class GameLevelsTests
{
    [Theory]
    [InlineData(12, 20, 14)]    // 14.4
    [InlineData(14, 20, 17)]    // 16.8
    [InlineData(10, 25, 13)]    // 12.5 rounds up, as UPR ZX does
    [InlineData(90, 20, 100)]   // never above 100
    [InlineData(1, -50, 1)]     // never below 1
    [InlineData(37, 0, 37)]
    public void Scale_RoundsLikeTheRandomizer(int level, double percent, int expected) =>
        Assert.Equal(expected, GameLevels.Scale(level, percent));

    /// <summary>A +20 % is undone by −16.67 %: the second randomization of the user's ROM, backwards.</summary>
    [Fact]
    public void Scale_UndoesATwentyPercentRise()
    {
        for (int level = 2; level <= 83; level++)
            Assert.Equal(level, GameLevels.Scale(GameLevels.Scale(level, 20), (100 / 1.2) - 100));
    }

    [Fact]
    public void Statics_LeaveTheStartersAndTheEggsAlone()
    {
        byte[] gifts = new byte[5 * GameLevels.GiftSize];
        byte[] levels = [5, 5, 5, 20, 1];
        for (int i = 0; i < levels.Length; i++)
            gifts[(i * GameLevels.GiftSize) + 3] = levels[i];
        byte[] encounters = new byte[2 * GameLevels.EncounterSize];
        encounters[3] = 12;
        encounters[GameLevels.EncounterSize + 3] = 50;

        int changed = GameLevels.ScaleStatics7([gifts, encounters, []], 20);

        Assert.Equal(3, changed);
        Assert.Equal([5, 5, 5, 24, 1], Enumerable.Range(0, 5).Select(i => gifts[(i * GameLevels.GiftSize) + 3]));
        Assert.Equal(14, encounters[3]);
        Assert.Equal(60, encounters[GameLevels.EncounterSize + 3]);
    }

    [Fact]
    public void Wild_ScalesTheDayAndNightRangesOfEveryArea()
    {
        byte[] Table(byte min, byte max)
        {
            byte[] table = new byte[4 + (2 * 0x164)];
            table[4] = min; table[5] = max;
            table[4 + 0x164] = min; table[5 + 0x164] = max;
            return table;
        }
        var files = new byte[GameLevels.FirstArea + GameLevels.AreaStride + 1][];
        for (int i = 0; i < files.Length; i++)
            files[i] = [1, 2, 3];   // not areas: never touched
        files[GameLevels.FirstArea] = LZSS.Compress(Mini.PackMini([Table(10, 12), Table(20, 25)], "EA"));
        files[GameLevels.FirstArea + GameLevels.AreaStride] = LZSS.Compress(Mini.PackMini([Table(30, 30)], "EA"));

        int changed = GameLevels.ScaleWild7(files, 20);

        Assert.Equal(12, changed);
        var first = Mini.UnpackMini(LZSS.Decompress(files[GameLevels.FirstArea]), "EA");
        Assert.Equal([12, 14], first[0][4..6]);
        Assert.Equal([24, 30], first[1][(4 + 0x164)..(6 + 0x164)]);
        Assert.Equal([36, 36], Mini.UnpackMini(LZSS.Decompress(files[GameLevels.FirstArea + GameLevels.AreaStride]), "EA")[0][4..6]);
        Assert.Equal([1, 2, 3], files[1]);
    }
}
