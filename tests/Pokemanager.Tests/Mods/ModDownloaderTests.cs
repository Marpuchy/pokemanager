using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Pokemanager.Model.Dump;
using Pokemanager.Model.Mods;

namespace Pokemanager.Tests.Mods;

/// <summary>
/// Fetching a mod's files into the user's own library. <b>No test here opens a socket</b>: everything goes through
/// <see cref="IModFetcher"/>, which a fake answers from memory — including Google Drive's confirmation page, which is
/// the part most likely to break in the real world and the part that most needs to be exercised.
/// </summary>
public sealed class ModDownloaderTests : IDisposable
{
    private const string Id = "test-expansion";
    private const string Repo = "someone/some-mod";

    private readonly string root = Directory.CreateTempSubdirectory("pokemanager-download-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(root, recursive: true); } catch (IOException) { }
    }

    private ModLibrary Library => new(root);

    private static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data));

    // ------------------------------------------------------------------ the fake network

    /// <summary>One canned answer.</summary>
    private sealed record Answer(byte[] Body, string ContentType = "application/octet-stream")
    {
        public long? Length { get; init; }
        public string? Cookies { get; init; }
        /// <summary>Throws part-way through the body, to stand for a connection that dropped mid-download.</summary>
        public bool BreakMidway { get; init; }
    }

    /// <summary>
    /// A fetcher that answers from a script and records what it was asked for. The recording is the point: it is how a
    /// test proves the Drive dance sent the token and the cookie back, and that a failure did not turn into a loop.
    /// </summary>
    private sealed class FakeFetcher(params Answer[] answers) : IModFetcher
    {
        private int next;

        public List<(Uri Url, string? Cookies)> Requests { get; } = [];

        public Task<ModFetchResponse> GetAsync(Uri url, string? cookies, CancellationToken cancel)
        {
            Requests.Add((url, cookies));
            if (next >= answers.Length)
                throw new ModFetchException("the fake was asked once more than it was told to answer");
            var answer = answers[next++];
            Stream body = answer.BreakMidway
                ? new BreakingStream(answer.Body)
                : new MemoryStream(answer.Body, writable: false);
            return Task.FromResult(new ModFetchResponse(
                body,
                answer.Length ?? (answer.BreakMidway ? answer.Body.Length * 2 : answer.Body.Length),
                answer.ContentType,
                null,
                answer.Cookies));
        }
    }

    /// <summary>Gives back half of what it holds and then fails, the way a dropped connection does.</summary>
    private sealed class BreakingStream(byte[] data) : Stream
    {
        private int position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => data.Length;
        public override long Position { get => position; set => throw new NotSupportedException(); }
        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (position >= data.Length / 2)
                throw new IOException("the connection dropped");
            int read = Math.Min(count, data.Length / 2 - position);
            Array.Copy(data, position, buffer, offset, read);
            position += read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    // ------------------------------------------------------------------ the fixtures

    /// <summary>A zip laid out as a release is: the two halves of the game, plus the rubbish a release carries.</summary>
    private static byte[] Zip(params (string Name, byte[] Data)[] entries)
    {
        var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, data) in entries)
            {
                var entry = zip.CreateEntry(name);
                using var stream = entry.Open();
                stream.Write(data);
            }
        }
        return buffer.ToArray();
    }

    private static readonly byte[] Personal = [1, 2, 3, 4, 5, 6, 7, 8];
    private static readonly byte[] Moves = [11, 12, 13];
    private static readonly byte[] SunCode = [0x55, 0x53, 0x55, 0x4E];
    private static readonly byte[] MoonCode = [0x55, 0x4D, 0x4F, 0x4F];
    private static readonly byte[] Models = [0x4D, 0x4F, 0x44, 0x45, 0x4C];

    private static byte[] ReleaseZip() => Zip(
        ("README.txt", Encoding.UTF8.GetBytes("read me")),
        ("SpritesCredits/Credits.txt", Encoding.UTF8.GetBytes("thanks")),
        ("romfs/a/0/1/7", Personal),
        ("romfs/a/0/1/1", Moves),
        ("romfs/a/0/9/PLACE_MODEL_FILE_HERE_AND_DELETE_THIS.txt", Encoding.UTF8.GetBytes("put it here")),
        ("exefs/code.bin", [0x4F, 0x4C, 0x44]));

    /// <summary>
    /// A mod shaped like the real one: a zip per game, an executable per game that replaces the zip's own, and one
    /// large file that comes apart from everything else.
    /// </summary>
    private static ModDefinition Definition(string? zipHash = null, string? codeHash = null) => new(
        Id, "Test expansion", "nobody", "a mod made up for a test", "1.0",
        "https://example.invalid", [new ModLicense("CC0-1.0", "a test")],
        [GameTitle.UltraSun, GameTitle.UltraMoon],
        [
            new ModFile(ModFileArea.RomFs, "a/0/1/7") { Required = true },
            new ModFile(ModFileArea.RomFs, "a/0/1/1"),
            new ModFile(ModFileArea.ExeFs, "code.bin") { Required = true },
            new ModFile(ModFileArea.RomFs, "a/0/9/4") { Apart = true },
        ])
    {
        Downloads =
        [
            ModDownload.Archive(ModSource.GitHub(Repo, "v1.4", "SunRelease.zip"), GameTitle.UltraSun) with
            {
                Sha256 = zipHash,
                Provides = ["romfs/a/0/1/7", "romfs/a/0/1/1"],
            },
            ModDownload.Archive(ModSource.GitHub(Repo, "v1.4", "MoonRelease.zip"), GameTitle.UltraMoon) with
            {
                Provides = ["romfs/a/0/1/7", "romfs/a/0/1/1"],
            },
            ModDownload.File(ModSource.GitHub(Repo, "v1.41", "Suncode.bin"), "exefs/code.bin", GameTitle.UltraSun) with
            {
                Sha256 = codeHash,
            },
            ModDownload.File(ModSource.GitHub(Repo, "v1.41", "Mooncode.bin"), "exefs/code.bin", GameTitle.UltraMoon),
            ModDownload.File(ModSource.Drive("the-drive-id"), "romfs/a/0/9/4") with { Apart = true },
        ],
    };

    private string Local(string servedPath) => Library.PathFor(Id, servedPath);

    private void Provide(string servedPath, byte[] data)
    {
        string path = Local(servedPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, data);
    }

    // ------------------------------------------------------------------ the catalogue picks by game

    /// <summary>
    /// One asset per game, so an Ultra Moon project never gets Ultra Sun's files. An executable from the wrong game
    /// is a ROM that does not boot, so this is not a cosmetic distinction.
    /// </summary>
    [Fact]
    public void EachGameGetsItsOwnAssetsAndNotTheOtherGames()
    {
        var mod = Definition();

        var sun = mod.DownloadsFor(GameTitle.UltraSun).Select(d => d.Source.AssetName ?? d.Source.DriveFileId).ToList();
        var moon = mod.DownloadsFor(GameTitle.UltraMoon).Select(d => d.Source.AssetName ?? d.Source.DriveFileId).ToList();

        Assert.Equal(["SunRelease.zip", "Suncode.bin", "the-drive-id"], sun);
        Assert.Equal(["MoonRelease.zip", "Mooncode.bin", "the-drive-id"], moon);
    }

    /// <summary>The real entry, checked the same way: the names were verified against the release itself.</summary>
    [Fact]
    public void TheRealCatalogueEntryHasOneAssetPerGame()
    {
        var mod = ModCatalog.Find(ModCatalog.UsumExpansionId)!;

        var sun = mod.DownloadsFor(GameTitle.UltraSun);
        var moon = mod.DownloadsFor(GameTitle.UltraMoon);

        Assert.Equal("UltraSunExpansion_NoModelFile.zip", sun[0].Source.AssetName);
        Assert.Equal("UltraMoonExpansion_NoModelFile.zip", moon[0].Source.AssetName);
        Assert.Equal("UltraSun code.bin", sun.Single(d => d.ServedPath == "exefs/code.bin").Source.AssetName);
        Assert.Equal("UltraMoon code.bin", moon.Single(d => d.ServedPath == "exefs/code.bin").Source.AssetName);
        Assert.Contains("/UltraSun_Expansion", sun.Single(d => d.ServedPath == "romfs/Battle.cro").Source.Url);

        // Nothing of one game leaks into the other.
        Assert.DoesNotContain(sun, d => d.Source.AssetName?.Contains("Moon", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(moon, d => d.Source.AssetName?.Contains("Sun", StringComparison.Ordinal) == true);

        // The model archive comes apart, from Drive, and is for both games.
        var models = sun.Single(d => d.Source.Kind == ModSourceKind.GoogleDrive);
        Assert.Equal("romfs/a/0/9/4", models.ServedPath);
        Assert.True(models.Apart);
        Assert.Null(models.Game);
        Assert.Contains(models, moon);
    }

    /// <summary>Every source that can fail has a page to send the user to: that is what the fallback is.</summary>
    [Fact]
    public void EverySourceOfTheRealEntryCanBeOpenedByHand()
    {
        var mod = ModCatalog.Find(ModCatalog.UsumExpansionId)!;

        Assert.NotEmpty(mod.Downloads);
        Assert.All(mod.Downloads, d => Assert.False(string.IsNullOrWhiteSpace(d.Source.PageUrl)));
        Assert.All(mod.Downloads, d => Assert.NotNull(d.Source.DownloadUri));
        Assert.True(mod.CanDownload);
    }

    // ------------------------------------------------------------------ the zip lands where the library looks

    /// <summary>
    /// A release zip is laid out as the game is, so it unpacks straight onto the served paths
    /// <see cref="ModLibrary.Inspect"/> reads — and the readme, the credits and the author's "put the model file here"
    /// note stay out of the library, because they are not game data and would otherwise be packed into the ROM.
    /// </summary>
    [Fact]
    public async Task AZipUnpacksOntoThePathsTheLibraryExpects()
    {
        var mod = Definition();
        var fetcher = new FakeFetcher(
            new Answer(ReleaseZip()),
            new Answer(SunCode),
            new Answer(Models));

        var report = await new ModDownloader(fetcher, Library).DownloadAsync(mod, GameTitle.UltraSun);

        Assert.All(report.Outcomes, o => Assert.True(o.Ok, o.Message));
        Assert.Equal(Personal, File.ReadAllBytes(Local("romfs/a/0/1/7")));
        Assert.Equal(Moves, File.ReadAllBytes(Local("romfs/a/0/1/1")));
        Assert.Equal(Models, File.ReadAllBytes(Local("romfs/a/0/9/4")));

        // The point release's executable wins over the one inside the zip: it is fetched after it.
        Assert.Equal(SunCode, File.ReadAllBytes(Local("exefs/code.bin")));

        // Not game data, so not in the library.
        Assert.False(File.Exists(Path.Combine(root, Id, "README.txt")));
        Assert.False(Directory.Exists(Path.Combine(root, Id, "SpritesCredits")));
        Assert.Empty(Directory.GetFiles(Path.Combine(root, Id, "romfs", "a", "0", "9"), "PLACE_*"));

        // And the library agrees it is there, which is the only test that really matters.
        var installation = Library.Inspect(mod);
        Assert.Empty(installation.MissingRequired);
        Assert.True(installation.CanBuild);
    }

    /// <summary>Ultra Moon's own executable, from Ultra Moon's own asset.</summary>
    [Fact]
    public async Task TheOtherGameGetsItsOwnExecutable()
    {
        var fetcher = new FakeFetcher(new Answer(ReleaseZip()), new Answer(MoonCode), new Answer(Models));

        await new ModDownloader(fetcher, Library).DownloadAsync(Definition(), GameTitle.UltraMoon);

        Assert.Equal(MoonCode, File.ReadAllBytes(Local("exefs/code.bin")));
        Assert.Contains("MoonRelease.zip", fetcher.Requests[0].Url.ToString(), StringComparison.Ordinal);
        Assert.Contains("Mooncode.bin", fetcher.Requests[1].Url.ToString(), StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ a cut download is not a file

    /// <summary>
    /// A connection that drops mid-download leaves nothing behind that could be taken for the real file: the bytes go
    /// to a <c>.partial</c> and the rename only happens once it is whole, the rule the ROM build already follows.
    /// </summary>
    [Fact]
    public async Task AnInterruptedDownloadIsNeverTakenForAGoodFile()
    {
        var mod = Definition();
        var fetcher = new FakeFetcher(new Answer(ReleaseZip()), new Answer(SunCode) { BreakMidway = true });

        var report = await new ModDownloader(fetcher, Library).DownloadAsync(mod, GameTitle.UltraSun);

        var code = report.Outcomes.Single(o => o.Download.ServedPath == "exefs/code.bin");
        Assert.Equal(ModDownloadStatus.Failed, code.Status);
        Assert.False(File.Exists(Local("exefs/code.bin")));
        // No .partial anywhere in the library pretending to be a file.
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(root, Id), "*.partial", SearchOption.AllDirectories));
        // The message says what happened, and where to go instead.
        Assert.NotEmpty(code.Message);
        Assert.NotNull(code.PageUrl);
    }

    /// <summary>
    /// A server that declares a length and then sends less has handed over a truncated file, whatever its status code
    /// said. It is refused rather than written.
    /// </summary>
    [Fact]
    public async Task ADownloadShorterThanDeclaredIsRefused()
    {
        var mod = Definition();
        var fetcher = new FakeFetcher(
            new Answer(ReleaseZip()),
            new Answer(SunCode) { Length = SunCode.Length + 500 });

        var report = await new ModDownloader(fetcher, Library).DownloadAsync(mod, GameTitle.UltraSun);

        Assert.Equal(ModDownloadStatus.Failed, report.Outcomes.Single(o => o.Download.ServedPath == "exefs/code.bin").Status);
        Assert.False(File.Exists(Local("exefs/code.bin")));
    }

    // ------------------------------------------------------------------ hashes

    /// <summary>
    /// The hash does not match, so what arrived is not the file the catalogue describes: it is thrown away and the
    /// library is left exactly as it was. A file that was already there is not replaced by a wrong one.
    /// </summary>
    [Fact]
    public async Task AWrongHashIsRejectedAndTheLibraryIsNotTouched()
    {
        var mod = Definition(codeHash: Hash([0xFF, 0xFF]));
        Provide("exefs/code.bin", MoonCode); // something already there, to prove it survives
        var fetcher = new FakeFetcher(new Answer(ReleaseZip()), new Answer(SunCode), new Answer(Models));

        var report = await new ModDownloader(fetcher, Library).DownloadAsync(mod, GameTitle.UltraSun);

        var code = report.Outcomes.Single(o => o.Download.ServedPath == "exefs/code.bin");
        Assert.Equal(ModDownloadStatus.WrongHash, code.Status);
        Assert.Equal(Hash(SunCode), code.Sha256);
        Assert.Equal(MoonCode, File.ReadAllBytes(Local("exefs/code.bin")));
        Assert.Empty(code.Written);
    }

    /// <summary>A hash the catalogue does know, and the bytes that match it, are accepted and reported as verified.</summary>
    [Fact]
    public async Task AKnownHashThatMatchesIsAccepted()
    {
        var zip = ReleaseZip();
        var mod = Definition(zipHash: Hash(zip), codeHash: Hash(SunCode));
        var fetcher = new FakeFetcher(new Answer(zip), new Answer(SunCode), new Answer(Models));

        var report = await new ModDownloader(fetcher, Library).DownloadAsync(mod, GameTitle.UltraSun);

        Assert.All(report.Outcomes, o => Assert.True(o.Ok, o.Message));
        Assert.Equal(Hash(SunCode), report.Outcomes.Single(o => o.Download.ServedPath == "exefs/code.bin").Sha256);
    }

    /// <summary>
    /// The hash is computed even when the catalogue has none to compare it with, and handed back — which is how an
    /// unconfirmable release gets measured once instead of guessed at for ever.
    /// </summary>
    [Fact]
    public async Task TheHashIsMeasuredEvenWhenTheCatalogueDoesNotKnowIt()
    {
        var mod = Definition();
        var fetcher = new FakeFetcher(new Answer(ReleaseZip()), new Answer(SunCode), new Answer(Models));

        var report = await new ModDownloader(fetcher, Library).DownloadAsync(mod, GameTitle.UltraSun);

        var models = report.Outcomes.Single(o => o.Download.Source.Kind == ModSourceKind.GoogleDrive);
        Assert.False(models.Download.HashKnown);
        Assert.Equal(Hash(Models), models.Sha256);
        Assert.Equal(Models.Length, (int)models.Bytes);
    }

    // ------------------------------------------------------------------ what is already there is left alone

    /// <summary>
    /// A file the library already holds and whose hash matches is not fetched again — these are hundreds of megabytes,
    /// and 2.3 GB for the model archive.
    /// </summary>
    [Fact]
    public async Task WhatIsAlreadyVerifiedIsNotDownloadedAgain()
    {
        var mod = Definition(codeHash: Hash(SunCode));
        Provide("romfs/a/0/1/7", Personal);
        Provide("romfs/a/0/1/1", Moves);
        Provide("exefs/code.bin", SunCode);
        Provide("romfs/a/0/9/4", Models);

        var fetcher = new FakeFetcher(); // it answers nothing: being asked at all would throw
        var report = await new ModDownloader(fetcher, Library).DownloadAsync(mod, GameTitle.UltraSun);

        Assert.Empty(fetcher.Requests);
        Assert.True(report.NothingToDo);
        Assert.All(report.Outcomes, o => Assert.Equal(ModDownloadStatus.AlreadyThere, o.Status));
    }

    /// <summary>
    /// A file that is there but is <b>not</b> the one the catalogue describes is fetched again: "already there" means
    /// the right file, not merely a file.
    /// </summary>
    [Fact]
    public async Task AFileThatIsThereButWrongIsFetchedAgain()
    {
        var mod = Definition(codeHash: Hash(SunCode));
        Provide("romfs/a/0/1/7", Personal);
        Provide("romfs/a/0/1/1", Moves);
        Provide("exefs/code.bin", MoonCode); // the wrong game's executable
        Provide("romfs/a/0/9/4", Models);

        var fetcher = new FakeFetcher(new Answer(SunCode));
        var report = await new ModDownloader(fetcher, Library).DownloadAsync(mod, GameTitle.UltraSun);

        Assert.Single(fetcher.Requests);
        Assert.Contains("Suncode.bin", fetcher.Requests[0].Url.ToString(), StringComparison.Ordinal);
        Assert.Equal(SunCode, File.ReadAllBytes(Local("exefs/code.bin")));
        Assert.False(report.NothingToDo);
    }

    /// <summary>
    /// An archive whose files are all there is skipped even though the catalogue names files that release does not
    /// carry — otherwise 55 MB would come down again on every single check.
    /// </summary>
    [Fact]
    public async Task AnArchiveIsSkippedOnWhatItActuallyBringsAndNotOnTheWholeFileList()
    {
        var mod = Definition();
        Provide("romfs/a/0/1/7", Personal);
        Provide("romfs/a/0/1/1", Moves);

        var fetcher = new FakeFetcher(new Answer(SunCode), new Answer(Models));
        var report = await new ModDownloader(fetcher, Library).DownloadAsync(mod, GameTitle.UltraSun);

        Assert.Equal(ModDownloadStatus.AlreadyThere, report.Outcomes[0].Status);
        Assert.DoesNotContain(fetcher.Requests, r => r.Url.ToString().Contains("SunRelease.zip", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ Google Drive, and its page

    /// <summary>
    /// Drive answers a large file with an HTML confirmation page. The token comes out of it and goes back with the
    /// cookie, against the download host — and <b>that is the one retry there is</b>.
    /// </summary>
    [Fact]
    public async Task DriveConfirmationPageIsAnsweredWithTheTokenAndTheCookie()
    {
        var mod = Definition();
        Provide("romfs/a/0/1/7", Personal);
        Provide("romfs/a/0/1/1", Moves);
        Provide("exefs/code.bin", SunCode);

        string page = """
            <html><body><form action="https://drive.usercontent.google.com/download">
            <input type="hidden" name="uuid" value="abcd-1234">
            <a href="/uc?export=download&confirm=t&id=the-drive-id">Download anyway</a>
            </form></body></html>
            """;
        var fetcher = new FakeFetcher(
            new Answer(Encoding.UTF8.GetBytes(page), "text/html") { Cookies = "download_warning=yes" },
            new Answer(Models));

        var report = await new ModDownloader(fetcher, Library).DownloadAsync(mod, GameTitle.UltraSun);

        Assert.Equal(Models, File.ReadAllBytes(Local("romfs/a/0/9/4")));
        Assert.Equal(2, fetcher.Requests.Count);

        var retry = fetcher.Requests[1];
        Assert.Contains("drive.usercontent.google.com/download", retry.Url.ToString(), StringComparison.Ordinal);
        Assert.Contains("confirm=", retry.Url.ToString(), StringComparison.Ordinal);
        Assert.Contains("uuid=abcd-1234", retry.Url.ToString(), StringComparison.Ordinal);
        Assert.Equal("download_warning=yes", retry.Cookies);
        Assert.All(report.Outcomes, o => Assert.True(o.Ok, o.Message));
    }

    /// <summary>
    /// <b>Drive is assumed to break.</b> When the page comes back a second time, this fails with a sentence and a page
    /// to open — it does not retry for ever, which is the thing that would hurt.
    /// </summary>
    [Fact]
    public async Task WhenDriveKeepsReturningItsPageItFailsCleanlyAndDoesNotLoop()
    {
        var mod = Definition();
        Provide("romfs/a/0/1/7", Personal);
        Provide("romfs/a/0/1/1", Moves);
        Provide("exefs/code.bin", SunCode);

        var html = new Answer(Encoding.UTF8.GetBytes("<html>something new of Google's</html>"), "text/html");
        var fetcher = new FakeFetcher(html, html);

        var report = await new ModDownloader(fetcher, Library).DownloadAsync(mod, GameTitle.UltraSun);

        var models = report.Outcomes.Single(o => o.Download.Source.Kind == ModSourceKind.GoogleDrive);
        Assert.Equal(ModDownloadStatus.Failed, models.Status);
        Assert.Equal(2, fetcher.Requests.Count); // asked twice, never a third time
        Assert.False(File.Exists(Local("romfs/a/0/9/4")));
        // What happened and what to do: the message, and the page to open by hand.
        Assert.NotEmpty(models.Message);
        Assert.Equal("https://drive.google.com/file/d/the-drive-id/view", models.PageUrl);
    }

    /// <summary>A refusal from the host is one failure with a message, and the rest of the plan still runs.</summary>
    [Fact]
    public async Task ASourceThatRefusesBecomesAMessageAndTheRestCarriesOn()
    {
        var mod = Definition();
        var fetcher = new RefusingFetcher("SunRelease.zip", new Answer(SunCode), new Answer(Models));

        var report = await new ModDownloader(fetcher, Library).DownloadAsync(mod, GameTitle.UltraSun);

        Assert.Equal(ModDownloadStatus.Failed, report.Outcomes[0].Status);
        Assert.Contains("404", report.Outcomes[0].Message, StringComparison.Ordinal);
        Assert.Equal("https://github.com/someone/some-mod/releases/tag/v1.4", report.Outcomes[0].PageUrl);
        // The executable and the models still arrived.
        Assert.Equal(SunCode, File.ReadAllBytes(Local("exefs/code.bin")));
        Assert.Equal(Models, File.ReadAllBytes(Local("romfs/a/0/9/4")));
        Assert.NotNull(report.FirstProblem);
    }

    /// <summary>Refuses the one URL that contains a word, and answers the rest from a script.</summary>
    private sealed class RefusingFetcher(string refuse, params Answer[] answers) : IModFetcher
    {
        private readonly FakeFetcher inner = new(answers);

        public Task<ModFetchResponse> GetAsync(Uri url, string? cookies, CancellationToken cancel) =>
            url.ToString().Contains(refuse, StringComparison.Ordinal)
                ? throw new ModFetchException("HTTP 404 Not Found")
                : inner.GetAsync(url, cookies, cancel);
    }

    // ------------------------------------------------------------------ cancellation

    /// <summary>
    /// Cancellation is real: it stops the plan, nothing half-written is kept, and what was already in the library
    /// stays as it was.
    /// </summary>
    [Fact]
    public async Task CancellingStopsThePlanAndLeavesNothingHalfWritten()
    {
        var mod = Definition();
        var fetcher = new FakeFetcher(new Answer(ReleaseZip()), new Answer(SunCode), new Answer(Models));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var report = await new ModDownloader(fetcher, Library).DownloadAsync(mod, GameTitle.UltraSun, null, cancellation.Token);

        Assert.True(report.Cancelled);
        Assert.Empty(fetcher.Requests);
        Assert.False(File.Exists(Local("exefs/code.bin")));
        Assert.False(Directory.Exists(Path.Combine(root, Id, "romfs")));
    }

    // ------------------------------------------------------------------ a zip that lies

    /// <summary>
    /// A zip is somebody else's data. An entry that points out of the mod's own folder is refused instead of being
    /// written where it asked to go, and nothing of that archive is kept.
    /// </summary>
    [Fact]
    public async Task AnArchiveEntryPointingOutsideTheModFolderIsRefused()
    {
        var mod = Definition();
        var evil = Zip(("romfs/../../../escaped.txt", Encoding.UTF8.GetBytes("got out")));
        var fetcher = new FakeFetcher(new Answer(evil), new Answer(SunCode), new Answer(Models));

        var report = await new ModDownloader(fetcher, Library).DownloadAsync(mod, GameTitle.UltraSun);

        Assert.Equal(ModDownloadStatus.Failed, report.Outcomes[0].Status);
        Assert.False(File.Exists(Path.Combine(root, "escaped.txt")));
        Assert.False(File.Exists(Path.GetFullPath(Path.Combine(root, "..", "escaped.txt"))));
    }

    /// <summary>An archive with nothing of the game in it is a failure with a reason, not an empty success.</summary>
    [Fact]
    public async Task AnArchiveWithNoGameFilesIsAFailureWithAReason()
    {
        var mod = Definition();
        var fetcher = new FakeFetcher(
            new Answer(Zip(("README.txt", Encoding.UTF8.GetBytes("only a readme")))),
            new Answer(SunCode),
            new Answer(Models));

        var report = await new ModDownloader(fetcher, Library).DownloadAsync(mod, GameTitle.UltraSun);

        Assert.Equal(ModDownloadStatus.Failed, report.Outcomes[0].Status);
        Assert.NotEmpty(report.Outcomes[0].Message);
    }

    /// <summary>Bytes that are not a zip at all: a reason, and nothing written.</summary>
    [Fact]
    public async Task SomethingThatIsNotAZipIsAFailureWithAReason()
    {
        var mod = Definition();
        var fetcher = new FakeFetcher(
            new Answer([0, 1, 2, 3, 4, 5, 6, 7]),
            new Answer(SunCode),
            new Answer(Models));

        var report = await new ModDownloader(fetcher, Library).DownloadAsync(mod, GameTitle.UltraSun);

        Assert.Equal(ModDownloadStatus.Failed, report.Outcomes[0].Status);
        Assert.False(File.Exists(Local("romfs/a/0/1/7")));
    }

    // ------------------------------------------------------------------ progress

    /// <summary>
    /// Progress is reported in bytes with the total when the host declares one, which is what lets the interface show
    /// a real percentage instead of a spinner and a hope.
    /// </summary>
    [Fact]
    public async Task ProgressIsReportedWithBytesAndTheTotalWhenThereIsOne()
    {
        var mod = Definition();
        Provide("romfs/a/0/1/7", Personal);
        Provide("romfs/a/0/1/1", Moves);
        Provide("romfs/a/0/9/4", Models);
        var fetcher = new FakeFetcher(new Answer(SunCode));
        var seen = new List<ModDownloadProgress>();

        await new ModDownloader(fetcher, Library).DownloadAsync(
            mod, GameTitle.UltraSun, new SynchronousProgress(seen));

        Assert.NotEmpty(seen);
        var last = seen.Last(p => p.Stage == ModDownloadStage.Downloading);
        Assert.Equal(SunCode.Length, (int)last.Received);
        Assert.Equal(SunCode.Length, (int)last.Total!.Value);
        Assert.Equal(1.0, last.Fraction!.Value);
        Assert.Equal("Suncode.bin", last.Label);
    }

    /// <summary>
    /// No declared length — what Drive does for a large file — so there is no fraction to show and the interface says
    /// the bytes so far rather than inventing a percentage.
    /// </summary>
    [Fact]
    public async Task WithNoDeclaredLengthThereIsNoFractionToShow()
    {
        var mod = Definition();
        Provide("romfs/a/0/1/7", Personal);
        Provide("romfs/a/0/1/1", Moves);
        Provide("exefs/code.bin", SunCode);
        var fetcher = new FakeFetcher(new Answer(Models) { Length = null });
        var seen = new List<ModDownloadProgress>();

        // The fake reports a length of its own unless told otherwise, so this is the one case worth stating: a
        // progress report whose total is unknown must not pretend to be a fraction.
        var unknown = new ModDownloadProgress(ModDownloadStage.Downloading, "a/0/9/4", 1, 1, 1024, null);
        Assert.Null(unknown.Fraction);

        await new ModDownloader(fetcher, Library).DownloadAsync(mod, GameTitle.UltraSun, new SynchronousProgress(seen));
        Assert.NotEmpty(seen);
    }

    /// <summary>Collects reports on the calling thread, so a test can read them without waiting for a dispatcher.</summary>
    private sealed class SynchronousProgress(List<ModDownloadProgress> into) : IProgress<ModDownloadProgress>
    {
        public void Report(ModDownloadProgress value) => into.Add(value);
    }
}
