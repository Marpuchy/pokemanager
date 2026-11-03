using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Pokemanager.Model.Dump;
using Pokemanager.Model.Resources;

namespace Pokemanager.Model.Mods;

/// <summary>What the downloader is doing right now, for a progress line that says more than a percentage.</summary>
public enum ModDownloadStage
{
    Downloading,
    Unpacking,
}

/// <summary>
/// How far one download has got. <see cref="Total"/> is null when the server declared no length — which is exactly
/// what Google Drive does for a large file — and the interface then shows an indeterminate bar and the bytes so far
/// instead of a percentage it would have to invent.
/// </summary>
/// <param name="Label">The file being fetched, as the user would name it.</param>
/// <param name="Index">Which of the planned downloads this is, from 1.</param>
/// <param name="Count">How many were planned.</param>
public sealed record ModDownloadProgress(
    ModDownloadStage Stage,
    string Label,
    int Index,
    int Count,
    long Received,
    long? Total)
{
    /// <summary>0 to 1, or null when the total is unknown.</summary>
    public double? Fraction => Total is > 0 ? Math.Clamp((double)Received / Total.Value, 0, 1) : null;
}

/// <summary>How one planned download ended.</summary>
public enum ModDownloadStatus
{
    /// <summary>The library already held it, so the network was never touched.</summary>
    AlreadyThere,

    /// <summary>It was fetched and is now in the library.</summary>
    Done,

    /// <summary>
    /// It was fetched and the bytes are not what the catalogue describes. Thrown away: nothing in the library was
    /// touched, because a file that fails its hash is not a file, it is a 200 from the wrong thing.
    /// </summary>
    WrongHash,

    /// <summary>The automatic path did not work. The user is told what happened and sent to the page.</summary>
    Failed,

    /// <summary>The user stopped it. The part that was written is gone and the library is as it was.</summary>
    Cancelled,
}

/// <summary>
/// What became of one planned download: enough for the interface to say what happened and what to do, and enough to
/// fill a hash into the catalogue afterwards.
/// </summary>
/// <param name="Sha256">
/// What was actually downloaded, upper-case hex — <b>computed whether or not the catalogue knew a hash</b>, so an
/// unverifiable release can be measured once and written down.
/// </param>
/// <param name="Message">A sentence saying what happened, in the user's language.</param>
public sealed record ModDownloadOutcome(
    ModDownload Download,
    ModDownloadStatus Status,
    string Message)
{
    public string? Sha256 { get; init; }

    public long Bytes { get; init; }

    /// <summary>Served paths this download put in the library (many for an archive, one for a file, none on failure).</summary>
    public IReadOnlyList<string> Written { get; init; } = [];

    /// <summary>The page to open by hand when the automatic path failed, or null.</summary>
    public string? PageUrl { get; init; }

    public bool Ok => Status is ModDownloadStatus.Done or ModDownloadStatus.AlreadyThere;
}

/// <summary>Everything that was attempted for one mod.</summary>
public sealed record ModDownloadReport(ModDefinition Mod, GameTitle Game, IReadOnlyList<ModDownloadOutcome> Outcomes)
{
    public bool Cancelled => Outcomes.Any(o => o.Status == ModDownloadStatus.Cancelled);

    /// <summary>Nothing was fetched because the library already held all of it.</summary>
    public bool NothingToDo => Outcomes.Count > 0 && Outcomes.All(o => o.Status == ModDownloadStatus.AlreadyThere);

    public IEnumerable<ModDownloadOutcome> Problems => Outcomes.Where(o => !o.Ok && o.Status != ModDownloadStatus.Cancelled);

    public int FilesWritten => Outcomes.Sum(o => o.Written.Count);

    /// <summary>The first thing that went wrong, which is the one worth putting in front of the user.</summary>
    public ModDownloadOutcome? FirstProblem => Problems.FirstOrDefault();
}

