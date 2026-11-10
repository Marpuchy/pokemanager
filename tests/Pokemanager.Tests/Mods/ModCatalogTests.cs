using Pokemanager.Model.Dump;
using Pokemanager.Model.Mods;

namespace Pokemanager.Tests.Mods;

/// <summary>
/// The catalogue is metadata and a link: no file of a mod is distributed with the application and nothing here
/// downloads anything. These tests keep the one entry honest — above all that it does not claim to have checked a
/// version it has no hash for.
/// </summary>
public class ModCatalogTests
{
    private static ModDefinition Expansion() =>
        ModCatalog.Find(ModCatalog.UsumExpansionId) ?? throw new InvalidOperationException("the entry is missing");

    [Fact]
    public void TheExpansionModIsInTheCatalogue()
    {
        var mod = Expansion();

        Assert.Equal("usum-expansion", mod.Id);
        Assert.Equal("1.42 (0b50f84)", mod.Version);
        Assert.Equal("https://github.com/TheSomewhatUnknownGuy/PokemonUSUMExpansion", mod.SourceUrl);
        // It brings files to the ROM, which is what makes it a layer, a cache key and a reason to build.
        Assert.Equal(ModKind.RomFiles, mod.Kind);
        Assert.True(mod.ChangesTheRom);
        Assert.False(mod.IsPlugin);
        Assert.NotEmpty(mod.Name);
        Assert.NotEmpty(mod.Author);
        Assert.NotEmpty(mod.Description);
    }

    /// <summary>Ultra Sun and Ultra Moon, and nothing else: the mod replaces those games' own archives.</summary>
    [Fact]
    public void ItAppliesToUltraSunAndUltraMoonOnly()
    {
        var mod = Expansion();

        Assert.True(mod.Supports(GameTitle.UltraSun));
        Assert.True(mod.Supports(GameTitle.UltraMoon));
        Assert.False(mod.Supports(GameTitle.Sun));
        Assert.False(mod.Supports(GameTitle.X));

        Assert.Contains(mod, ModCatalog.For(GameTitle.UltraMoon));
        Assert.Empty(ModCatalog.For(GameTitle.X));
    }

    /// <summary>
    /// <b>No hash is known yet, and the model says so instead of inventing one.</b> A made-up hash would reject the
    /// real release, which is worse than admitting the files cannot be confirmed.
    /// </summary>
    [Fact]
    public void NoHashIsKnownYetAndNoneIsInvented()
    {
        var mod = Expansion();

        Assert.False(mod.HasKnownHashes);
        Assert.All(mod.Files, f => Assert.Null(f.Sha256));
        Assert.All(mod.Files, f => Assert.False(f.HashKnown));
    }

    /// <summary>
    /// The expanded personal table and the modified executable are what the mod is: without either of them nothing
    /// happens, so the build refuses rather than quietly making a ROM without the mod.
    /// </summary>
    [Fact]
    public void ThePersonalTableAndTheExecutableAreRequired()
    {
        var mod = Expansion();
        var required = mod.RequiredFiles.Select(f => f.ServedPath).ToList();

        Assert.Contains("romfs/" + GameTitle.UltraSun.Layout().Personal, required);
        Assert.Contains("exefs/code.bin", required);
        Assert.Equal(2, required.Count);
    }

    /// <summary>
    /// The model archive is distributed apart from the mod's repository because of its size, so a library without it is
    /// an incomplete mod and not a broken one. It is also the file whose contents are not the mod author's to license,
    /// which is the other reason nothing of it is ever packaged here.
    /// </summary>
    [Fact]
    public void TheModelArchiveIsMarkedAsDistributedApart()
    {
        var mod = Expansion();
        var models = mod.File("romfs/a/0/9/4");

        Assert.NotNull(models);
        Assert.True(models!.Apart);
        Assert.False(models.Required);
        Assert.NotNull(mod.SeparateDownloadNote);
        Assert.NotEmpty(mod.SeparateDownloadNote!);
    }

