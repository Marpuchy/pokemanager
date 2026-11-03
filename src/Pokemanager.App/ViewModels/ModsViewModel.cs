using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pokemanager.Bridge;
using Pokemanager.App.Resources;
using Pokemanager.Model.Data;
using Pokemanager.Model.Dump;
using Pokemanager.Model.Editing;
using Pokemanager.Model.Mods;

namespace Pokemanager.App.ViewModels;

/// <summary>
/// Juego (ROM) → <b>Mods</b>: the community mods this version knows about, for the project's game. One card per mod,
/// saying who made it, what it does, what it is known to get wrong, exactly what the user's own library holds for it,
/// and — when it is on — what the ROM really gained, measured in the ROM.
/// </summary>
/// <remarks>
/// <para>
/// <b>Pokemanager hosts nothing.</b> A card can fetch what the library is missing straight from the author's own
/// release (<see cref="ModDownloader"/>), and behind that there is always the button that opens the mod's page in the
/// system browser and the two Import buttons — the chain degrades to "do it by hand" instead of failing. Nothing is
/// mirrored or cached to pass on, and the library is per user and per machine, so it is one download by one user.
/// </para>
/// <para>
/// It lives in the ROM half of the shell because a mod is built into the game: turning one on asks for a <i>build</i>
/// and never for a new seed, like the shops and the game options.
/// </para>
/// <para>
/// <b>Two kinds of card live here</b> (<see cref="ModKind"/>). One is that: a mod built into the ROM, with an Enable
/// check box and a measured block saying what the ROM gained. The other is a <b>3GX plugin the emulator loads</b>,
/// which changes no byte of the ROM — it has no check box and nothing measured, because there is nothing to build;
/// what it has instead is an Install and a Remove that copy one file into the emulator's own plugin folder, the state
/// of that folder read back, and the two things that make a correctly installed plugin do nothing all the same: the
/// emulator's plugin loader being off, and another plugin already being in the folder.
/// </para>
/// </remarks>
public sealed partial class ModsViewModel : ObservableObject
{
    private readonly EditorViewModel editor;
    private bool loaded;

    public ModsViewModel(EditorViewModel editor)
    {
        this.editor = editor;
        // Built once, in the constructor, and never replaced. This project's rule: a list bound to an ItemsSource is
        // built once — a property that hands back a new collection on every read made a ComboBox drop its selection
        // and write the old value back, three times over in this application's history.
        Mods = [.. ModCatalog.For(editor.Session.Project.Game).Select(m => new ModCardViewModel(editor, m))];
    }

    /// <summary>One card per catalogued mod that applies to this game. Built once.</summary>
    public IReadOnlyList<ModCardViewModel> Mods { get; }

    public bool HasMods => Mods.Count > 0;

    /// <summary>What a game with no catalogued mod says, instead of an empty page.</summary>
    public string NoneText => string.Format(Strings.Mods_NoneForGame, editor.Session.Project.Game.DisplayName());

    /// <summary>Where the user's own copies live: <c>&lt;data&gt;/mods</c>, one folder per mod.</summary>
    public string LibraryText => string.Format(Strings.Mods_LibraryIs, editor.Session.Mods.Root);

    /// <summary>
    /// Reads the library the first time the tab is shown, the way the save is read when its own tab is. Hashing a mod's
    /// files is real disk work — the model archive is distributed apart precisely because of its size — so it is not
    /// done while opening a project that may never visit this page.
    /// </summary>
    public void EnsureLoaded()
    {
        if (loaded)
            return;
        loaded = true;
        _ = RefreshAllAsync();
    }

    /// <summary>All the cards at once: a small plugin is not kept waiting behind a mod with gigabytes to hash.</summary>
    public Task RefreshAllAsync() => Task.WhenAll(Mods.Select(card => card.RefreshAsync()));

    /// <summary>
    /// The session was reopened (a mod went on or off, a build landed): what the cards measure in the ROM moved, while
    /// what the library holds did not, so only the measured half is read again.
    /// </summary>
    public void RefreshMeasured()
    {
        foreach (var card in Mods)
            card.RefreshMeasured();
    }
}

/// <summary>One mod: who it is, what the library holds for it, and what it does to this project.</summary>
public sealed partial class ModCardViewModel : ObservableObject
{
    private readonly EditorViewModel editor;
    private bool busy;

    public ModCardViewModel(EditorViewModel editor, ModDefinition mod)
    {
        this.editor = editor;
        Mod = mod;
        // Built once, like every list bound to an ItemsSource here (see the ComboBox rule in ModsViewModel).
        KnownIssues = [.. mod.KnownIssues];
        Conflicts = [.. mod.Conflicts];
        FilesSummary = Strings.Mods_Checking; // until the folder has been read (when the tab is first shown)
        PluginStateText = Strings.Mods_Checking;
    }

    public ModDefinition Mod { get; }

