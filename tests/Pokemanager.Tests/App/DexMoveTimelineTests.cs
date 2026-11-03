using Avalonia.Media;
using Pokemanager.App.ViewModels;

namespace Pokemanager.Tests.App;

/// <summary>The learnset as a timeline: one stop per level, in proportion, never overlapping.</summary>
public sealed class DexMoveTimelineTests
{
    private static (int, DexMoveLine) Move(int level, string name) =>
        (level, new DexMoveLine($"Nv. {level}", name, new TypeChip("Normal", Brushes.Gray, Brushes.White)));

    [Fact]
    public void MovesOfOneLevel_ShareAStop_InLevelOrder()
    {
        var stops = DexMoveTimeline.Build([Move(5, "b"), Move(1, "a"), Move(1, "c")]);

        Assert.Equal(["Nv. 1", "Nv. 5"], stops.Select(s => s.Level));
        Assert.Equal(["a", "c"], stops[0].Moves.Select(m => m.Move));
        Assert.Equal(0, stops[0].Gap.Left);
    }

    [Fact]
    public void FarLevels_AreSpacedInProportion_CloseOnesAreOnlyPushedApart()
    {
        var stops = DexMoveTimeline.Build([Move(1, "a"), Move(2, "b"), Move(40, "c")]);

        // Level 2 would sit 12 px in: it goes right after level 1's stop instead.
        Assert.Equal(DexMoveTimeline.MinimumGap, stops[1].Gap.Left);
        // Level 40 sits where its level puts it: 39 levels from the first.
        double secondEnd = DexMoveTimeline.StopWidth + DexMoveTimeline.MinimumGap + DexMoveTimeline.StopWidth;
        Assert.Equal(39 * DexMoveTimeline.PixelsPerLevel - secondEnd, stops[2].Gap.Left, 3);
    }

    [Fact]
    public void NoMoves_NoStops() => Assert.Empty(DexMoveTimeline.Build([]));
}
