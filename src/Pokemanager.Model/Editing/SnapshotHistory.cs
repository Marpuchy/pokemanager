using System.IO.Compression;

namespace Pokemanager.Model.Editing;

/// <summary>
/// Undo and redo by snapshots: after every change the owner captures its whole state; the previous state is kept,
/// compressed, so any number of steps back can be restored exactly, whatever the change was. Consecutive changes with
/// the same label close together in time (typing, a spinner held down) count as one step.
/// </summary>
/// <remarks>
/// States are compared as bytes, so a change that ends in the state it started from adds no step. Memory is bounded by
/// <see cref="MaxSteps"/> and <see cref="MaxBytes"/>: the oldest steps are dropped first.
/// </remarks>
public sealed class SnapshotHistory
{
    private readonly Func<byte[]> capture;
    private readonly Action<byte[]> restore;
    private readonly LinkedList<Step> undo = new();
    private readonly Stack<Step> redo = new();
    private byte[] current = [];
    private DateTime lastChange;
    private string? lastLabel;
    private long bytes;

    private sealed record Step(string Label, byte[] State);

    /// <param name="capture">The whole state as bytes.</param>
    /// <param name="restore">Puts a captured state back.</param>
    public SnapshotHistory(Func<byte[]> capture, Action<byte[]> restore)
    {
        this.capture = capture;
        this.restore = restore;
    }

    public int MaxSteps { get; init; } = 2000;

    /// <summary>Total compressed size kept for undo and redo.</summary>
    public long MaxBytes { get; init; } = 512L * 1024 * 1024;

    /// <summary>Changes of the same kind within this time are merged into one step.</summary>
    public TimeSpan MergeWindow { get; init; } = TimeSpan.FromMilliseconds(900);

    /// <summary>Time source (tests).</summary>
    public Func<DateTime> Clock { get; init; } = () => DateTime.UtcNow;

    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;
    public int UndoCount => undo.Count;
    public int RedoCount => redo.Count;

    /// <summary>What the next undo reverts, or null.</summary>
    public string? UndoLabel => undo.Last?.Value.Label;

    public string? RedoLabel => redo.TryPeek(out var step) ? step.Label : null;

    /// <summary>Raised after every change of the stacks.</summary>
    public event Action? Changed;

    /// <summary>Starts over from the current state (after opening or reloading): nothing to undo.</summary>
    public void Reset()
    {
        undo.Clear();
        redo.Clear();
        bytes = 0;
        current = Compress(capture());
        lastLabel = null;
        Changed?.Invoke();
    }

    /// <summary>The state changed: the state before becomes an undo step (or joins the last one).</summary>
    public void Record(string label)
    {
        byte[] now = Compress(capture());
        if (now.AsSpan().SequenceEqual(current))
            return;
        var at = Clock();
        bool merge = undo.Count > 0 && redo.Count == 0 && label == lastLabel && at - lastChange < MergeWindow;
        if (!merge)
        {
            undo.AddLast(new Step(label, current));
            bytes += current.Length;
        }
        foreach (var step in redo)
            bytes -= step.State.Length;
        redo.Clear();
        current = now;
        lastLabel = label;
        lastChange = at;
        Trim();
        Changed?.Invoke();
    }

    /// <summary>Goes back one step. Returns its label, or null when there is nothing to undo.</summary>
    public string? Undo()
    {
        if (undo.Last is not { } node)
            return null;
        undo.RemoveLast();
        var step = node.Value;
        redo.Push(step with { State = current });
        bytes += current.Length - step.State.Length;
        current = step.State;
        lastLabel = null;
        restore(Decompress(current));
        Changed?.Invoke();
        return step.Label;
    }

    /// <summary>Goes forward one undone step. Returns its label, or null.</summary>
    public string? Redo()
    {
        if (!redo.TryPop(out var step))
            return null;
        undo.AddLast(step with { State = current });
        bytes += current.Length - step.State.Length;
        current = step.State;
        lastLabel = null;
        restore(Decompress(current));
        Changed?.Invoke();
        return step.Label;
    }

    private void Trim()
    {
        while (undo.Count > 0 && (undo.Count > MaxSteps || bytes > MaxBytes))
        {
            bytes -= undo.First!.Value.State.Length;
            undo.RemoveFirst();
        }
    }

    private static byte[] Compress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var brotli = new BrotliStream(output, CompressionLevel.Fastest))
            brotli.Write(data);
        return output.ToArray();
    }

    private static byte[] Decompress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var brotli = new BrotliStream(new MemoryStream(data), CompressionMode.Decompress))
            brotli.CopyTo(output);
        return output.ToArray();
    }
}