    // ------------------------------------------------------------------ who it is and what it does

    public string Name => Mod.Name;

    /// <summary>
    /// "Aqua · version 1.41 · CC0-1.0 (GitHub) · CC BY-NC-SA 4.0 (GameBanana)": author, the release the catalogue
    /// describes, and <b>every licence the author declares with the place that declares it</b> — never one name on
    /// its own when there are two of them.
    /// </summary>
    public string Identity => string.Format(Strings.Mods_Identity, Mod.Author, Mod.Version, Mod.LicenseText);

    /// <summary>
    /// Said out loud when the author's own declarations disagree (the Expansion Mod's repository says CC0 and its
    /// GameBanana page says CC BY-NC-SA, which are not compatible): the card shows both and says Pokemanager does not
    /// choose. Null when they agree.
    /// </summary>
    public string? LicenseWarning => Mod.LicenseWarning;

    public bool HasLicenseWarning => LicenseWarning is not null;

    public string Description => Mod.Description;

    /// <summary>The one sentence the list shows; everything else is behind <see cref="ShowDetails"/>.</summary>
    public string Summary => Mod.Summary ?? Mod.Description;

    /// <summary>The card is open: who made it, its licences, its known faults, the files and the measurements.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailsText))]
    public partial bool ShowDetails { get; set; }

    public string DetailsText => ShowDetails ? Strings.Mods_HideDetails : Strings.Mods_Details;

    /// <summary>
    /// Something the mod needs is not in the library yet — a required file, or the one distributed apart — so the list
    /// offers the Download button. Hidden once everything is there.
    /// </summary>
    [ObservableProperty]
    public partial bool NeedsDownload { get; private set; } = true;

    public bool ShowDownload => LibraryRead && CanDownload && NeedsDownload && !IsDownloading;

    /// <summary>
    /// The library has been read at least once. Until then nothing is known: the line says it is checking, there is
    /// no Download button and the check box waits, instead of "not downloaded" for a mod that is all there.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(ShowDownload))]
    public partial bool LibraryRead { get; private set; }

    /// <summary>
    /// One line under the name saying where the mod stands: not downloaded, ready, on (and what the ROM gained), or for
    /// a plugin what the emulator's folder holds.
    /// </summary>
    public string StatusText => !LibraryRead
        ? Strings.Mods_Checking
        : IsPlugin
        ? PluginStateText
        : IsEnabled
            ? MeasuredText
            : Problem is not null && NeedsDownload
                ? Strings.Mods_StatusNotDownloaded
                : CanBuild ? Strings.Mods_StatusReady : Problem ?? Strings.Mods_StatusNotDownloaded;