    /// <summary>
    /// The ROM packer only writes romfs files and the executable (<c>PokemanagerUpr.pack</c>), so the home-menu icon
    /// and banner are kept and checked but not carried into the ROM. The model has to say that, not pretend otherwise.
    /// </summary>
    [Fact]
    public void TheIconAndBannerAreKeptButNotWrittenIntoTheRom()
    {
        var mod = Expansion();

        Assert.False(mod.File("exefs/icon.bin")!.IntoRom);
        Assert.False(mod.File("exefs/banner.bin")!.IntoRom);
        Assert.True(mod.File("exefs/code.bin")!.IntoRom);
        Assert.DoesNotContain("exefs/icon.bin", mod.RomFiles.Select(f => f.ServedPath));
        // Everything that does go in is a romfs file or the bare executable, which is what RomBuilder accepts.
        Assert.All(mod.RomFiles, f => Assert.True(f.TitlePath.StartsWith("romfs/", StringComparison.Ordinal) || f.TitlePath == "code.bin"));
    }

    /// <summary>The faults the user should know about before a run are carried with the entry, not left out.</summary>
    [Fact]
    public void TheKnownFaultsAreCarried()
    {
        var mod = Expansion();

        Assert.Equal(3, mod.KnownIssues.Count);
        Assert.All(mod.KnownIssues, issue => Assert.NotEmpty(issue));
    }

    /// <summary>
    /// Where the files come from, as the entry says it: the bulk of the mod is the zip of v1.4, then the newest
    /// executable and Battle.cro over it from the repository (pinned to a commit: no release carries them since v1.41), then the model archive from the author's Drive link.
    /// The names, sizes and hashes were verified against the release itself, so this test is what notices if somebody
    /// edits one of them by hand.
    /// </summary>
    [Fact]
    public void TheSourcesAreTheOnesThatWereVerifiedAgainstTheRelease()
    {
        var mod = Expansion();

        Assert.True(mod.CanDownload);
        Assert.Equal(7, mod.Downloads.Count);
        // Everything that is fetched from GitHub is fetched from the mod's own repository and no other.
        Assert.All(mod.Downloads.Where(d => d.Source.Kind == ModSourceKind.GitHubRelease),
            d => Assert.Equal("TheSomewhatUnknownGuy/PokemonUSUMExpansion", d.Source.Repository));

        var zips = mod.Downloads.Where(d => d.Kind == ModDownloadKind.Archive).ToList();
        Assert.Equal(2, zips.Count);
        Assert.All(zips, z => Assert.Equal("v1.4", z.Source.Tag));
        Assert.All(zips, z => Assert.NotNull(z.Game));
        // A zip that does not hash out is thrown away, so the hash has to be a real one.
        Assert.All(zips, z => Assert.Equal(64, z.Sha256!.Length));

        var executables = mod.Downloads.Where(d => d.ServedPath == "exefs/code.bin").ToList();
        Assert.Equal(2, executables.Count);
        // The newest executable and Battle.cro are fetched from the repository itself, pinned to a commit.
        Assert.All(executables, e => Assert.Contains("/0b50f8436b743301f727018f5fd6612ee4c39781/", e.Source.Url));
        var battle = mod.Downloads.Where(d => d.ServedPath == "romfs/Battle.cro").ToList();
        Assert.Equal(2, battle.Count);
        Assert.All(battle, b => Assert.Equal("0E1D0BCC5E7D18E9AC3A0DA94D5775A18377229C27C0C0EABEFCD4449E729223", b.Sha256));
        Assert.All(executables, e => Assert.Equal(5_914_624L, e.Bytes!.Value));
        Assert.All(executables, e => Assert.Equal(64, e.Sha256!.Length));
        // The executable comes after the zip that also holds one, so the point release wins.
        var order = mod.Downloads.ToList();
        Assert.True(order.IndexOf(executables[0]) > order.IndexOf(zips[0]));

        var models = mod.Downloads.Single(d => d.Source.Kind == ModSourceKind.GoogleDrive);
        Assert.Equal("romfs/a/0/9/4", models.ServedPath);
        Assert.Equal("1opjwsUhXLgcGV99Xihd3o2mUe554_bbC", models.Source.DriveFileId);
        Assert.True(models.Apart);
        // 2.3 GB, which is the whole reason it is not in the repository.
        Assert.Equal(2_266_714_724L, models.Bytes!.Value);
        // And no hash: it was not measured, and a guess would reject the real file.
        Assert.False(models.HashKnown);
    }

