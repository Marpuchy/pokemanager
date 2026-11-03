using System.Security.Cryptography;
using Pokemanager.Model.Dump;
using Pokemanager.Model.Mods;
using Pokemanager.Model.Projects;

namespace Pokemanager.Tests.Mods;

/// <summary>
/// The user's own copies of the mod files: where they live, and telling the right file from one that only claims to be
/// it. Nothing here downloads anything — the files are made on the spot, as the user would provide them.
/// </summary>
public sealed class ModLibraryTests : IDisposable
{
    private const string Id = "test-expansion";
    private static readonly byte[] Personal = [1, 2, 3, 4, 5, 6, 7, 8];
    private static readonly byte[] Code = [9, 9, 9, 9];
    private static readonly byte[] SomethingElse = [0xBA, 0xD0];

    private readonly string root = Directory.CreateTempSubdirectory("pokemanager-mods-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(root, recursive: true); } catch (IOException) { }
    }

    private ModLibrary Library => new(root);

    private static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data));

    /// <summary>A definition whose hashes are known, which is what the real catalogue will look like once measured.</summary>
    private static ModDefinition WithHashes() => Definition(Hash(Personal), Hash(Code));

    /// <summary>The catalogue as it is today: the files are named but no hash is known for them.</summary>
    private static ModDefinition WithoutHashes() => Definition(null, null);

    private static ModDefinition Definition(string? personalHash, string? codeHash) => new(
        Id, "Test expansion", "nobody", "a mod made up for a test", "1.0", "https://example.invalid",
        [new ModLicense("CC0-1.0", "a test")],
        [GameTitle.UltraSun],
        [
            new ModFile(ModFileArea.RomFs, "a/0/1/7", personalHash) { Required = true },
            new ModFile(ModFileArea.ExeFs, "code.bin", codeHash) { Required = true },
            new ModFile(ModFileArea.ExeFs, "icon.bin") { IntoRom = false },
            new ModFile(ModFileArea.RomFs, "a/0/9/4") { Apart = true },
        ]);

    /// <summary>Puts a file in the library under its served path, as the user dropping a release in would.</summary>
    private void Provide(string servedPath, byte[] data)
    {
        string path = Path.Combine(root, Id, servedPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, data);
    }

    private ModSettings Enabled() => new() { Enabled = [Id] };

    // ------------------------------------------------------------------ verification by hash

    /// <summary>The right files are confirmed, and the library can say which release they are.</summary>
    [Fact]
    public void TheRightFilesAreVerifiedAndTheVersionIsConfirmed()
    {
        Provide("romfs/a/0/1/7", Personal);
        Provide("exefs/code.bin", Code);

        var installation = Library.Inspect(WithHashes());

        Assert.Equal(ModFileState.Verified, installation.Files.Single(f => f.ServedPath == "romfs/a/0/1/7").State);
        Assert.Equal(ModFileState.Verified, installation.Files.Single(f => f.ServedPath == "exefs/code.bin").State);
        Assert.Empty(installation.Wrong);
        Assert.True(installation.CanBuild);
        Assert.Null(installation.Problem);
        Assert.True(installation.ConfirmsVersion);
        Assert.Contains("1.0", installation.VersionText);
    }

    /// <summary>
    /// A different file under the same name is rejected: it is not what it claims to be, and the build stops instead
    /// of packing it. This is the whole point of keeping hashes.
    /// </summary>
    [Fact]
    public void AFileThatIsNotWhatItClaimsToBeIsRejected()
    {
        Provide("romfs/a/0/1/7", SomethingElse);
        Provide("exefs/code.bin", Code);

        var installation = Library.Inspect(WithHashes());
        var personal = installation.Files.Single(f => f.ServedPath == "romfs/a/0/1/7");

        Assert.Equal(ModFileState.WrongHash, personal.State);
        Assert.Equal(Hash(SomethingElse), personal.Sha256); // what it really is, so it can be reported
        Assert.Single(installation.Wrong);
        Assert.False(installation.CanBuild);
        Assert.NotNull(installation.Problem);
        Assert.Contains("romfs/a/0/1/7", installation.Problem!);
        Assert.False(installation.ConfirmsVersion);
    }

    /// <summary>
    /// With no hash in the catalogue the file is taken as given and the library <b>says it cannot confirm it</b>,
    /// instead of claiming a version nothing was checked against.
    /// </summary>
    [Fact]
    public void WithoutAKnownHashTheFileIsTakenAsGivenAndSaidToBeUnconfirmed()
    {
        Provide("romfs/a/0/1/7", Personal);
        Provide("exefs/code.bin", Code);

        var installation = Library.Inspect(WithoutHashes());

        Assert.All(installation.Present, f => Assert.Equal(ModFileState.HashUnknown, f.State));
        Assert.True(installation.HasUnconfirmedFiles);
        Assert.True(installation.CanBuild);          // nothing says it is wrong, so it is not refused
        Assert.False(installation.ConfirmsVersion);  // and nothing says it is right either
        Assert.Contains("1.0", installation.VersionText);
    }

    /// <summary>A required file that is not there stops the build, and the message says which one and where to put it.</summary>
    [Fact]
    public void ARequiredFileThatIsMissingStopsTheBuild()
    {
        Provide("romfs/a/0/1/7", Personal); // the executable is missing

        var installation = Library.Inspect(WithHashes());

        Assert.Equal(ModFileState.Missing, installation.Files.Single(f => f.ServedPath == "exefs/code.bin").State);
        Assert.Single(installation.MissingRequired);
        Assert.False(installation.CanBuild);
        Assert.Contains("exefs/code.bin", installation.Problem!);
        Assert.Contains(installation.Directory, installation.Problem!);
    }

    /// <summary>An empty library is reported as empty, not as a broken mod.</summary>
    [Fact]
    public void AnEmptyLibraryIsReportedAsEmpty()
    {
        var installation = Library.Inspect(WithHashes());

        Assert.True(installation.IsEmpty);
        Assert.False(installation.CanBuild);
        Assert.NotNull(installation.Problem);
        Assert.Empty(installation.Contents);
    }

    /// <summary>
    /// The file distributed apart is missing in an ordinary library and that is not a fault: it is not required and it
    /// is not counted among what the catalogue expected to find.
    /// </summary>
    [Fact]
    public void TheFileDistributedApartIsNotAFaultWhenItIsMissing()
    {
        Provide("romfs/a/0/1/7", Personal);
        Provide("exefs/code.bin", Code);

        var installation = Library.Inspect(WithHashes());

        Assert.True(installation.CanBuild);
        Assert.DoesNotContain("romfs/a/0/9/4", installation.MissingExpected.Select(f => f.ServedPath));
        Assert.Contains("exefs/icon.bin", installation.MissingExpected.Select(f => f.ServedPath));
    }

    // ------------------------------------------------------------------ what goes into the ROM

    /// <summary>
    /// A mod that replaces whole archives can ship more files than the catalogue names, so everything the user
    /// provides is packed — and the catalogue is only used to check and to report. The executable travels as the bare
    /// <c>code.bin</c>, which is the shape the ROM packer takes.
    /// </summary>
    [Fact]
    public void EveryFileProvidedGoesIntoTheRomEvenOneTheCatalogueDoesNotName()
    {
        Provide("romfs/a/0/1/7", Personal);
        Provide("exefs/code.bin", Code);
        Provide("romfs/a/0/3/2", [7, 7]); // a text archive the catalogue of this test does not list

        var contents = Library.Inspect(WithHashes()).Contents;

        Assert.Equal(new[] { "code.bin", "romfs/a/0/1/7", "romfs/a/0/3/2" }, contents.Select(c => c.TitlePath).ToArray());
        Assert.All(contents, c => Assert.True(File.Exists(c.LocalPath)));
        Assert.Equal(Hash(Personal), contents.Single(c => c.TitlePath == "romfs/a/0/1/7").Sha256);
    }

    /// <summary>
    /// The icon and the banner stay out of the ROM: the packer only writes romfs files and the executable, and the
    /// library has to be honest about that instead of handing them over to be silently dropped.
    /// </summary>
    [Fact]
    public void TheIconAndBannerAreKeptButStayOutOfTheRom()
    {
        Provide("romfs/a/0/1/7", Personal);
        Provide("exefs/code.bin", Code);
        Provide("exefs/icon.bin", [1]);
        Provide("exefs/banner.bin", [2]); // not even named by this test's catalogue

        var installation = Library.Inspect(WithHashes());

        Assert.Contains("exefs/icon.bin", installation.Present.Select(f => f.ServedPath));
        Assert.Contains("exefs/banner.bin", installation.Present.Select(f => f.ServedPath));
        Assert.DoesNotContain("icon.bin", installation.Contents.Select(c => c.TitlePath));
        Assert.DoesNotContain("banner.bin", installation.Contents.Select(c => c.TitlePath));
    }

    // ------------------------------------------------------------------ where the files are

    [Fact]
    public void TheRomFsLayerIsTheModsOwnFolderAndOnlyWhenItExists()
    {
        Assert.Empty(Library.RomFsRoots(Enabled())); // nothing provided yet

        Provide("romfs/a/0/1/7", Personal);

        Assert.Equal(new[] { Path.Combine(root, Id, "romfs") }, Library.RomFsRoots(Enabled()).ToArray());
        Assert.Empty(Library.RomFsRoots(new ModSettings()));
    }

    [Fact]
    public void TheExecutableIsFoundOnlyWhenTheModHasOne()
    {
        Assert.Null(Library.CodePath(Enabled()));

        Provide("exefs/code.bin", Code);

        Assert.Equal(Path.Combine(root, Id, "exefs", "code.bin"), Library.CodePath(Enabled()));
        Assert.Null(Library.CodePath(new ModSettings()));
    }

    /// <summary>Importing a release copies the two halves of a game and leaves its readme and licence behind.</summary>
    [Fact]
    public void ImportingAReleaseTakesOnlyTheGamesOwnFolders()
    {
        string release = Path.Combine(root, "release");
        Directory.CreateDirectory(Path.Combine(release, "romfs", "a", "0", "1"));
        Directory.CreateDirectory(Path.Combine(release, "exefs"));
        File.WriteAllBytes(Path.Combine(release, "romfs", "a", "0", "1", "7"), Personal);
        File.WriteAllBytes(Path.Combine(release, "exefs", "code.bin"), Code);
        File.WriteAllText(Path.Combine(release, "README.md"), "read me");

        var copied = Library.ImportFrom(WithHashes(), release);

        Assert.Equal(new[] { "exefs/code.bin", "romfs/a/0/1/7" }, copied.ToArray());
        Assert.True(Library.Inspect(WithHashes()).CanBuild);
        Assert.False(File.Exists(Path.Combine(root, Id, "README.md")));
    }

    /// <summary>The file that comes apart from the rest is added on its own, under the path the mod serves it at.</summary>
    [Fact]
    public void TheFileDistributedApartIsAddedOnItsOwn()
    {
        string models = Path.Combine(root, "models-the-user-downloaded");
        File.WriteAllBytes(models, [4, 5, 6]);

        Library.ImportFile(WithHashes(), models, "romfs/a/0/9/4");

        var status = Library.Inspect(WithHashes()).Files.Single(f => f.ServedPath == "romfs/a/0/9/4");
        Assert.True(status.Present);
        Assert.Equal(ModFileState.HashUnknown, status.State);
        Assert.Equal("romfs/a/0/9/4", status.TitlePath);
    }

    /// <summary>The library follows the data folder, like the randomization cache and the save backups.</summary>
    [Fact]
    public void TheDefaultRootIsUnderTheDataFolder() =>
        Assert.Equal("mods", Path.GetFileName(ModLibrary.DefaultRoot));
}