    private void NotifyStatus()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(ShowDownload));
    }

    partial void OnIsDownloadingChanged(bool value) => NotifyStatus();

    /// <summary>Which games it is for, so a card is never read as applying to another one by accident.</summary>
    public string GamesText => string.Format(Strings.Mods_Games, string.Join(", ", Mod.Games.Select(g => g.DisplayName())));

    /// <summary>
    /// What the mod is known to get wrong, in its own words. Shown without being asked for: the point is deciding
    /// before a run and not finding out halfway through one.
    /// </summary>
    public IReadOnlyList<string> KnownIssues { get; }

    public bool HasKnownIssues => KnownIssues.Count > 0;

    public string KnownIssuesTitle => string.Format(Strings.Mods_KnownIssuesTitle, KnownIssues.Count);

    /// <summary>
    /// It is a 3GX plugin the emulator loads and not files for the ROM, which is what decides the whole card: no
    /// Enable check box, no build, nothing measured in the ROM — an Install and a Remove instead.
    /// </summary>
    public bool IsPlugin => Mod.IsPlugin;

    /// <summary>It brings files to the game, so it is built into the ROM: the card it always was.</summary>
    public bool IsRomMod => !Mod.IsPlugin;

    /// <summary>What the mod will not work alongside, from the author's own page. Built once.</summary>
    public IReadOnlyList<string> Conflicts { get; }

    public bool HasConflicts => Conflicts.Count > 0;

    /// <summary>
    /// What the user has to switch on outside Pokemanager — for a 3GX plugin, the emulator's own plugin loader,
    /// without which a correctly installed plugin does absolutely nothing and looks broken.
    /// </summary>
    public string? SetupNote => Mod.SetupNote;

    public bool HasSetupNote => SetupNote is not null;

    public string SourceUrl => Mod.SourceUrl;

    public string OpenSourceTip => string.Format(Strings.Mods_OpenSourceTip, Mod.SourceUrl);

    /// <summary>The note about the file distributed apart from the rest (the models), or null.</summary>
    public string? SeparateNote => Mod.SeparateDownloadNote;

    public bool HasSeparateNote => SeparateNote is not null;

    // ------------------------------------------------------------------ what the library holds

    /// <summary>Emptied and filled in place; the instance is never replaced.</summary>
    public ObservableCollection<ModFileRowViewModel> Files { get; } = [];

    /// <summary>The folder a release is unzipped into: <c>&lt;data&gt;/mods/&lt;id&gt;</c>.</summary>
    public string Folder => editor.Session.Mods.DirectoryFor(Mod.Id);

    public string FolderText => string.Format(Strings.Mods_FolderIs, Folder);

    [ObservableProperty]
    public partial string FilesSummary { get; private set; }

    /// <summary>
    /// What can honestly be said about which release these files are — <see cref="ModInstallation.VersionText"/>
    /// unchanged. With no hash known it says the version cannot be confirmed, and that is information, not an error to
    /// hide behind a version number nothing was checked against.
    /// </summary>
    [ObservableProperty]
    public partial string VersionText { get; private set; } = "";

    /// <summary>What stops the build, naming the file and the folder it goes in; null when nothing does.</summary>
    [ObservableProperty]
    public partial string? Problem { get; private set; }

    public bool HasProblem => Problem is not null;

    /// <summary>Every required file is there and nothing present is the wrong file.</summary>
    [ObservableProperty]
    public partial bool CanBuild { get; private set; }

    // ------------------------------------------------------------------ on or off in this project

    /// <summary>
    /// Whether this project plays with the mod. Writing it reopens the editing session over the new stack of layers and
    /// asks for a ROM <b>build</b> and not for a new seed: the mod is in the ROM, like the shops and the shiny patch.
    /// </summary>
    public bool IsEnabled
    {
        get => editor.Session.Project.Mods.Has(Mod.Id);
        set
        {
            var mods = editor.Session.Project.Mods;
            if (busy || mods.Has(Mod.Id) == value)
                return;
            // Turning it on with a required file missing would open the session over half a mod. Turning it off is
            // always allowed, which is why the control is not disabled: a mod whose library went wrong has to be
            // removable.
            if (value && !CanBuild)
            {
                editor.SetStatus(!LibraryRead
                    ? Strings.Mods_StillChecking
                    : Problem ?? string.Format(Strings.Mods_NothingYet, Mod.Name), error: true);
                // After the binding has finished writing, or the box stays ticked on a mod that is not on.
                Avalonia.Threading.Dispatcher.UIThread.Post(() => OnPropertyChanged(nameof(IsEnabled)));
                return;
            }

            busy = true;
            try
            {
                mods.Set(Mod.Id, value);
                if (editor.ReopenOverMods())
                {
                    // It is in the ROM, so the ROM has to be built again — and never randomized again.
                    editor.MarkDirty(string.Format(value ? Strings.Mods_UndoEnabled : Strings.Mods_UndoDisabled, Mod.Name));
                    // Saved at once: the whole application now reads the game with the mod (or without it), and a
                    // project left unsaved would reopen without it after a restart and look as if nothing had happened.
                    _ = SaveAfterToggleAsync(value);
                }
                else
                {
                    // The data could not be read with that stack: put the project back and reopen over what worked, so
                    // the application is never left reading something it failed to open.
                    mods.Set(Mod.Id, !value);
                    editor.ReopenOverMods();
                }
            }
            finally
            {
                busy = false;
            }
            OnPropertyChanged();
            RefreshMeasured();
        }
    }

    private async Task SaveAfterToggleAsync(bool enabled)
    {
        if (!await editor.SaveNowAsync())
            return;
        var data = editor.Session.Current;
        editor.SetStatus(enabled
            ? string.Format(Strings.Mods_EnabledStatus, Mod.Name, data.ExtraSpecies, data.Moves.Length - 1, data.Items.Length - 1)
            : string.Format(Strings.Mods_DisabledStatus, Mod.Name));
    }

    public string EnableTip => CanBuild
        ? Strings.Mods_EnableTip
        : string.Format(Strings.Mods_EnableBlockedTip, Problem ?? string.Format(Strings.Mods_NothingYet, Mod.Name));

    // ------------------------------------------------------------------ what the ROM really gained

    /// <summary>
    /// What is being read right now, measured in the ROM instead of promised by the catalogue:
    /// <see cref="GameData.ExtraSpecies"/> over <see cref="GameData.VanillaSpeciesCount"/>.
    /// </summary>
    [ObservableProperty]
    public partial string MeasuredText { get; private set; } = "";

    /// <summary>Whether the data being read really is expanded (<see cref="GameData.IsExpanded"/>).</summary>
    [ObservableProperty]
    public partial bool IsExpanded { get; private set; }

    [ObservableProperty]
    public partial string ShinyEffectText { get; private set; } = "";

    [ObservableProperty]
    public partial string ShopsEffectText { get; private set; } = "";

    [ObservableProperty]
    public partial bool ShinyFound { get; private set; }

    [ObservableProperty]
    public partial bool ShopsFound { get; private set; }

    /// <summary>The two side effects the other way round, for the styles that colour a line that is gone.</summary>
    public bool ShinyGone => !ShinyFound;

    public bool ShopsGone => !ShopsFound;

    /// <summary>The measured block only means anything while the mod is part of the base being read.</summary>
    public bool ShowMeasured => IsEnabled;

    // ------------------------------------------------------------------ reading the library

    /// <summary>
    /// Hashes everything the library holds for this mod and shows it, off the UI thread: a mod file can be hundreds of
    /// megabytes.
    /// </summary>
    public async Task RefreshAsync()
    {
        var library = editor.Session.Mods;
        ModInstallation found;
        try
        {
            found = await Task.Run(() => library.Inspect(Mod));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            FilesSummary = string.Format(Strings.Mods_ReadFailed, ex.Message);
            Problem = FilesSummary;
            LibraryRead = true;
            return;
        }

        Files.Clear();
        foreach (var file in found.Files)
            Files.Add(new ModFileRowViewModel(file, Folder));

        FilesSummary = string.Format(Strings.Mods_FilesSummary,
            found.Present.Count(), found.Files.Count(f => !f.Present), found.Wrong.Count());
        VersionText = found.VersionText;
        Problem = found.Problem;
        CanBuild = found.CanBuild;
        NeedsDownload = Mod.IsPlugin
            ? found.Plugin is null
            : !found.CanBuild || found.Files.Any(f => f.Apart && !f.Present);
        LibraryRead = true;
        OnPropertyChanged(nameof(HasProblem));
        OnPropertyChanged(nameof(EnableTip));
        RefreshMeasured();
        // A plugin mod's other half: what the emulator's own folder holds, which the library cannot say.
        await RefreshPluginAsync();
    }

    /// <summary>
    /// What the ROM being read actually holds. Apart from <see cref="RefreshAsync"/> because a session reopen moves
    /// this and not the library, and because this measurement is what tells a mod that is working from one that is
    /// merely switched on.
    /// </summary>
    public void RefreshMeasured()
    {
        var session = editor.Session;
        var data = session.Current;
        IsExpanded = data.IsExpanded;
        MeasuredText = data.IsExpanded
            ? string.Format(Strings.Mods_MeasuredExtra, data.ExtraSpecies, data.VanillaSpeciesCount, data.SpeciesCount)
            : string.Format(Strings.Mods_MeasuredNone, data.VanillaSpeciesCount);

        // The two side effects, measured instead of warned about in the abstract: both the shiny branch and the shop
        // table are found by a byte signature, and a mod that replaces code.bin can move or remove either, which turns
        // that option off on its own.
        ShinyFound = ShinyBranchFound(session);
        ShopsFound = ShopTableFound(session);
        ShinyEffectText = ShinyFound ? Strings.Mods_SideEffectShinyOk : Strings.Mods_SideEffectShinyGone;
        ShopsEffectText = ShopsFound ? Strings.Mods_SideEffectShopsOk : Strings.Mods_SideEffectShopsGone;

        OnPropertyChanged(nameof(ShinyGone));
        OnPropertyChanged(nameof(ShopsGone));
        OnPropertyChanged(nameof(IsEnabled));
        OnPropertyChanged(nameof(ShowMeasured));
        NotifyStatus();
    }

    /// <summary>The shiny patch is a branch found by the bytes around it: the same look-up its own option does.</summary>
    private static bool ShinyBranchFound(EditorSession session)
    {
        try
        {
            string code = session.Project.CodePath(session.Mods);
            return File.Exists(code) && GameCode.HasShinyBranch(File.ReadAllBytes(code));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// The shop table is found by the item ids the game ships in it (Generation 6, in the executable) or at a fixed
    /// offset of a romfs module (Generation 7, <c>Shop.cro</c>) — looked for here the way the build looks for it.
    /// </summary>
    private static bool ShopTableFound(EditorSession session)
    {
        try
        {
            if (GameShops.For(session.Current.Title) is not { } layout)
                return false;
            string? path = layout.File is { } file
                ? session.Layers.TryResolve(file)
                : session.Project.CodePath(session.Mods);
            if (path is null || !File.Exists(path))
                return false;
            return GameShops.Find(File.ReadAllBytes(path), layout, session.Current.Items.Length) is not null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // ------------------------------------------------------------------ the actions

    /// <summary>
    /// Opens the mod's own page in the system browser: the fallback behind every automatic source, and the only way
    /// for a host whose protocol this application does not speak (Mega needs its own cryptography). When a download
    /// fails this is where the message sends the user.
    /// </summary>
    [RelayCommand]
    private void OpenSource() => Open(Mod.SourceUrl);

    /// <summary>Opens this mod's folder of the library, so the files can be dropped in by hand.</summary>
    [RelayCommand]
    private void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(Folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            editor.SetStatus(string.Format(Strings.Mods_OpenFailed, Folder, ex.Message), error: true);
            return;
        }
        Open(Folder);
    }

    public string OpenFolderTip => string.Format(Strings.Mods_OpenFolderTip, Folder);

    private void Open(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            editor.SetStatus(string.Format(Strings.Mods_OpenFailed, target, ex.Message), error: true);
        }
    }

    /// <summary>
    /// Copies a release the user has already unzipped into the library, keeping the paths inside it. Only
    /// <c>romfs/</c> and <c>exefs/</c> are taken, so a readme and a licence stay where they are. Nothing is fetched:
    /// the folder is already on the user's own disk.
    /// </summary>
    [RelayCommand]
    private async Task ImportRelease()
    {
        if (await editor.Dialogs.PickFolderAsync(Strings.Mods_ImportTitle) is not { } folder)
            return;
        var library = editor.Session.Mods;
        try
        {
            var copied = await Task.Run(() => library.ImportFrom(Mod, folder));
            editor.SetStatus(copied.Count == 0
                ? string.Format(Strings.Mods_ImportedNothing, folder)
                : string.Format(Strings.Mods_Imported, copied.Count, Folder));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            editor.SetStatus(string.Format(Strings.Mods_ImportFailed, ex.Message), error: true);
            return;
        }
        await RefreshAsync();
    }

    /// <summary>The file distributed apart from the rest: the user picks it and it is put at its served path.</summary>
    public bool HasApartFile => Mod.Files.Any(f => f.Apart);

    public string ApartPath => Mod.Files.FirstOrDefault(f => f.Apart)?.ServedPath ?? "";

    public string ImportApartText => string.Format(Strings.Mods_ImportFile, ApartPath);

    public string ImportApartTip => string.Format(Strings.Mods_ImportFileTip, ApartPath);

    [RelayCommand]
    private async Task ImportApartFile()
    {
        if (Mod.Files.FirstOrDefault(f => f.Apart) is not { } apart)
            return;
        if (await editor.Dialogs.PickOpenFileAsync(string.Format(Strings.Mods_ImportFileTitle, apart.ServedPath), ["*"]) is not { } file)
            return;
        var library = editor.Session.Mods;
        try
        {
            await Task.Run(() => library.ImportFile(Mod, file, apart.ServedPath));
            editor.SetStatus(string.Format(Strings.Mods_ImportedFile, Path.GetFileName(file), apart.ServedPath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            editor.SetStatus(string.Format(Strings.Mods_ImportFailed, ex.Message), error: true);
            return;
        }
        await RefreshAsync();
    }

    /// <summary>Reads the folder and hashes it again, for files the user has just put there by hand.</summary>
    [RelayCommand]
    private Task Recheck() => RefreshAsync();

    // ------------------------------------------------------------------ fetching the files from the author's source

    private CancellationTokenSource? downloading;

    /// <summary>Whether this version knows an automatic source for any of this mod's files.</summary>
    public bool CanDownload => Mod.CanDownload;

    public string DownloadTip => CanDownload
        ? Strings.Mods_DownloadTip
        : string.Format(Strings.Mods_DownloadBlockedTip, Mod.Name);

    /// <summary>A download is running: the button becomes the progress line and a Stop.</summary>
    [ObservableProperty]
    public partial bool IsDownloading { get; private set; }

    /// <summary>Which file is being fetched and how far it has got.</summary>
    [ObservableProperty]
    public partial string DownloadText { get; private set; } = "";

    /// <summary>How much of the current file has arrived, 0 to 100, or 0 while the total is unknown.</summary>
    [ObservableProperty]
    public partial double DownloadPercent { get; private set; }

    /// <summary>
    /// The host declared no length — Google Drive does not, for a large file — so the bar runs rather than lying
    /// about a percentage, and the bytes so far are shown instead.
    /// </summary>
    [ObservableProperty]
    public partial bool DownloadUnknownTotal { get; private set; }

    /// <summary>
    /// The hashes of what was downloaded, for the files this version has no hash for. Shown so the catalogue can be
    /// filled in from a real measurement instead of a guess.
    /// </summary>
    [ObservableProperty]
    public partial string? MeasuredHashText { get; private set; }

    public bool HasMeasuredHashes => MeasuredHashText is not null;

    /// <summary>
    /// Fetches whatever the library is missing, straight from the author's own release, off the UI thread. Progress and
    /// cancellation are real: these are hundreds of megabytes, and 2.3 GB for the model archive.
    /// </summary>
    /// <remarks>
    /// When a source fails, the message says <b>what happened and what to do</b> — the page to open and the button to
    /// come back with — because the chain degrading to "do it by hand" is the design and not an error to apologise for.
    /// </remarks>
    [RelayCommand]
    private async Task Download()
    {
        if (IsDownloading || !CanDownload)
            return;

        var library = editor.Session.Mods;
        var game = editor.Session.Project.Game;
        using var cancellation = new CancellationTokenSource();
        downloading = cancellation;
        IsDownloading = true;
        DownloadPercent = 0;
        DownloadUnknownTotal = false;
        MeasuredHashText = null;
        OnPropertyChanged(nameof(HasMeasuredHashes));
        DownloadText = string.Format(Strings.Mods_Downloading, Mod.Name, 1, Mod.DownloadsFor(game).Count);

        // Built on the UI thread, so every report is posted back to it: the work itself runs on a worker.
        var progress = new Progress<ModDownloadProgress>(Show);

        ModDownloadReport report;
        try
        {
            var downloader = ModDownloader.For(library);
            report = await Task.Run(
                () => downloader.DownloadAsync(Mod, game, progress, cancellation.Token),
                cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            editor.SetStatus(Strings.Mods_DownloadStopped);
            await Finish();
            return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            editor.SetStatus(string.Format(Strings.Mods_ImportFailed, ex.Message), error: true);
            await Finish();
            return;
        }

        Report(report);
        await Finish();

        async Task Finish()
        {
            downloading = null;
            IsDownloading = false;
            DownloadText = "";
            DownloadPercent = 0;
            DownloadUnknownTotal = false;
            // The same reading "Check the files again" does, so the card shows what the library now holds.
            await RefreshAsync();
        }
    }

    /// <summary>Turns a report into one line in the status bar: what happened, and what is left to do by hand.</summary>
    private void Report(ModDownloadReport report)
    {
        // Measured hashes first, so they survive whatever the status line ends up saying: they are the only record of
        // what a file the catalogue cannot confirm actually was.
        var measured = report.Outcomes
            .Where(o => o.Status == ModDownloadStatus.Done && !o.Download.HashKnown && o.Sha256 is not null)
            .Select(o => string.Format(Strings.Mods_DownloadMeasuredOne, o.Download.Label, o.Sha256))
            .ToList();
        MeasuredHashText = measured.Count == 0 ? null : string.Format(Strings.Mods_DownloadMeasured, string.Join(" · ", measured));
        OnPropertyChanged(nameof(HasMeasuredHashes));

        if (report.Cancelled)
        {
            editor.SetStatus(Strings.Mods_DownloadStopped);
            return;
        }

        if (report.FirstProblem is { } problem)
        {
            editor.SetStatus(problem.PageUrl is { } page
                ? string.Format(Strings.Mods_DownloadProblem, problem.Message, page, ImportApartText, Strings.Mods_ImportRelease)
                : string.Format(Strings.Mods_DownloadProblemNoPage, problem.Message, Strings.Mods_OpenSource, ImportApartText),
                error: true);
            return;
        }

        editor.SetStatus(report.NothingToDo
            ? string.Format(Strings.Mods_DownloadNothingToDo, Mod.Name)
            : string.Format(Strings.Mods_DownloadFinished, report.FilesWritten));
    }

    /// <summary>One progress report, already back on the UI thread.</summary>
    private void Show(ModDownloadProgress p)
    {
        if (p.Stage == ModDownloadStage.Unpacking)
        {
            DownloadUnknownTotal = false;
            DownloadPercent = p.Total is > 0 ? 100.0 * p.Received / p.Total.Value : 0;
            DownloadText = string.Format(Strings.Mods_Unpacking, p.Label, p.Received, p.Total ?? p.Received);
            return;
        }

        DownloadUnknownTotal = p.Fraction is null;
        DownloadPercent = p.Fraction is { } fraction ? 100 * fraction : 0;
        string how = p.Total is { } total
            ? string.Format(Strings.Mods_DownloadingOf, Megabytes(p.Received), Megabytes(total))
            : string.Format(Strings.Mods_DownloadingSoFar, Megabytes(p.Received));
        DownloadText = string.Format(Strings.Mods_Downloading, $"{p.Label} — {how}", p.Index, p.Count);
    }

    private static string Megabytes(long bytes) =>
        string.Format(Strings.Mods_Megabytes, (bytes / 1024d / 1024d).ToString("N1"));

    // ------------------------------------------------------------------ installing it in the emulator (plugin mods)

    /// <summary>
    /// Where the emulator looks for this game's plugin: <c>sdmc/luma/plugins/&lt;TitleID&gt;</c> of the user folder the
    /// settings point at, or null when no emulator was found. <b>This is the only place that path is worked out</b> —
    /// the model takes it as a parameter, because finding an emulator belongs to <c>Pokemanager.Bridge</c> and the
    /// model does not reference it (the same arrangement <c>ModInstaller</c> already has for the LayeredFS folder).
    /// </summary>
    public string? PluginFolder => editor.Settings.EffectiveEmulatorDirectory is { } directory
        ? EmulatorUserFolders.PluginDirectory(directory, editor.Session.Project.Game.TitleIdHex())
        : null;

    public bool HasPluginFolder => PluginFolder is not null;

    public string PluginFolderText => string.Format(Strings.Mods_PluginFolderIs, PluginFolder ?? "");

    /// <summary>
    /// What that folder holds, measured and not remembered: installed with its size and hash, ready to install, or
    /// nothing to install yet because the library has not got the file.
    /// </summary>
    [ObservableProperty]
    public partial string PluginStateText { get; private set; }

    /// <summary>The plugin file really is in the emulator's folder.</summary>
    [ObservableProperty]
    public partial bool PluginInstalled { get; private set; }

    /// <summary>
    /// The SHA-256 of the installed file. Shown because this version has no hash to check it against, so what is in
    /// the folder can at least be written down from a measurement instead of assumed.
    /// </summary>
    [ObservableProperty]
    public partial string? PluginHashText { get; private set; }

    public bool HasPluginHash => PluginHashText is not null;

    /// <summary>
    /// Another <c>.3gx</c> in the same folder, named. The emulator loads one plugin per title, so this is the
    /// difference between "installed" and "installed and actually loading", and it is measured in the folder rather
    /// than warned about in the abstract.
    /// </summary>
    [ObservableProperty]
    public partial string? PluginOtherText { get; private set; }

    public bool HasPluginOther => PluginOtherText is not null;

    /// <summary>The library holds the plugin and there is an emulator folder to put it in.</summary>
    [ObservableProperty]
    public partial bool CanInstallPlugin { get; private set; }

    public string PluginInstallTip => CanInstallPlugin
        ? string.Format(Strings.Mods_PluginInstallTip, PluginName)
        : string.Format(Strings.Mods_PluginInstallBlockedTip, PluginStateText);

    public string PluginUninstallTip => string.Format(Strings.Mods_PluginUninstallTip, PluginName);

    public string PluginOpenFolderTip => string.Format(Strings.Mods_PluginOpenFolderTip, PluginFolder ?? "");

    /// <summary>The plugin's own file name, as the author ships it.</summary>
    public string PluginName => Mod.PluginFile?.Path ?? "";

    /// <summary>
    /// Reads the emulator's plugin folder and says what is in it. Off the UI thread, because it hashes the file.
    /// </summary>
    public async Task RefreshPluginAsync()
    {
        if (!IsPlugin)
            return;

        if (PluginFolder is not { } folder)
        {
            PluginInstalled = false;
            CanInstallPlugin = false;
            PluginHashText = null;
            PluginOtherText = null;
            PluginStateText = Strings.Mods_PluginNoEmulator;
            Notify();
            return;
        }

        PluginInstallation? found = null;
        bool inLibrary = false;
        try
        {
            var library = editor.Session.Mods;
            // Both readings in one hop off the UI thread: the emulator's folder, and whether the library has the file
            // to put there at all — the second is what tells "not installed" from "nothing to install yet".
            found = await Task.Run(() => ModPluginInstaller.Inspect(folder, Mod));
            inLibrary = await Task.Run(() => library.Inspect(Mod).CanInstallPlugin);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            PluginStateText = string.Format(Strings.Mods_ReadFailed, ex.Message);
            Notify();
            return;
        }

        if (found is not { } state)
            return;

        PluginInstalled = state.Installed;
        PluginHashText = state.Sha256 is { } hash ? string.Format(Strings.Mods_PluginHash, hash) : null;
        PluginOtherText = state.HasConflict
            ? string.Format(Strings.Mods_PluginOtherFound, string.Join(", ", state.OtherPlugins))
            : null;
        CanInstallPlugin = !state.Installed && inLibrary;
        PluginStateText = state.Installed
            ? string.Format(Strings.Mods_PluginInstalled, editor.Settings.EffectiveEmulatorName, state.Path,
                string.Format(Strings.Mods_Bytes, state.Bytes.ToString("N0")))
            : inLibrary
                ? string.Format(Strings.Mods_PluginReady, state.FileName)
                : string.Format(Strings.Mods_PluginNoFile, state.FileName);
        Notify();

        void Notify()
        {
            NotifyStatus();
            OnPropertyChanged(nameof(HasPluginHash));
            OnPropertyChanged(nameof(HasPluginOther));
            OnPropertyChanged(nameof(PluginInstallTip));
        }
    }

    /// <summary>
    /// Copies the plugin into the emulator's folder. <b>It builds nothing and marks nothing dirty</b>: the ROM is not
    /// touched, so there is no version of the project to tell apart and no reason to ask for a build.
    /// </summary>
    /// <remarks>
    /// It refuses while the emulator is open, like every other write that touches the emulator's own files
    /// (<c>SaveEditorViewModel.Write</c>, <c>GameTweaksViewModel.ResetGame</c>, the level-cap pass): the refusal
    /// itself lives in <see cref="ModPluginInstaller"/> so install and remove cannot drift apart, and what the
    /// application contributes is looking at the processes.
    /// </remarks>
    [RelayCommand]
    private Task InstallPlugin() => WritePlugin(install: true);

    /// <summary>Takes the plugin back out of the emulator's folder, and leaves anything else there alone.</summary>
    [RelayCommand]
    private Task UninstallPlugin() => WritePlugin(install: false);

    private async Task WritePlugin(bool install)
    {
        if (PluginFolder is not { } folder)
        {
            editor.SetStatus(Strings.Mods_PluginNoEmulator, error: true);
            return;
        }

        var library = editor.Session.Mods;
        string emulator = editor.Settings.EffectiveEmulatorName;
        try
        {
            var running = await Task.Run(() => EmulatorUserFolders.RunningEmulators(emulator));
            var result = await Task.Run(() => install
                ? ModPluginInstaller.Install(folder, Mod, library, running)
                : ModPluginInstaller.Uninstall(folder, Mod, running));
            editor.SetStatus(install
                ? string.Format(Strings.Mods_PluginInstalledStatus, Mod.Name, result.Path)
                : string.Format(Strings.Mods_PluginRemovedStatus, Mod.Name, result.Directory));
        }
        catch (InvalidOperationException ex)
        {
            // The emulator is open, or the library has not got the file: the sentence already says which.
            editor.SetStatus(ex.Message, error: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            editor.SetStatus(string.Format(Strings.Mods_PluginFailed, ex.Message), error: true);
        }

        await RefreshPluginAsync();
    }

    /// <summary>Opens the emulator's plugin folder, so the user can see what is in it.</summary>
    [RelayCommand]
    private void OpenPluginFolder()
    {
        if (PluginFolder is not { } folder)
        {
            editor.SetStatus(Strings.Mods_PluginNoEmulator, error: true);
            return;
        }
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            editor.SetStatus(string.Format(Strings.Mods_OpenFailed, folder, ex.Message), error: true);
            return;
        }
        Open(folder);
    }

    /// <summary>Stops the download. The part that had arrived is deleted by the downloader itself.</summary>
    [RelayCommand]
    private void StopDownload()
    {
        try
        {
            downloading?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // It finished between the click and here: there is nothing left to stop.
        }
    }
}

/// <summary>One file of a mod as the library has it: where it goes in the game, what state it is in, how big it is.</summary>
public sealed class ModFileRowViewModel
{
    public ModFileRowViewModel(ModFileStatus status, string folder)
    {
        ServedPath = status.ServedPath;
        Present = status.Present;
        Required = status.Required;
        Apart = status.Apart;
        IsWrong = status.State == ModFileState.WrongHash;
        SizeText = status.Bytes > 0 ? string.Format(Strings.Mods_Bytes, status.Bytes.ToString("N0")) : "";

        StateText = status.State switch
        {
            ModFileState.Verified => Strings.Mods_StateVerified,
            ModFileState.WrongHash => Strings.Mods_StateWrongHash,
            ModFileState.HashUnknown => Strings.Mods_StateHashUnknown,
            _ => Strings.Mods_StateMissing,
        };

        // The tags say what a row means on its own: a missing required file stops the build, a missing one that comes
        // apart is a mod the user has not finished, a file the catalogue does not name is packed all the same, and one
        // the packer cannot write is kept and checked but never reaches the ROM.
        var tags = new List<string>();
        if (Required)
            tags.Add(Strings.Mods_FileRequired);
        if (Apart)
            tags.Add(Strings.Mods_FileApart);
        if (status.Declared is null)
            tags.Add(Strings.Mods_FileExtra);
        if (!status.IntoRom)
            tags.Add(Strings.Mods_FileNotInRom);
        TagsText = string.Join(" · ", tags);

        string where = Path.Combine(folder, status.ServedPath.Replace('/', Path.DirectorySeparatorChar));
        Tip = status.State switch
        {
            ModFileState.Verified => string.Format(Strings.Mods_FileTipVerified, ServedPath, SizeText),
            ModFileState.WrongHash => string.Format(Strings.Mods_FileTipWrongHash, ServedPath, SizeText),
            ModFileState.HashUnknown => string.Format(Strings.Mods_FileTipHashUnknown, ServedPath, SizeText),
            _ => string.Format(Strings.Mods_FileTipMissing, ServedPath, where),
        };
    }

    public string ServedPath { get; }
    public bool Present { get; }
    public bool Required { get; }
    public bool Apart { get; }
    public bool IsWrong { get; }
    public string StateText { get; }
    public string SizeText { get; }
    public string TagsText { get; }
    public bool HasTags => TagsText.Length > 0;
    public string Tip { get; }

    /// <summary>A required file that is not there, or a file that is not what it claims to be: the two red states.</summary>
    public bool IsBlocking => (Required && !Present) || IsWrong;
}
