using Pokemanager.Model.Dump;
using Pokemanager.Model.Resources;

namespace Pokemanager.Model.Mods;

/// <summary>
/// The community mods this version of Pokemanager knows how to apply: what they are, and where each of their files
/// comes from. <b>No file of a mod is distributed with the application</b> — the entries here point at the authors'
/// own releases and hosts, and <see cref="ModDownloader"/> fetches from those and nowhere else
/// (see <see cref="ModDefinition"/>).
/// </summary>
/// <remarks>
/// <b>Two kinds of thing live here and they are treated differently</b> (<see cref="ModKind"/>): one brings files to
/// the ROM and is built into the game, the other is a 3GX plugin the emulator loads beside it. Nothing of the build
/// path applies to the second: it is not a layer, nothing is packed for it, and installing it is a file copy into the
/// emulator's own folder.
/// </remarks>
public static class ModCatalog
{
    /// <summary>The USUM Expansion Mod: the Pokémon of Generations 8 and 9 in Ultra Sun and Ultra Moon.</summary>
    public const string UsumExpansionId = "usum-expansion";

    /// <summary>Aqua_'s follower mod: a 3GX plugin, not files for the ROM. Sun, Moon, Ultra Sun and Ultra Moon.</summary>
    public const string FollowerId = "gen7-follower";

    public static IReadOnlyList<ModDefinition> All { get; } = [UsumExpansion(), Follower()];

    /// <summary>The definition with that id, or null when this version does not know it.</summary>
    public static ModDefinition? Find(string id) =>
        All.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>The mods that apply to a game.</summary>
    public static IEnumerable<ModDefinition> For(GameTitle game) => All.Where(m => m.Supports(game));

    /// <summary>
    /// Aqua's expansion of Ultra Sun and Ultra Moon: it raises the 807 species the games ship with to about 1025 by
    /// replacing whole GARCs (an expanded personal table, the moves, the texts), a modified <c>code.bin</c> and the
    /// exefs <c>icon.bin</c> and <c>banner.bin</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>No per-file hash is known yet.</b> Every <see cref="ModFile.Sha256"/> here is null on purpose: a
    /// <see cref="ModFile"/> describes a file for both games at once, and the releases differ between Ultra Sun and
    /// Ultra Moon (the executables and the text archives are not the same bytes), so a single hash could only be right
    /// for one of them and a made-up one would reject the real release. Until a per-game hash has somewhere to live, a
    /// library reports <see cref="ModFileState.HashUnknown"/> for each file and says plainly that it cannot confirm the
    /// version — which is the honest answer, instead of "this is 1.41" when nothing was checked.
    /// </para>
    /// <para>
    /// <b>The downloads are a different matter and are hashed.</b> A <see cref="ModDownload"/> is per game, so its
    /// <see cref="ModDownload.Sha256"/> can be the measured truth: the four GitHub assets below were downloaded and
    /// hashed, and a download whose bytes do not match is thrown away without touching the library.
    /// </para>
    /// <para>
    /// The file list is what the mod is known to replace, not an inventory of the release: the mod ships whole archives
    /// and may carry more than this. <see cref="ModLibrary"/> packs everything the user actually provides, and uses
    /// this list to say what is missing.
    /// </para>
    /// <para>
    /// The model archive <c>a/0/9/4</c> is distributed apart from the repository because of its size, and it holds
    /// models taken from the Switch games: the CC0 the mod declares covers the mod's own work, not somebody else's
    /// copyright, so the user fetches it themselves and nothing of it is ever packaged here.
    /// </para>
    /// </remarks>
    /// <summary>The commit of the Expansion's repository whose executable and Battle.cro the catalogue fetches.</summary>
    private const string UsumExpansionCommit = "0b50f8436b743301f727018f5fd6612ee4c39781";

    /// <summary>A file of the Expansion's repository at <see cref="UsumExpansionCommit"/>, for one game's folder.</summary>
    private static ModSource RepositoryFile(string game, string path) => ModSource.Direct(
        $"https://raw.githubusercontent.com/TheSomewhatUnknownGuy/PokemonUSUMExpansion/{UsumExpansionCommit}/{game}_ExpansionNoModelAndEncounterFile/{path}",
        "https://github.com/TheSomewhatUnknownGuy/PokemonUSUMExpansion",
        $"{game} {Path.GetFileName(path)}");

