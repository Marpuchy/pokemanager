using Pokemanager.Model.Dump;
using Pokemanager.Model.Resources;

namespace Pokemanager.Model.Mods;

/// <summary>Where a file of a mod goes: the game's romfs or exefs, or the emulator's plugin folder.</summary>
public enum ModFileArea
{
    RomFs,
    ExeFs,

    /// <summary>
    /// Not part of the game at all: a plugin the emulator loads at run time, which lives in the emulator's own
    /// <c>sdmc/luma/plugins/&lt;TitleID&gt;</c> folder. Nothing of it is ever packed into a ROM.
    /// </summary>
    Plugin,
}

/// <summary>
/// One file a mod replaces, as the catalogue describes it beforehand: the path it is served at inside the game and,
/// when it is known, the hash the file of that release has.
/// </summary>
/// <remarks>
/// The catalogue is a description, not an inventory: a mod that replaces whole GARCs may ship more files than are named
/// here, and <see cref="ModLibrary"/> takes whatever the user actually provides. What this list is for is saying what
/// should be there (so a missing piece is reported instead of silently doing nothing) and checking the files that do
/// arrive against a known hash.
/// </remarks>
/// <param name="Area">Which half of the game the file belongs to.</param>
/// <param name="Path">
/// Path inside that half, as the game addresses it: <c>a/0/1/7</c> in the romfs, <c>code.bin</c> in the exefs.
/// </param>
/// <param name="Sha256">
/// The hash of this file in the release the definition describes, in upper-case hex, or <b>null when it is not known
/// yet</b>. A null is never filled in with a guess: the installation then reports
/// <see cref="ModFileState.HashUnknown"/> and says the file could not be confirmed, which is the truth.
/// </param>
public sealed record ModFile(ModFileArea Area, string Path, string? Sha256 = null)
{
    /// <summary>The build refuses to go on without this file: without it the mod does nothing at all.</summary>
    public bool Required { get; init; }

    /// <summary>
    /// The file is distributed apart from the rest of the mod (the Pokémon models of the USUM Expansion Mod are too
    /// large for its repository), so a library without it is not broken — it is a mod the user has not completed.
    /// </summary>
    public bool Apart { get; init; }

    /// <summary>
    /// Whether this file is written into the ROM Pokemanager packs. The packer only takes romfs files and the
    /// executable (see <c>PokemanagerUpr.pack</c>), so files like <c>icon.bin</c> and <c>banner.bin</c> are kept in the
    /// library and verified but not carried into the ROM yet.
    /// </summary>
    public bool IntoRom { get; init; } = Area != ModFileArea.Plugin;

    /// <summary>Whether the hash of this file is known, so a file given for it can be confirmed.</summary>
    public bool HashKnown => Sha256 is not null;

    /// <summary>
    /// The path inside the library and inside the packed title: <c>romfs/a/0/1/7</c>, <c>exefs/code.bin</c>. The ROM
    /// packer wants the executable as plain <c>code.bin</c>, which is what <see cref="TitlePath"/> gives.
    /// </summary>
    public string ServedPath => Area switch
    {
        ModFileArea.RomFs => "romfs/" + Path,
        ModFileArea.ExeFs => "exefs/" + Path,
        _ => PluginPrefix + Path,
    };

    /// <summary>Where a plugin file is kept inside the library: <c>plugin/Gen7FieldFollower.3gx</c>.</summary>
    public const string PluginPrefix = "plugin/";

    /// <summary>It is loaded by the emulator, not written into the game.</summary>
    public bool IsPlugin => Area == ModFileArea.Plugin;

    /// <summary>
    /// The path the ROM packer uses: romfs files keep their <c>romfs/</c> prefix and the executable is the bare
    /// <c>code.bin</c>, which is the shape <c>RomBuilder</c> already accepts.
    /// </summary>
    public string TitlePath => Area switch
    {
        ModFileArea.RomFs => "romfs/" + Path,
        ModFileArea.ExeFs => Path,
        // A plugin never reaches a title, so there is no packer path for it; its served path is all there is.
        _ => ServedPath,
    };
}

/// <summary>
/// What kind of thing a mod is, which decides everything that happens to it. <b>The two are not variants of one
/// thing</b>: one is built into the ROM and the other is a file the emulator loads, and almost nothing of the
/// pipeline applies to both.
/// </summary>
public enum ModKind
{
    /// <summary>
    /// It brings files to the game: they are stacked over the dump (<c>Project.ReadLayers</c>), packed into a base ROM
    /// (<c>ModdedRomCache</c>) and therefore part of what the project builds — so turning one on asks for a build, is
    /// part of the randomization's cache key and tells two versions of the history apart.
    /// </summary>
    RomFiles,