/// <summary>
/// Fetches a mod's files from the author's own sources into the user's own library, by a chain that degrades instead of
/// failing: a GitHub release asset, then — only for what does not fit in a repository — Google Drive, and behind both
/// the user's browser.
/// </summary>
/// <remarks>
/// <para>
/// <b>Pokemanager downloads from the author's source; it does not host, mirror or cache anything to pass on.</b> The
/// library is per user and per machine, so this is one user's own download, the one they would otherwise have made by
/// hand — not a redistribution and not one copy per project.
/// </para>
/// <para>
/// Everything is written to a <c>.partial</c> and renamed only once it is whole and, where a hash is known, right —
/// the same rule <c>RomBuilder</c> and <c>ProjectHistory</c> already follow, so an interrupted download never looks
/// like a good file. The hash is computed as the bytes go past, so a 2.3 GB file is not read twice.
/// </para>
/// <para>
/// <b>Google Drive is assumed to break.</b> For a large file Drive answers with an HTML confirmation page whose token
/// has to be sent back with its cookie; that page is Google's and changes without notice. When it does, this fails
/// after one retry with a sentence naming the page to open — it never loops.
/// </para>
/// </remarks>
public sealed partial class ModDownloader(IModFetcher fetcher, ModLibrary library)
{
    /// <summary>How much of Drive's confirmation page is read before giving up on finding a token in it.</summary>
    private const int MaxHtmlBytes = 1 << 20;

    private const int BufferBytes = 1 << 20;

    /// <summary>The real thing, over the shared <see cref="HttpModFetcher"/>.</summary>
    public static ModDownloader For(ModLibrary library) => new(new HttpModFetcher(), library);

    [GeneratedRegex(@"confirm=([0-9A-Za-z_\-]+)")]
    private static partial Regex ConfirmToken();

    [GeneratedRegex(@"name=""uuid""\s+value=""([0-9A-Za-z\-]+)""")]
    private static partial Regex UuidField();

    /// <summary>
    /// Fetches everything the library is missing for a mod, in catalogue order, and says what happened to each piece.
    /// Never throws for a source that failed: a failure is an outcome with a sentence in it, because the point of the
    /// chain is to carry on and tell the user what is left to do by hand.
    /// </summary>
    public async Task<ModDownloadReport> DownloadAsync(
        ModDefinition mod,
        GameTitle game,
        IProgress<ModDownloadProgress>? progress = null,
        CancellationToken cancel = default)
    {
        var plan = mod.DownloadsFor(game);
        var installation = library.Inspect(mod);
        var outcomes = new List<ModDownloadOutcome>();

        for (int i = 0; i < plan.Count; i++)
        {
            var download = plan[i];
            if (cancel.IsCancellationRequested)
            {
                outcomes.Add(Cancelled(download));
                break;
            }

            if (Satisfied(mod, game, download, installation))
            {
                outcomes.Add(new ModDownloadOutcome(download, ModDownloadStatus.AlreadyThere,
                    string.Format(Strings.Mods_Download_AlreadyThere, download.Label)));
                continue;
            }

            var outcome = await OneAsync(mod, download, i + 1, plan.Count, progress, cancel);
            outcomes.Add(outcome);
            if (outcome.Status == ModDownloadStatus.Cancelled)
                break;
        }

        return new ModDownloadReport(mod, game, outcomes);
    }

    /// <summary>
    /// Whether the library already holds what this download would bring, so it is not fetched again.
    /// </summary>
    /// <remarks>
    /// A file whose hash the catalogue knows is skipped only when it <b>matches</b> — a file that is there and wrong
    /// is downloaded again. With no hash known anywhere, presence is all there is to go on, and the declared size is
    /// used as the weak check it is: re-fetching 2.3 GB to overwrite bytes that are probably identical would be worse
    /// than admitting the file cannot be confirmed, which <see cref="ModInstallation.VersionText"/> already says out
    /// loud.
    /// </remarks>
    private static bool Satisfied(ModDefinition mod, GameTitle game, ModDownload download, ModInstallation installation)
    {
        if (download.Kind == ModDownloadKind.Archive)
        {
            var paths = download.Provides.Count > 0 ? download.Provides : mod.PathsFromArchive(game);
            return paths.Count > 0 && paths.All(p => installation.Files.Any(f =>
                string.Equals(f.ServedPath, p, StringComparison.OrdinalIgnoreCase) && f.Present));
        }

        if (download.ServedPath is not { } served)
            return false;
        var status = installation.Files.FirstOrDefault(f =>
            string.Equals(f.ServedPath, served, StringComparison.OrdinalIgnoreCase));
        if (status is null || !status.Present)
            return false;
        if (download.Sha256 is { } expected)
            return string.Equals(status.Sha256, expected, StringComparison.OrdinalIgnoreCase);
        if (status.State == ModFileState.Verified)
            return true;
        return download.Bytes is not { } size || status.Bytes == size;
    }