    private static ModDefinition UsumExpansion()
    {
        var layout = GameTitle.UltraSun.Layout();
        var files = new List<ModFile>
        {
            // The expanded personal table is the mod: without it the game has the species it shipped with.
            new(ModFileArea.RomFs, layout.Personal) { Required = true },
            // The modified executable has to travel into the packed ROM, or the expanded data has nothing reading it.
            new(ModFileArea.ExeFs, "code.bin") { Required = true },
            new(ModFileArea.RomFs, layout.Moves),
            new(ModFileArea.RomFs, layout.LevelUp),
            new(ModFileArea.RomFs, layout.Evolution),
            new(ModFileArea.RomFs, layout.Items),
            new(ModFileArea.RomFs, layout.Icons[0]),
        };
        // The game texts: names and descriptions of what the mod adds, one archive per language.
        for (int language = 0; language < layout.LanguageCount; language++)
            files.Add(new ModFile(ModFileArea.RomFs, GameImporter.GarcPath(layout.GameText + language)));
        // The home-menu icon and banner: the library keeps and checks them, but the ROM packer only takes romfs files
        // and the executable, so they are not written into the ROM yet.
        files.Add(new ModFile(ModFileArea.ExeFs, "icon.bin") { IntoRom = false });
        files.Add(new ModFile(ModFileArea.ExeFs, "banner.bin") { IntoRom = false });
        // The models, distributed apart because of their size.
        files.Add(new ModFile(ModFileArea.RomFs, "a/0/9/4") { Apart = true });

        // ---------------------------------------------------------------- where the files come from
        // Verified against the GitHub release API and by downloading each asset on 2026-11-02: the names, the sizes
        // and the hashes below are measured, not guessed. The newest release, v1.41, carries *only* the executables;
        // the bulk of the mod is still the pair of zips of v1.4, which is why the chain is "the v1.4 zip, then the
        // v1.41 code.bin over it" — exactly the order the author's own release notes describe.
        const string repo = "TheSomewhatUnknownGuy/PokemonUSUMExpansion";

        // What the zip of a release really brings. Said out loud because it is not the whole declared list: the
        // release has no icon.bin or banner.bin, and no Simplified or Traditional Chinese text archive (its own notes
        // say "all languages support added besides Simplified and Traditional Chinese"). Without this the downloader
        // would see those as missing and fetch 55 MB again on every check.
        string[] fromZip =
        [
            "romfs/" + layout.Personal,
            "romfs/" + layout.Moves,
            "romfs/" + layout.LevelUp,
            "romfs/" + layout.Evolution,
            "romfs/" + layout.Items,
            "romfs/" + layout.Icons[0],
            .. Enumerable.Range(0, layout.LanguageCount - 2)
                .Select(language => "romfs/" + GameImporter.GarcPath(layout.GameText + language)),
        ];

        List<ModDownload> downloads =
        [
            // The mod itself, one zip per game, laid out as the game is — so it unpacks straight into the library.
            ModDownload.Archive(ModSource.GitHub(repo, "v1.4", "UltraSunExpansion_NoModelFile.zip"), GameTitle.UltraSun) with
            {
                Bytes = 55_792_126,
                Sha256 = "5E42414B2D414F496B8D83A9399CC8E5C4E2BD74E10D3B647D7CB6065137A4AE",
                Provides = fromZip,
            },
            ModDownload.Archive(ModSource.GitHub(repo, "v1.4", "UltraMoonExpansion_NoModelFile.zip"), GameTitle.UltraMoon) with
            {
                Bytes = 55_814_263,
                Sha256 = "3BA34C85BA89972C3AC05C67E9144384C2F492D1C52D6BFBFD3AF8EC1BB4C338",
                Provides = fromZip,
            },
            // After v1.4 the author publishes the executable and Battle.cro straight to the repository: v1.41 was only
            // the executables, v1.42 has no assets at all, and the newest fixes (2026-10-22..26: two-byte abilities in
            // battle, Trace / Role Play / Entrain, a bigger heap for the personal data) are only in the repository's
            // main branch. They are fetched pinned to that commit, so the bytes cannot change under the hash. Measured
            // on 2026-11-09: against v1.4 + v1.41 these two files are the only ones of the release that differ. Moved to
            // 0b50f84 on 2026-11-10 ("more Ability fixes for two bytes (this was causing a battle AI issue)"): only
            // Battle.cro changed, same size, hash measured. One per
            // game for the executable (an Ultra Moon one in an Ultra Sun library would not boot); Battle.cro is the
            // same file for both.
            .. new[] { (GameTitle.UltraSun, "UltraSun", 5_914_624L, "1249622FEC32B9209932965BB1B100636A76AF79E8ABDFD5EB5A5A12D039986E"),
                       (GameTitle.UltraMoon, "UltraMoon", 5_914_624L, "C3A3D7647DB2E8FAC854D2C5F8F938BC3B2648961C5D980D9AF8D7ABE28D9317") }
                .SelectMany(g => new[]
                {
                    ModDownload.File(RepositoryFile(g.Item2, "exefs/code.bin"), "exefs/code.bin", g.Item1) with
                    {
                        Bytes = g.Item3,
                        Sha256 = g.Item4,
                    },
                    ModDownload.File(RepositoryFile(g.Item2, "romfs/Battle.cro"), "romfs/Battle.cro", g.Item1) with
                    {
                        Bytes = 1_425_408,
                        Sha256 = "0E1D0BCC5E7D18E9AC3A0DA94D5775A18377229C27C0C0EABEFCD4449E729223",
                    },
                }),
            // The model archive: 2.3 GB, far past what a repository takes, so the author keeps it in Drive and links
            // it from the repository's own description. The id is the one that description points at and the only one
            // of the three in circulation that still resolves (the links in the v1.3 notes are both dead). Its hash
            // is not known: it was not downloaded to measure, and a made-up one would reject the real file — the
            // downloader computes and reports it, so it can be filled in here afterwards.
            ModDownload.File(ModSource.Drive("1opjwsUhXLgcGV99Xihd3o2mUe554_bbC"), "romfs/a/0/9/4") with
            {
                Bytes = 2_266_714_724,
                Apart = true,
            },
        ];

        return new ModDefinition(
            UsumExpansionId,
            Strings.Mods_UsumExpansion_Name,
            Strings.Mods_UsumExpansion_Author,
            Strings.Mods_UsumExpansion_Description,
            "1.42 (0b50f84)",
            "https://github.com/TheSomewhatUnknownGuy/PokemonUSUMExpansion",
            // Two declarations that do not agree, both the author's own: CC0 waives everything, CC BY-NC-SA does not
            // (it forbids commercial use and requires share-alike). The application says both and where each one is
            // written, and does not decide which is the real one — that is not ours to decide.
            [
                new ModLicense("CC0-1.0", "GitHub", "https://github.com/TheSomewhatUnknownGuy/PokemonUSUMExpansion"),
                new ModLicense("CC BY-NC-SA 4.0", "GameBanana"),
            ],
            [GameTitle.UltraSun, GameTitle.UltraMoon],
            files)
        {
            Summary = Strings.Mods_UsumExpansion_Summary,
            KnownIssues =
            [
                Strings.Mods_UsumExpansion_Issue_Learnsets,
                Strings.Mods_UsumExpansion_Issue_UpperHand,
                Strings.Mods_UsumExpansion_Issue_Baxcalibur,
            ],
            SeparateDownloadNote = Strings.Mods_UsumExpansion_Models,
            Downloads = downloads,
        };
    }

