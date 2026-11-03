using Pokemanager.Model.Dump;

namespace Pokemanager.Model.Mods;

/// <summary>Where a download comes from, which is also the order the chain tries them in.</summary>
/// <remarks>
/// The chain degrades instead of failing: a GitHub release asset is plain HTTPS and is the reliable path; Google Drive
/// is only for what does not fit in a repository and <b>is expected to break</b> when Google changes its confirmation
/// page; the browser is the fallback for both and the only way for anything else.
/// </remarks>
public enum ModSourceKind
{
    /// <summary>A public release asset of a GitHub repository: ordinary HTTPS, no account, no token.</summary>
    GitHubRelease,

    /// <summary>
    /// A public Google Drive file. For a large file Drive answers with an HTML confirmation page instead of the
    /// bytes, so the token has to be taken out of it and sent back with the cookie — a dance that is Google's and
    /// changes without notice, which is why a failure here is a message and not an error.
    /// </summary>
    GoogleDrive,

    /// <summary>
    /// A plain download link on the author's own host, followed as it comes. <b>It may answer with a redirect</b> —
    /// GameBanana's <c>/dl/</c> links answer 302 towards its own file cache — and the fetcher follows it
    /// (<c>HttpModFetcher</c> sets <c>AllowAutoRedirect</c>), which is why no extra dance is needed here.
    /// </summary>
    DirectUrl,

    /// <summary>
    /// Nothing is fetched: the user is sent to the page in their browser. The fallback of everything above, and the
    /// only way for a host whose protocol this application does not speak (Mega needs its own cryptography).
    /// </summary>
    Browser,
}

/// <summary>What a download gives back: a whole archive, or one file as it comes.</summary>
public enum ModDownloadKind
{
    /// <summary>A zip laid out like the game (<c>romfs/</c>, <c>exefs/</c>) that unpacks into many declared files.</summary>
    Archive,

    /// <summary>One file, taken byte for byte and put at a single served path.</summary>
    SingleFile,
}

/// <summary>
/// Where one downloadable thing of a mod lives. A description only: <see cref="ModDownloader"/> is what fetches it,
/// and every kind here has the browser behind it when the automatic path does not work.
/// </summary>
/// <param name="Kind">Which host, and therefore which way of asking for the bytes.</param>
public sealed record ModSource(ModSourceKind Kind)
{
    /// <summary><c>owner/name</c> of the repository, for <see cref="ModSourceKind.GitHubRelease"/>.</summary>
    public string? Repository { get; init; }

    /// <summary>The release tag the asset belongs to: <c>v1.41</c>.</summary>
    public string? Tag { get; init; }

    /// <summary>The asset's own file name in that release, exactly as the author uploaded it.</summary>
    public string? AssetName { get; init; }

    /// <summary>The Drive file id, for <see cref="ModSourceKind.GoogleDrive"/>.</summary>
    public string? DriveFileId { get; init; }

    /// <summary>The link the bytes are asked for, for <see cref="ModSourceKind.DirectUrl"/>.</summary>
    public string? Url { get; init; }

    /// <summary>
    /// The page a human should open when the automatic path fails: the release page, the Drive viewer, the repository.
    /// <b>Never null for a source that can fail</b> — the whole point of the fallback is having somewhere to send the
    /// user instead of saying "unknown error".
    /// </summary>
    public string? PageUrl { get; init; }

    /// <summary>A public release asset: <c>https://github.com/owner/name/releases/download/tag/asset</c>.</summary>
    public static ModSource GitHub(string repository, string tag, string assetName) => new(ModSourceKind.GitHubRelease)
    {
        Repository = repository,
        Tag = tag,
        AssetName = assetName,
        PageUrl = $"https://github.com/{repository}/releases/tag/{tag}",
    };

    /// <summary>A public Drive file, with its viewer page as the fallback.</summary>
    public static ModSource Drive(string fileId) => new(ModSourceKind.GoogleDrive)
    {
        DriveFileId = fileId,
        PageUrl = $"https://drive.google.com/file/d/{fileId}/view",
    };

    /// <summary>
    /// A download link on the author's own host, with the mod's page behind it. Redirects are followed, so a link
    /// that answers 302 towards a file cache (GameBanana's do) needs nothing special.
    /// </summary>
    /// <param name="url">The link to ask for.</param>
    /// <param name="pageUrl">The page a human opens when that link stops working.</param>
    /// <param name="assetName">What the file is called, so a message and a progress line can name it.</param>
    public static ModSource Direct(string url, string pageUrl, string assetName) => new(ModSourceKind.DirectUrl)
    {
        Url = url,
        AssetName = assetName,
        PageUrl = pageUrl,
    };

    /// <summary>A page and nothing else: what Mega gets until its cryptography is here.</summary>
    public static ModSource Page(string url) => new(ModSourceKind.Browser) { PageUrl = url };