    private static ModDownloadOutcome Cancelled(ModDownload download) =>
        new(download, ModDownloadStatus.Cancelled, string.Format(Strings.Mods_Download_Cancelled, download.Label));

    private async Task<ModDownloadOutcome> OneAsync(
        ModDefinition mod,
        ModDownload download,
        int index,
        int count,
        IProgress<ModDownloadProgress>? progress,
        CancellationToken cancel)
    {
        if (!download.Source.CanFetch)
        {
            return new ModDownloadOutcome(download, ModDownloadStatus.Failed,
                string.Format(Strings.Mods_Download_NoAutomaticSource, download.Label, download.Source.HostText))
            { PageUrl = download.Source.PageUrl };
        }

        // Staged inside the mod's own folder, so a half-written 2.3 GB file is on the same disk as its destination and
        // the rename at the end is a rename and not a copy across volumes.
        string staging = Path.Combine(library.DirectoryFor(mod.Id), ".downloads");
        string partial = Path.Combine(staging, SafeName(download.Label) + ".partial");

        try
        {
            Directory.CreateDirectory(staging);
            var fetched = await FetchAsync(download, progress, index, count, partial, cancel);

            if (download.Sha256 is { } expected && !string.Equals(fetched.Sha256, expected, StringComparison.OrdinalIgnoreCase))
            {
                Delete(partial);
                return new ModDownloadOutcome(download, ModDownloadStatus.WrongHash,
                    string.Format(Strings.Mods_Download_WrongHash, download.Label, fetched.Sha256, expected))
                {
                    Sha256 = fetched.Sha256,
                    Bytes = fetched.Bytes,
                    PageUrl = download.Source.PageUrl,
                };
            }

            IReadOnlyList<string> written = download.Kind == ModDownloadKind.Archive
                ? Unpack(mod, partial, download, index, count, progress, cancel)
                : [Place(mod, partial, download.ServedPath!)];

            return new ModDownloadOutcome(download, ModDownloadStatus.Done,
                download.Kind == ModDownloadKind.Archive
                    ? string.Format(Strings.Mods_Download_Unpacked, download.Label, written.Count)
                    : string.Format(Strings.Mods_Download_Done, download.Label, download.ServedPath))
            {
                Sha256 = fetched.Sha256,
                Bytes = fetched.Bytes,
                Written = written,
            };
        }
        catch (OperationCanceledException)
        {
            Delete(partial);
            return Cancelled(download);
        }
        catch (ModFetchException ex)
        {
            Delete(partial);
            return new ModDownloadOutcome(download, ModDownloadStatus.Failed,
                string.Format(Strings.Mods_Download_SourceFailed, download.Label, download.Source.HostText, ex.Message))
            { PageUrl = download.Source.PageUrl };
        }
        catch (InvalidDataException ex)
        {
            Delete(partial);
            return new ModDownloadOutcome(download, ModDownloadStatus.Failed,
                string.Format(Strings.Mods_Download_BadArchive, download.Label, ex.Message))
            { PageUrl = download.Source.PageUrl };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Delete(partial);
            return new ModDownloadOutcome(download, ModDownloadStatus.Failed,
                string.Format(Strings.Mods_Download_DiskFailed, download.Label, ex.Message))
            { PageUrl = download.Source.PageUrl };
        }
        finally
        {
            TidyStaging(staging);
        }
    }

    private sealed record Fetched(string Sha256, long Bytes);