    /// <summary>
    /// Aqua_'s follower mod: the Pokémon of your party's first slot walks behind you around the map, with an in-game
    /// menu on Start+Select. <b>A different class of mod entirely</b> — a 3GX plugin the emulator loads beside the
    /// game.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It changes no byte of the ROM.</b> No romfs, no exefs, no <c>code.bin</c>: there is nothing to overlay,
    /// nothing to pack into a base ROM and nothing to build, so it is not a read layer, it is not in the randomization
    /// cache key and it is not in the history's fingerprint (<see cref="ModKind.EmulatorPlugin"/>). Installing it is
    /// one file copied into <c>sdmc/luma/plugins/&lt;TitleID&gt;</c> of the emulator's own user folder
    /// (<see cref="ModPluginInstaller"/>), and it does nothing at all until the emulator's 3GX plugin loader is
    /// switched on — which is why <see cref="ModDefinition.SetupNote"/> is not optional decoration here.
    /// </para>
    /// <para>
    /// <b>Four games, four downloads, and the right one has to be picked.</b> Each zip is built for one title, and the
    /// title ids come from <see cref="GameTitle"/> rather than being written out again here, so the application has
    /// one idea of what Sun's title id is and not two.
    /// </para>
    /// <para>
    /// <b>No hash is known.</b> The four zips were not downloaded and hashed, so every <see cref="ModFile.Sha256"/>
    /// and every <see cref="ModDownload.Sha256"/> below is null on purpose: the model already says out loud that it
    /// cannot confirm the file (<see cref="ModFileState.HashUnknown"/>), and a made-up hash would reject the real
    /// release. The downloader measures and reports what arrived, so they can be filled in from a measurement.
    /// </para>
    /// <para>
    /// The licence is the one its GameBanana page declares, <b>CC BY-NC-ND 4.0</b> — no derivatives, which is one more
    /// reason nothing of the file travels with the application: it is fetched from the author's own page into the
    /// user's own library and copied from there into their own emulator.
    /// </para>
    /// </remarks>
    private static ModDefinition Follower()
    {
        const string page = "https://gamebanana.com/mods/694400";

        // One file, and it is the whole mod: without it there is nothing to install.
        var plugin = new ModFile(ModFileArea.Plugin, "Gen7FieldFollower.3gx") { Required = true };

        // One zip per game, each 310,732 bytes, each holding a luma folder with that same plugin inside. The links
        // answer 302 towards GameBanana's file cache, which the fetcher follows on its own.
        (GameTitle Game, string Asset, string Url)[] assets =
        [
            (GameTitle.UltraSun, "ultra_sun_c8a6f.zip", "https://gamebanana.com/dl/1773645"),
            (GameTitle.UltraMoon, "ultra_moon_1e982.zip", "https://gamebanana.com/dl/1773643"),
            (GameTitle.Sun, "sun_cef82.zip", "https://gamebanana.com/dl/1773644"),
            (GameTitle.Moon, "moon_6cd18.zip", "https://gamebanana.com/dl/1773642"),
        ];

        var downloads = assets
            .Select(a => ModDownload.Archive(ModSource.Direct(a.Url, page, a.Asset), a.Game) with
            {
                Bytes = 310_732,
                Provides = [plugin.ServedPath],
            })
            .ToList();

        return new ModDefinition(
            FollowerId,
            Strings.Mods_Follower_Name,
            Strings.Mods_Follower_Author,
            Strings.Mods_Follower_Description,
            "3.1",
            page,
            [new ModLicense("CC BY-NC-ND 4.0", "GameBanana", page)],
            [GameTitle.Sun, GameTitle.Moon, GameTitle.UltraSun, GameTitle.UltraMoon],
            [plugin])
        {
            Kind = ModKind.EmulatorPlugin,
            Summary = Strings.Mods_Follower_Summary,
            SetupNote = Strings.Mods_Follower_Setup,
            KnownIssues =
            [
                Strings.Mods_Follower_Issue_FormCycling,
                Strings.Mods_Follower_Issue_Untested,
                Strings.Mods_Follower_Issue_OldHardware,
                Strings.Mods_Follower_Issue_FlyingHeight,
                Strings.Mods_Follower_Issue_NoPokemon,
            ],
            Conflicts =
            [
                Strings.Mods_Follower_Conflict_OnePlugin,
                Strings.Mods_Follower_Conflict_LayeredFs,
                Strings.Mods_Follower_Conflict_Expansion,
            ],
            Downloads = [.. downloads],
        };
    }
}
