namespace Pokemanager.Multiplayer;

/// <summary>
/// Notices when the game writes its save file: a file system watcher for speed and a poll of size and write time as a
/// safety net (watchers miss events on some folders). Raises <see cref="Saved"/> once the file has been quiet for a moment,
/// so a save in progress is not read half written.
/// </summary>
public sealed class SaveWatcher : IDisposable
{
    private readonly string path;
    private readonly FileSystemWatcher? watcher;
    private readonly Timer timer;
    private readonly TimeSpan quiet;
    private (long Length, DateTime Written) seen;
    private DateTime changedAt = DateTime.MaxValue;
    private int disposed;

    /// <summary>Raised on a worker thread after the save changed.</summary>
    public event Action? Saved;

    public SaveWatcher(string path, TimeSpan? quiet = null, TimeSpan? poll = null)
    {
        this.path = Path.GetFullPath(path);
        this.quiet = quiet ?? TimeSpan.FromSeconds(1.5);
        seen = Stamp();
        string? directory = Path.GetDirectoryName(this.path);
        if (directory is not null && Directory.Exists(directory))
        {
            watcher = new FileSystemWatcher(directory, Path.GetFileName(this.path))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                EnableRaisingEvents = true,
            };
            watcher.Changed += (_, _) => Touch();
            watcher.Created += (_, _) => Touch();
            watcher.Renamed += (_, _) => Touch();
        }
        var every = poll ?? TimeSpan.FromSeconds(2);
        timer = new Timer(_ => Tick(), null, every / 4, every / 4);
    }

    private (long, DateTime) Stamp()
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? (info.Length, info.LastWriteTimeUtc) : (-1, default);
        }
        catch (IOException)
        {
            return seen;
        }
    }

    private void Touch() => changedAt = DateTime.UtcNow;

    private void Tick()
    {
        var stamp = Stamp();
        if (stamp != seen)
        {
            seen = stamp;
            Touch();
        }
        if (changedAt != DateTime.MaxValue && DateTime.UtcNow - changedAt >= quiet && stamp.Item1 >= 0)
        {
            changedAt = DateTime.MaxValue;
            if (disposed == 0)
                Saved?.Invoke();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 1)
            return;
        watcher?.Dispose();
        timer.Dispose();
    }
}