    /// <summary>
    /// The chain for one file: ask the source, and for Drive get past its confirmation page once. Writes to
    /// <paramref name="partial"/> and hashes as it goes.
    /// </summary>
    private async Task<Fetched> FetchAsync(
        ModDownload download,
        IProgress<ModDownloadProgress>? progress,
        int index,
        int count,
        string partial,
        CancellationToken cancel)
    {
        var uri = download.Source.DownloadUri!;
        using var response = await fetcher.GetAsync(uri, null, cancel);

        if (!response.LooksLikeHtml)
            return await SaveAsync(response, download, progress, index, count, partial, cancel);

        if (download.Source.Kind != ModSourceKind.GoogleDrive)
            throw new ModFetchException(Strings.Mods_Download_GotAPage);

        // Drive's confirmation page: take the token and the cookie it set and ask once more, the modern way (the
        // download host, with confirm=t). One retry, then it is a message — never a loop.
        string html = await ReadHtmlAsync(response.Content, cancel);
        string? uuid = UuidField().Match(html) is { Success: true } m ? m.Groups[1].Value : null;
        string? token = ConfirmToken().Match(html) is { Success: true } t ? t.Groups[1].Value : null;

        string retry = $"https://drive.usercontent.google.com/download?id={download.Source.DriveFileId}"
            + $"&export=download&confirm={token ?? "t"}"
            + (uuid is null ? "" : $"&uuid={uuid}");

        using var second = await fetcher.GetAsync(new Uri(retry), response.Cookies, cancel);
        if (second.LooksLikeHtml)
            throw new ModFetchException(Strings.Mods_Download_DriveRefused);

        return await SaveAsync(second, download, progress, index, count, partial, cancel);
    }

    /// <summary>Streams the body to the <c>.partial</c>, hashing it on the way past and reporting progress.</summary>
    private static async Task<Fetched> SaveAsync(
        ModFetchResponse response,
        ModDownload download,
        IProgress<ModDownloadProgress>? progress,
        int index,
        int count,
        string partial,
        CancellationToken cancel)
    {
        long? total = response.Length ?? download.Bytes;
        long received = 0;
        var buffer = new byte[BufferBytes];
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        // No sharing while it is being written: a half-file is nobody's business until it is renamed.
        await using (var file = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, BufferBytes, useAsync: true))
        {
            progress?.Report(new ModDownloadProgress(ModDownloadStage.Downloading, download.Label, index, count, 0, total));
            var lastReport = DateTime.UtcNow;
            while (true)
            {
                int read = await response.Content.ReadAsync(buffer, cancel);
                if (read == 0)
                    break;
                await file.WriteAsync(buffer.AsMemory(0, read), cancel);
                hash.AppendData(buffer, 0, read);
                received += read;
                // Four times a second is enough: a report per megabyte would spend the download posting to the UI.
                if (DateTime.UtcNow - lastReport > TimeSpan.FromMilliseconds(250))
                {
                    lastReport = DateTime.UtcNow;
                    progress?.Report(new ModDownloadProgress(ModDownloadStage.Downloading, download.Label, index, count, received, total));
                }
            }
            await file.FlushAsync(cancel);
        }

        progress?.Report(new ModDownloadProgress(ModDownloadStage.Downloading, download.Label, index, count, received, total ?? received));

        // A server that declared a length and then sent less has given us a truncated file, whatever the status said.
        if (response.Length is { } declared && received != declared)
            throw new ModFetchException(string.Format(Strings.Mods_Download_Truncated, received, declared));

