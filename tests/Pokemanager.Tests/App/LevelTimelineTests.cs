using Pokemanager.App.Controls;

namespace Pokemanager.Tests.App;

/// <summary>The learn levels across the width: in proportion, labels alternating sides, none on top of another.</summary>
public sealed class LevelTimelineTests
{
    private static IReadOnlyList<TimelineMark> Lay(int[] levels, double width = 636) =>
        LevelTimeline.Layout(levels, width, l => l == 0 ? "Evo" : l.ToString(), t => t.Length * 7);

    [Fact]
    public void FirstAndLastLevel_SpanTheWholeWidth_InProportion()
    {
        var marks = Lay([1, 50, 100, 1, 50]);

        Assert.Equal([1, 50, 100], marks.Select(m => m.Level));
        Assert.Equal(LevelTimeline.Side, marks[0].X);
        Assert.Equal(636 - LevelTimeline.Side, marks[^1].X);
        Assert.Equal(LevelTimeline.Side + 49 * (636 - 2 * LevelTimeline.Side) / 99, marks[1].X, 3);
    }

    [Fact]
    public void LabelsAlternate_AndCloseOnesGoOneRowOut()
    {
        var marks = Lay([1, 50, 100]);
        Assert.Equal([true, false, true], marks.Select(m => m.Above));
        Assert.All(marks, m => Assert.Equal(0, m.Tier));

        // 1, 2, 3, 4 a pixel or so apart: two fit on the first rows, the next ones go one row out.
        var close = Lay([1, 2, 3, 4, 100], 300);
        Assert.Equal(1, close[2].Tier);
        Assert.Equal(1, close[3].Tier);
    }

    [Fact]
    public void NoLevels_NoMarks() => Assert.Empty(Lay([]));
}