    /// <summary>
    /// The zip does not carry everything the catalogue names — no <c>icon.bin</c>, no <c>banner.bin</c>, and no
    /// Simplified or Traditional Chinese text archive — so what it does bring is said out loud. Without that the
    /// downloader would count those as missing and fetch 55 MB again on every check.
    /// </summary>
    [Fact]
    public void TheArchiveSaysWhatItActuallyBringsBecauseItIsNotTheWholeList()
    {
        var mod = Expansion();
        var layout = GameTitle.UltraSun.Layout();
        var zip = mod.Downloads.First(d => d.Kind == ModDownloadKind.Archive);

        Assert.Contains("romfs/" + layout.Personal, zip.Provides);
        Assert.Contains("romfs/" + layout.Moves, zip.Provides);
        // The release's own notes: "all languages support added besides Simplified and Traditional Chinese".
        Assert.Equal(layout.LanguageCount - 2, zip.Provides.Count(p => p.StartsWith("romfs/a/0/3/", StringComparison.Ordinal)));
        // The ROM packer never writes these anyway, and the release has not got them.
        Assert.DoesNotContain("exefs/icon.bin", zip.Provides);
        Assert.DoesNotContain("exefs/banner.bin", zip.Provides);
        // The model archive comes apart, so it is never expected out of the zip.
        Assert.DoesNotContain("romfs/a/0/9/4", zip.Provides);
    }

    /// <summary>
    /// Every source has a page a human can open, because the last link of the chain is always "do it by hand" — a
    /// failure has to say where to go, never just that it failed.
    /// </summary>
    [Fact]
    public void EverySourceHasAPageToFallBackTo()
    {
        Assert.All(Expansion().Downloads, d => Assert.False(string.IsNullOrWhiteSpace(d.Source.PageUrl)));
    }

    /// <summary>
    /// <b>The author declares two different licences and the application says so</b>: CC0 on the mod's repository,
    /// CC BY-NC-SA 4.0 on its GameBanana page. They are not compatible — CC0 waives everything, BY-NC-SA forbids
    /// commercial use and requires share-alike — and nothing here is in a position to decide which one is meant, so
    /// both are carried with the place that states them and neither is presented as <i>the</i> licence.
    /// </summary>
    [Fact]
    public void TwoLicencesAreDeclaredAndNeitherIsChosen()
    {
        var mod = Expansion();

        Assert.Equal(2, mod.Licenses.Count);
        Assert.Contains(mod.Licenses, l => l.Name == "CC0-1.0" && l.Where == "GitHub");
        Assert.Contains(mod.Licenses, l => l.Name == "CC BY-NC-SA 4.0" && l.Where == "GameBanana");

        // The disagreement is a fact the model states, and the text names both with where each is written.
        Assert.True(mod.LicensesDisagree);
        Assert.Contains("CC0-1.0 (GitHub)", mod.LicenseText);
        Assert.Contains("CC BY-NC-SA 4.0 (GameBanana)", mod.LicenseText);
        Assert.NotNull(mod.LicenseWarning);
        Assert.Contains(mod.LicenseText, mod.LicenseWarning!);
    }

    /// <summary>One licence, declared in one place, is not a disagreement and gets no warning.</summary>
    [Fact]
    public void OneLicenceDeclaredOnceIsNotADisagreement()
    {
        var mod = ModCatalog.Find(ModCatalog.FollowerId)!;

        Assert.Single(mod.Licenses);
        Assert.False(mod.LicensesDisagree);
        Assert.Null(mod.LicenseWarning);
    }

    [Fact]
    public void AnUnknownIdIsNotFound() => Assert.Null(ModCatalog.Find("no-such-mod"));
}