        return new Fetched(Convert.ToHexString(hash.GetHashAndReset()), received);
    }

    private static async Task<string> ReadHtmlAsync(Stream stream, CancellationToken cancel)
    {
        var buffer = new byte[MaxHtmlBytes];
        int read = await stream.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, cancel);
        return System.Text.Encoding.UTF8.GetString(buffer, 0, read);
    }

    /// <summary>Renames a finished <c>.partial</c> onto its served path in the library.</summary>
    private string Place(ModDefinition mod, string partial, string servedPath)
    {
        string target = library.PathFor(mod.Id, servedPath);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Move(partial, target, overwrite: true);
        return servedPath;
    }

    /// <summary>
    /// Unpacks a release zip into the library. For a mod that brings files to the game only the two halves are taken
    /// (<c>romfs/</c> and <c>exefs/</c>), so a readme and a credits file stay in the zip — the same rule
    /// <see cref="ModLibrary.ImportFrom"/> follows for a folder the user picked, and the reason the author's archives
    /// drop straight in: they are already laid out like the game. For a <see cref="ModKind.EmulatorPlugin"/> mod the
    /// archive is laid out as the <i>emulator</i> is (a <c>luma</c> folder), so what is taken is the <c>.3gx</c>
    /// itself, by its own name — see <see cref="Served"/>.
    /// </summary>
    private IReadOnlyList<string> Unpack(
        ModDefinition mod,
        string partial,
        ModDownload download,
        int index,
        int count,
        IProgress<ModDownloadProgress>? progress,
        CancellationToken cancel)
    {
        string directory = Path.GetFullPath(library.DirectoryFor(mod.Id));
        var written = new List<string>();

        // The archive is opened in a block of its own: the zip has to be closed before the file can be deleted, which
        // on Windows is not a nicety but the difference between working and an access denied.
        using (var zip = ZipFile.OpenRead(partial))
        {
            var entries = zip.Entries.Where(e => e.Length > 0 && Served(e.FullName, mod) is not null).ToList();
            for (int i = 0; i < entries.Count; i++)
            {
                cancel.ThrowIfCancellationRequested();
                var entry = entries[i];
                string served = Served(entry.FullName, mod)!;
                string target = library.PathFor(mod.Id, served);

                // A zip is somebody else's data: an entry that resolves outside the mod's own folder is refused rather
                // than written where it asked to go.
                if (!Path.GetFullPath(target).StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    throw new InvalidDataException(string.Format(Strings.Mods_Download_EntryOutside, entry.FullName));

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                // Even one file out of the zip lands by rename, so a half-extracted file is never read as a good one.
                string staged = target + ".partial";
                entry.ExtractToFile(staged, overwrite: true);
                File.Move(staged, target, overwrite: true);
                written.Add(served);

                progress?.Report(new ModDownloadProgress(
                    ModDownloadStage.Unpacking, download.Label, index, count, i + 1, entries.Count));
            }
        }

        if (written.Count == 0)
            throw new InvalidDataException(Strings.Mods_Download_EmptyArchive);

        Delete(partial);
        return written;
    }

    /// <summary>
    /// A zip entry's served path, or null for anything that is not part of the mod: a readme, a credits folder, and
    /// the placeholder the author leaves where the model archive goes (its own name says to delete it, and it would
    /// otherwise be packed into the ROM as if it were game data).
    /// </summary>
    /// <remarks>
    /// <b>A plugin archive is laid out for the emulator and not for the game.</b> The follower mod's zips carry a
    /// <c>luma</c> folder, whose shape (and whose title-id subfolder) belongs to the emulator and not here, so the
    /// <c>.3gx</c> is taken by its own file name and kept at <c>plugin/&lt;name&gt;.3gx</c>; the folder it is copied
    /// into is worked out at install time from the project's game, which is the only place that knows it.
    /// </remarks>
    private static string? Served(string entryName, ModDefinition mod)
    {
        string name = entryName.Replace('\\', '/').TrimStart('/');
        if (name.Contains("..", StringComparison.Ordinal) || name.EndsWith('/'))
            return null;

        if (mod.IsPlugin)
        {
            string plugin = name[(name.LastIndexOf('/') + 1)..];
            return plugin.EndsWith(".3gx", StringComparison.OrdinalIgnoreCase) ? ModFile.PluginPrefix + plugin : null;
        }

        bool half = name.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("exefs/", StringComparison.OrdinalIgnoreCase);
        if (!half || name.EndsWith('/'))
            return null;
        string leaf = name[(name.LastIndexOf('/') + 1)..];
        if (leaf.Contains("PLACE_MODEL_FILE", StringComparison.OrdinalIgnoreCase)
            || leaf.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            return null;
        return name;
    }

    private static string SafeName(string label)
    {
        var clean = label.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == '/' ? '_' : c).ToArray();
        string name = new string(clean).Trim();
        return name.Length == 0 ? "download" : name;
    }

    private static void Delete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover .partial is harmless: it is never read, and the next attempt overwrites it.
        }
    }

    private static void TidyStaging(string staging)
    {
        try
        {
            if (Directory.Exists(staging) && Directory.EnumerateFileSystemEntries(staging).FirstOrDefault() is null)
                Directory.Delete(staging);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