    /// <summary>The URL the bytes are asked for, or null for a source that is only a page.</summary>
    public Uri? DownloadUri => Kind switch
    {
        ModSourceKind.GitHubRelease when Repository is not null && Tag is not null && AssetName is not null =>
            new Uri($"https://github.com/{Repository}/releases/download/{Tag}/{Uri.EscapeDataString(AssetName)}"),
        ModSourceKind.GoogleDrive when DriveFileId is not null =>
            new Uri($"https://drive.google.com/uc?export=download&id={DriveFileId}"),
        ModSourceKind.DirectUrl when Url is not null => new Uri(Url),
        _ => null,
    };

    /// <summary>Whether this source can be fetched at all, or only opened in a browser.</summary>
    public bool CanFetch => DownloadUri is not null;

    /// <summary>What to name this source in a message to the user: the host, not a URL.</summary>
    public string HostText => Kind switch
    {
        ModSourceKind.GitHubRelease => "GitHub",
        ModSourceKind.GoogleDrive => "Google Drive",
        // The author's own host, named as it names itself: gamebanana.com, not a URL.
        ModSourceKind.DirectUrl when Url is not null => new Uri(Url).Host,
        _ => "the browser",
    };
}

/// <summary>
/// One thing the application can fetch for a mod: the zip of a release (which carries many of the declared files) or a
/// single file that comes apart from the rest. The catalogue lists these in order, and a later one wins over an
/// earlier one — which is how the executable of a point release replaces the one inside the big zip.
/// </summary>
/// <param name="Kind">Whether it unpacks into many files or lands as one.</param>
/// <param name="Source">Where it comes from, and what to open when that does not work.</param>
public sealed record ModDownload(ModDownloadKind Kind, ModSource Source)
{
    /// <summary>
    /// The game this download is for, or null when it serves every game of the mod. The USUM Expansion ships one
    /// asset per game, so the catalogue has to pick by the project's game and never hand Ultra Moon's files to Ultra
    /// Sun.
    /// </summary>
    public GameTitle? Game { get; init; }

    /// <summary>
    /// Where a <see cref="ModDownloadKind.SingleFile"/> lands, as the library serves it (<c>exefs/code.bin</c>,
    /// <c>romfs/a/0/9/4</c>). Null for an archive, which brings its own paths.
    /// </summary>
    public string? ServedPath { get; init; }

    /// <summary>
    /// The hash of the bytes this download should give, upper-case hex, or null when it is not known. A single file is
    /// checked against it; an archive is checked before it is unpacked, so a truncated or substituted zip never
    /// reaches the library.
    /// </summary>
    public string? Sha256 { get; init; }

    /// <summary>
    /// How large it is said to be, for the progress bar when the server sends no <c>Content-Length</c> — which is
    /// exactly what Drive does for a large file. Null when unknown.
    /// </summary>
    public long? Bytes { get; init; }

    /// <summary>
    /// It is distributed apart from the rest of the mod (the model archive is 2.3 GB and does not fit in a
    /// repository), so it is fetched on its own and its absence is an unfinished mod, not a broken one.
    /// </summary>
    public bool Apart { get; init; }

    /// <summary>
    /// The served paths this archive is known to bring, for deciding whether it still has to be fetched. Empty means
    /// "every declared file nothing else claims" (<see cref="ModDefinition.PathsFromArchive"/>).
    /// </summary>
    /// <remarks>
    /// It has to be said out loud rather than assumed from the file list, because a release does not always carry
    /// everything a mod replaces: the USUM Expansion's zip has no <c>icon.bin</c> or <c>banner.bin</c> and no Chinese
    /// text archives (its own notes say "all languages support besides Simplified and Traditional Chinese"), and
    /// treating those as missing would fetch 55 MB again on every single check.
    /// </remarks>
    public IReadOnlyList<string> Provides { get; init; } = [];

    /// <summary>Whether this download serves that game.</summary>
    public bool AppliesTo(GameTitle game) => Game is null || Game == game;

    /// <summary>The hash is known, so the bytes can be confirmed rather than merely counted.</summary>
    public bool HashKnown => Sha256 is not null;

    /// <summary>What this download is called in the interface: the asset's own name, or the path it lands at.</summary>
    public string Label => Source.AssetName ?? ServedPath ?? Source.DriveFileId ?? Source.HostText;

    /// <summary>A release asset that unpacks into the library, for one game or for all of them.</summary>
    public static ModDownload Archive(ModSource source, GameTitle? game = null) =>
        new(ModDownloadKind.Archive, source) { Game = game };

    /// <summary>One file, put at a served path exactly as it was downloaded.</summary>
    public static ModDownload File(ModSource source, string servedPath, GameTitle? game = null) =>
        new(ModDownloadKind.SingleFile, source) { ServedPath = servedPath, Game = game };
}