    /// <summary>
    /// It is a <c>.3gx</c> plugin the emulator loads at run time: no romfs, no exefs, no <c>code.bin</c>. There is
    /// nothing to pack and nothing to build — installing it is copying one file into the emulator's own
    /// <c>sdmc/luma/plugins/&lt;TitleID&gt;</c> folder (<see cref="ModPluginInstaller"/>). <b>It does not change the
    /// ROM</b>, so it is not a layer, not in the cache key and not in the history's fingerprint, and it needs the
    /// emulator's 3GX plugin loader to be switched on to do anything at all.
    /// </summary>
    EmulatorPlugin,
}

/// <summary>
/// A licence as <b>one place</b> states it. There is a list of these and not a single string because an author can
/// state different licences in different places, and the honest thing is then to say so rather than pick one.
/// </summary>
/// <param name="Name">The licence as it is named there: <c>CC0-1.0</c>, <c>CC BY-NC-SA 4.0</c>.</param>
/// <param name="Where">Where it says so, as a person would name it: <c>GitHub</c>, <c>GameBanana</c>.</param>
/// <param name="Url">The page that states it, when there is one to point at.</param>
public sealed record ModLicense(string Name, string Where, string? Url = null);

/// <summary>
/// A community mod this application knows about: what it is, who made it, which games it is for, where it comes from and
/// which files it replaces.
/// </summary>
/// <remarks>
/// <para>
/// <b>Pokemanager distributes not a single byte of a mod.</b> This type is metadata and links: the files come from
/// <see cref="SourceUrl"/> and the author's own hosts into the user's own library (<see cref="ModLibrary"/>), exactly
/// as the ROMs do. Since 2026-11 it can make that download itself (<see cref="Downloads"/>, <see cref="ModDownloader"/>)
/// — straight from the author's source, never through anything of ours: nothing is hosted, mirrored or cached to pass
/// on, and the library being per user and per machine makes it one download per user and not one per project. The
/// licence a mod declares covers the mod's own work and not material with somebody else's copyright in it — the USUM
/// Expansion Mod's model file carries models extracted from the Switch games — which is why none of it travels with
/// the application.
/// </para>
/// <para>
/// A <see cref="ModKind.RomFiles"/> mod replaces whole files rather than editing fields, so it cannot share the game's
/// tables with the randomizer: the pipeline packs a base ROM with the mod applied and hands <i>that</i> to UPR, so the
/// randomization sees the mod's species. See <c>ModdedRomCache</c>. <b>None of that applies to a
/// <see cref="ModKind.EmulatorPlugin"/> mod</b>, which is not in the ROM at all: see <see cref="ModPluginInstaller"/>.
/// </para>
/// </remarks>
public sealed record ModDefinition(
    string Id,
    string Name,
    string Author,
    string Description,
    string Version,
    string SourceUrl,
    IReadOnlyList<ModLicense> Licenses,
    IReadOnlyList<GameTitle> Games,
    IReadOnlyList<ModFile> Files)
{
    /// <summary>
    /// Which of the two classes of mod this is, and so what the whole pipeline does with it. Defaults to
    /// <see cref="ModKind.RomFiles"/>, which is what every mod was before plugins existed.
    /// </summary>
    public ModKind Kind { get; init; } = ModKind.RomFiles;

    /// <summary>It is a plugin the emulator loads, not files for the ROM.</summary>
    public bool IsPlugin => Kind == ModKind.EmulatorPlugin;

    /// <summary>
    /// It puts files in the ROM, so it is a layer, it is packed into a base ROM, it asks for a build and it belongs in
    /// the randomization's cache key and the history's fingerprint. A plugin does none of that.
    /// </summary>
    public bool ChangesTheRom => Kind == ModKind.RomFiles;

    /// <summary>The plugin file this mod installs, or null when it is not that kind of mod.</summary>
    public ModFile? PluginFile => Files.FirstOrDefault(f => f.IsPlugin);

    /// <summary>
    /// What the user has to switch on <b>outside</b> Pokemanager for the mod to do anything — a 3GX plugin does
    /// nothing at all until the emulator's plugin loader is enabled, and without this note it would simply look
    /// broken. Null when there is nothing to do.
    /// </summary>
    public string? SetupNote { get; init; }

    /// <summary>
    /// What this mod will not work alongside, and what to do about it. Carried with the entry because it is what the
    /// user needs to know <i>before</i> installing, not after wondering why nothing loads.
    /// </summary>
    public IReadOnlyList<string> Conflicts { get; init; } = [];

    /// <summary>
    /// <b>The declarations disagree</b>: the same author states a different licence depending on where you look.
    /// Pokemanager does not choose between them — it says both and where each one is written.
    /// </summary>
    public bool LicensesDisagree =>
        Licenses.Select(l => l.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;

    /// <summary>
    /// Every licence the mod is declared under, each with the place that declares it:
    /// <c>CC0-1.0 (GitHub) · CC BY-NC-SA 4.0 (GameBanana)</c>. Never one name on its own when there are two.
    /// </summary>
    public string LicenseText => string.Join(" · ", Licenses.Select(l => $"{l.Name} ({l.Where})"));

    /// <summary>
    /// A sentence for the user when the declarations disagree, naming both and saying that Pokemanager does not pick
    /// one; null when they agree. The application <b>states what the author states</b> and no more: deciding which
    /// licence is the real one is not ours to do.
    /// </summary>
    public string? LicenseWarning =>
        LicensesDisagree ? string.Format(Strings.Mods_LicenseDisagrees, LicenseText) : null;

    /// <summary>Things the user should know before playing with it, in the mod's own words where there are any.</summary>
    public IReadOnlyList<string> KnownIssues { get; init; } = [];

    /// <summary>
    /// What the user has to get somewhere else, and where it is said to be. Empty when the whole mod comes from
    /// <see cref="SourceUrl"/>. Only a note: no code here fetches anything.
    /// </summary>
    public string? SeparateDownloadNote { get; init; }

    /// <summary>
    /// Where the files come from, in the order the chain tries them: an archive of a release that unpacks into many of
    /// the files above, and the single files that come apart from it or replace one of its own (the executable of a
    /// point release). A later download wins over an earlier one.
    /// </summary>
    /// <remarks>
    /// <b>Pokemanager fetches from the author's own source and nothing else.</b> It does not host, mirror or keep a
    /// copy to pass on: the library is per user and per machine, so this is one download by one user, exactly the one
    /// they would have made by hand. Empty means this version knows of no automatic source and the user brings the
    /// files, which is what every mod did before this existed.
    /// </remarks>
    public IReadOnlyList<ModDownload> Downloads { get; init; } = [];

    /// <summary>Whether anything at all can be fetched for this mod.</summary>
    public bool CanDownload => Downloads.Any(d => d.Source.CanFetch);

    /// <summary>
    /// The downloads that serve a game, in catalogue order. This is what picks Ultra Sun's asset for an Ultra Sun
    /// project and Ultra Moon's for an Ultra Moon one, instead of handing over whichever came first.
    /// </summary>
    public IReadOnlyList<ModDownload> DownloadsFor(GameTitle game) => [.. Downloads.Where(d => d.AppliesTo(game))];

    /// <summary>
    /// The served paths an archive is taken to provide for that game: every file the catalogue names that no single
    /// file of its own claims, and that does not come apart. It is what lets a download be skipped when the library
    /// already holds what it would bring — the archive does not have to list its contents for that.
    /// </summary>
    public IReadOnlyList<string> PathsFromArchive(GameTitle game)
    {
        var claimed = DownloadsFor(game)
            .Where(d => d.Kind == ModDownloadKind.SingleFile && d.ServedPath is not null)
            .Select(d => d.ServedPath!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return [.. Files.Where(f => !f.Apart && !claimed.Contains(f.ServedPath)).Select(f => f.ServedPath)];
    }

    /// <summary>Whether the mod applies to that game.</summary>
    public bool Supports(GameTitle game) => Games.Contains(game);

    /// <summary>Files the build writes into the packed ROM (romfs files and the executable).</summary>
    public IEnumerable<ModFile> RomFiles => Files.Where(f => f.IntoRom);

    /// <summary>Files without which the mod does nothing, so the build refuses.</summary>
    public IEnumerable<ModFile> RequiredFiles => Files.Where(f => f.Required);

    /// <summary>Whether any file of this definition carries a hash, i.e. whether a library can be confirmed at all.</summary>
    public bool HasKnownHashes => Files.Any(f => f.HashKnown);

    /// <summary>The description of a served path, or null when the definition does not name that file.</summary>
    public ModFile? File(string servedPath) =>
        Files.FirstOrDefault(f => string.Equals(f.ServedPath, servedPath, StringComparison.OrdinalIgnoreCase));
}
