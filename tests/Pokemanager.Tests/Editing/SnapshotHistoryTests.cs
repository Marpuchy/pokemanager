using System.Text;
using Pokemanager.Model.Editing;

namespace Pokemanager.Tests.Editing;

public sealed class SnapshotHistoryTests
{
    private string state = "a";
    private DateTime now = new(2026, 9, 15, 12, 0, 0);

    private SnapshotHistory NewHistory(int maxSteps = 2000) => new(() => Encoding.UTF8.GetBytes(state), b => state = Encoding.UTF8.GetString(b))
    {
        Clock = () => now,
        MaxSteps = maxSteps,
    };

    private void Change(SnapshotHistory history, string value, string label, double seconds = 5)
    {
        now = now.AddSeconds(seconds);
        state = value;
        history.Record(label);
    }

    [Fact]
    public void UndoAndRedo_WalkThroughEveryState()
    {
        var history = NewHistory();
        history.Reset();
        Change(history, "b", "hp");
        Change(history, "c", "atk");
        Change(history, "d", "def");

        Assert.Equal("def", history.Undo());
        Assert.Equal("c", state);
        Assert.Equal("atk", history.Undo());
        Assert.Equal("hp", history.Undo());
        Assert.Equal("a", state);
        Assert.Null(history.Undo());
        Assert.Equal("a", state);

        Assert.Equal("hp", history.Redo());
        Assert.Equal("b", state);
        Assert.Equal("atk", history.Redo());
        Assert.Equal("c", state);
        Assert.True(history.CanRedo);

        // A new change after undoing drops what could be redone.
        Change(history, "x", "spe");
        Assert.False(history.CanRedo);
        history.Undo();
        Assert.Equal("c", state);
    }

    [Fact]
    public void QuickChangesOfTheSameKind_AreOneStep_OthersAreNot()
    {
        var history = NewHistory();
        history.Reset();
        Change(history, "t", "description", 0.1);
        Change(history, "te", "description", 0.1);
        Change(history, "tex", "description", 0.1);
        Change(history, "texts", "name", 0.1);

        Assert.Equal(2, history.UndoCount);
        history.Undo();
        Assert.Equal("tex", state);
        history.Undo();
        Assert.Equal("a", state);
    }

    [Fact]
    public void NoChange_NoStep_AndTheOldestStepsAreDropped()
    {
        var history = NewHistory(maxSteps: 3);
        history.Reset();
        Change(history, "a", "same");
        Assert.False(history.CanUndo);

        for (int i = 0; i < 10; i++)
            Change(history, "v" + i, "step" + i);
        Assert.Equal(3, history.UndoCount);
        while (history.CanUndo)
            history.Undo();
        Assert.Equal("v6", state);
    }

    [Fact]
    public void BigStates_RoundTripExactly()
    {
        var random = new Random(7);
        byte[] data = new byte[450_000];
        random.NextBytes(data.AsSpan(0, 5000));
        byte[] current = (byte[])data.Clone();
        var history = new SnapshotHistory(() => (byte[])current.Clone(), b => current = b);
        history.Reset();
        current[100] = 1;
        history.Record("one");
        current[400_000] = 2;
        history.Record("two");

        history.Undo();
        history.Undo();
        Assert.Equal(data, current);
        history.Redo();
        Assert.Equal(1, current[100]);
        Assert.Equal(0, current[400_000]);
    }
}
