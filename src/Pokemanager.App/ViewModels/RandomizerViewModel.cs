using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pokemanager.App.Resources;
using Pokemanager.App.Services;
using Pokemanager.Bridge;
using Pokemanager.Model.Dump;
using Pokemanager.Model.Projects;
using Pokemanager.Randomizer;
using Pokemanager.Save;

namespace Pokemanager.App.ViewModels;

/// <summary>A "changes to your save" line, already with names.</summary>
public sealed record SaveChangeLine(string Location, string Pokemon, string Description);

/// <summary>Randomizer tab: options, seed, output ROM, save and log.</summary>
public partial class RandomizerViewModel : ObservableObject
{
    private const int MaxLogLines = 3000;

    private readonly EditorViewModel editor;
    private readonly IDialogs dialogs;
    private readonly UprService upr;
    private readonly AppSettings settings;
    private string[] logLines = [];
    private bool optionsLoading;

    private Project Project => editor.Session.Project;
    private RandomizationSettings Settings => Project.Randomization;

    /// <summary>First entry of the section list, meaning "no section filter".</summary>
    private static string AllSections => Strings.Log_All;

    public UprOptionsViewModel Options { get; }

    /// <summary>Pokémon (.pkdata) and move (.mvdata) data files, the counterpart of the .rnqs for base stats, types and the rest.</summary>
    public IAsyncRelayCommand ImportPokemonDataCommand => editor.ImportPokemonDataCommand;
    public IAsyncRelayCommand ExportPokemonDataCommand => editor.ExportPokemonDataCommand;
    public IAsyncRelayCommand ExportMoveDataCommand => editor.ExportMoveDataCommand;
    public IAsyncRelayCommand PlayCommand => editor.PlayCommand;

    public ObservableCollection<string> VisibleLog { get; } = [];
    public ObservableCollection<string> LogSections { get; } = [];
    public ObservableCollection<SaveChangeLine> SaveChanges { get; } = [];

    [ObservableProperty]
    public partial string SeedText { get; set; } = "";

    [ObservableProperty]
    public partial string? SeedError { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OutputPath), nameof(OutputError), nameof(OutputExists))]
    public partial string OutputName { get; set; } = "";

    [ObservableProperty]
    public partial string LogFilter { get; set; } = "";

    [ObservableProperty]
    public partial string? SelectedLogSection { get; set; }

    [ObservableProperty]
    public partial string LogSummary { get; set; } = Strings.Log_Empty;

    [ObservableProperty]
    public partial string SaveChangesSummary { get; set; } = "";

    /// <summary>Changes to the game's shops; ours, not UPR ZX's, and kept in the project.</summary>
    public ShopExtrasViewModel Shops { get; private set; } = null!;

    public RandomizerViewModel(EditorViewModel editor, IDialogs dialogs, UprService upr, AppSettings settings)
    {
        this.editor = editor;
        this.dialogs = dialogs;
        this.upr = upr;
        this.settings = settings;
        Shops = new ShopExtrasViewModel(editor);
        Options = new UprOptionsViewModel(editor.MarkDirty, Shops);

        SeedText = Settings.Seed > 0 ? Settings.Seed.ToString() : UprRunner.NewSeed().ToString();
        OutputName = Settings.OutputName ?? DefaultOutputName();

        // Projects from before LastBuiltRom: if the ROM with its name exists, it is the last one built.
        if (Settings.LastBuiltRom is null && BaseRom is { } baseRom && RomBuilder.ValidateName(OutputName) is null
            && RomBuilder.OutputPath(baseRom, OutputName) is var previous && File.Exists(previous))
            Settings.LastBuiltRom = previous;
        if (upr.TryGetCached(Project, Settings.Preset) is { } cached)
            LoadLog(cached.LogPath);
    }

    // ------------------------------------------------------------------ state

    public bool Enabled
    {
        get => Settings.Enabled;
        set
        {
            if (Settings.Enabled == value)
                return;
            Settings.Enabled = value;
            editor.MarkDirty();
            OnPropertyChanged();
            RefreshSave();
        }
    }

    public bool UpdateSave
    {
        get => Settings.UpdateSave;
        set
        {
            if (Settings.UpdateSave == value)
                return;
            Settings.UpdateSave = value;
            editor.MarkDirty();
            OnPropertyChanged();
            RefreshSave();
        }
    }

    public string ToolsStatus => upr.StatusText;
    public bool ToolsReady => upr.Tools is not null;

    public string PresetText
    {
        get
        {
            string name = Settings.PresetName ?? Strings.Rnd_PresetDefaults;
            if (Settings.PresetModified)
                name = string.Format(Strings.Rnd_PresetModified, name);
            return string.Format(Strings.Rnd_Preset, name);
        }
    }

    public string? BaseRom => Project.ResolveRomFile();
    public string BaseRomText => BaseRom ?? Strings.Rnd_BaseRomMissing;

    /// <summary>There is a ROM built earlier by this project that still exists.</summary>
    public bool HasPreviousRom => Settings.LastBuiltRom is { } p && File.Exists(p);

    public string PreviousRomText => HasPreviousRom ? string.Format(Strings.Rnd_ReplacePrevious, Path.GetFileName(Settings.LastBuiltRom)) : "";

    public bool ReplacePrevious
    {
        get => Settings.ReplacePreviousRom;
        set
        {
            if (Settings.ReplacePreviousRom == value)
                return;
            Settings.ReplacePreviousRom = value;
            editor.MarkDirty();
            OnPropertyChanged();
            OnPropertyChanged(nameof(CreateNew));
            OnPropertyChanged(nameof(IsNameEditable));
            OnPropertyChanged(nameof(OutputPath));
            OnPropertyChanged(nameof(OutputError));
            OnPropertyChanged(nameof(OutputExists));
        }
    }

    public bool CreateNew
    {
        get => !ReplacePrevious;
        set => ReplacePrevious = !value;
    }

    private bool Replacing => ReplacePrevious && HasPreviousRom;

    public bool IsNameEditable => !Replacing;

    /// <summary>Where the ROM will be written: the previous one (when replacing) or a new one with the given name.</summary>
    public string? OutputPath => Replacing
        ? Settings.LastBuiltRom
        : BaseRom is { } rom && RomBuilder.ValidateName(OutputName) is null ? RomBuilder.OutputPath(rom, OutputName) : null;

    public string? OutputError => Replacing ? null : RomBuilder.ValidateName(OutputName);
    public bool OutputExists => !Replacing && OutputPath is { } p && File.Exists(p);

    public string EmulatorName => settings.EffectiveEmulatorName;

    public string? SavePath => settings.EffectiveEmulatorDirectory is { } dir
        ? EmulatorUserFolders.SaveFile(dir, editor.Dump.Title.TitleId())
        : null;

    /// <summary>Summary of the emulator save (trainer, party, date), or why there is none.</summary>
    public string SaveText
    {
        get
        {
            if (SavePath is not { } path)
                return Strings.Rnd_NoEmulator;
            if (!File.Exists(path))
                return string.Format(Strings.Rnd_NoSave, editor.Dump.Title.DisplayName(), EmulatorName);
            try
            {
                var sav = SaveUpdater.Load(path);
                return string.Format(Strings.Rnd_SaveInfo, EmulatorName, sav.OT, sav.PartyCount, File.GetLastWriteTime(path));
            }
            catch (Exception ex) when (ex is IOException or SaveUpdateException or UnauthorizedAccessException)
            {
                return string.Format(Strings.Rnd_SaveUnreadable, EmulatorName, ex.Message);
            }
        }
    }

    public bool HasSave => SavePath is { } p && File.Exists(p);

    /// <summary>Exact path of the save that will be modified, so it is always visible which one it is.</summary>
    public string SaveTargetText => SavePath is { } p ? string.Format(Strings.Rnd_SaveFile, p) : "";

    public string? SaveWarning => !HasSave || !Settings.Enabled ? null : UpdateSave ? Strings.Rnd_WarningUpdate : Strings.Rnd_WarningNoUpdate;

    public bool HasSaveWarning => SaveWarning is not null;

    private string DefaultOutputName()
    {
        string baseName = BaseRom is { } rom ? Path.GetFileNameWithoutExtension(rom) : "Pokemon";
        return $"{baseName} - random";
    }

    partial void OnSeedTextChanged(string value)
    {
        if (long.TryParse(value.Trim(), out long seed) && seed > 0)
        {
            SeedError = null;
            if (Settings.Seed != seed)
            {
                Settings.Seed = seed;
                editor.MarkDirty();
            }
        }
        else
        {
            SeedError = Strings.Rnd_SeedError;
        }
    }

    partial void OnOutputNameChanged(string value)
    {
        if (Settings.OutputName != value)
        {
            Settings.OutputName = value;
            editor.MarkDirty();
        }
    }

    /// <summary>The project's randomization settings were put back (undo): show them again.</summary>
    public void ReloadFromProject()
    {
        SeedText = Settings.Seed > 0 ? Settings.Seed.ToString() : SeedText;
        OutputName = Settings.OutputName ?? DefaultOutputName();
        OnPropertyChanged(string.Empty);
    }

    public void RefreshSave()
    {
        OnPropertyChanged(nameof(EmulatorName));
        OnPropertyChanged(nameof(SavePath));
        OnPropertyChanged(nameof(SaveText));
        OnPropertyChanged(nameof(SaveTargetText));
        OnPropertyChanged(nameof(HasSave));
        OnPropertyChanged(nameof(SaveWarning));
        OnPropertyChanged(nameof(HasSaveWarning));
        OnPropertyChanged(nameof(HasPreviousRom));
        OnPropertyChanged(nameof(PreviousRomText));
        OnPropertyChanged(nameof(IsNameEditable));
        OnPropertyChanged(nameof(OutputPath));
        OnPropertyChanged(nameof(OutputError));
        OnPropertyChanged(nameof(OutputExists));
    }

    /// <summary>The project's randomization was replaced (a version restored): show the new seed, preset and log.</summary>
    public async Task OnProjectReplacedAsync()
    {
        if (Settings.Seed > 0)
            SeedText = Settings.Seed.ToString();
        OnPropertyChanged(nameof(Enabled));
        OnPropertyChanged(nameof(PresetText));
        bool wasLoaded = Options.IsLoaded;
        Options.Fail(Strings.Rnd_ReloadingOptions);
        if (wasLoaded)
            await LoadOptionsAsync();
        if (upr.TryGetCached(Project, Settings.Preset) is { } cached)
            LoadLog(cached.LogPath);
        RefreshSave();
    }

    // ------------------------------------------------------------------ options

    /// <summary>Loads the preset options into the editor (UPR takes a few seconds the first time).</summary>
    public async Task LoadOptionsAsync()
    {
        if (optionsLoading || Options.IsLoaded)
            return;
        if (upr.Tools is not { } tools)
        {
            Options.Fail(upr.StatusText);
            return;
        }

        optionsLoading = true;
        try
        {
            var runner = new UprRunner(tools);
            var description = await runner.DescribeSettingsAsync(Settings.Preset);
            var available = BaseRom is { } rom ? await upr.AvailableTweaksAsync(rom) : new HashSet<string>();
            Options.Load(description, available, editor.Dump.Title.Generation());
        }
        catch (Exception ex) when (ex is UprException or IOException or System.Text.Json.JsonException)
        {
            Options.Fail(string.Format(Strings.Rnd_OptionsLoadFailed, ex.Message));
        }
        finally
        {
            optionsLoading = false;
        }
    }

    /// <summary>Writes the preset with the edited options to the project (or UPR defaults when there is no preset).</summary>
    public async Task CommitOptionsAsync()
    {
        if (upr.Tools is not { } tools)
            return;
        if (Options.IsLoaded && Options.HasChanges)
        {
            Settings.Preset = await new UprRunner(tools).WriteSettingsAsync(Settings.Preset, Options.Assignments());
            Settings.PresetModified = true;
            Options.MarkWritten();
            OnPropertyChanged(nameof(PresetText));
        }
        else if (Settings.Preset is null)
        {
            Settings.Preset = await new UprRunner(tools).WriteSettingsAsync(null, []);
        }
    }

    [RelayCommand]
    private void NewSeed() => SeedText = UprRunner.NewSeed().ToString();

    [RelayCommand]
    private async Task ImportPreset()
    {
        if (await dialogs.PickOpenFileAsync(Strings.Rnd_ImportTitle, ["*.rnqs"]) is not { } path)
            return;
        try
        {
            Settings.Preset = await File.ReadAllBytesAsync(path);
            Settings.PresetName = Path.GetFileNameWithoutExtension(path);
            Settings.PresetModified = false;
            editor.MarkDirty();
            OnPropertyChanged(nameof(PresetText));
            Options.Fail(Strings.Rnd_ReloadingOptions);
            await LoadOptionsAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            editor.SetStatus(string.Format(Strings.Rnd_PresetReadFailed, ex.Message), error: true);
        }
    }

    [RelayCommand]
    private async Task ExportPreset()
    {
        await CommitOptionsAsync();
        if (Settings.Preset is null)
            return;
        if (await dialogs.PickSaveFileAsync(Strings.Rnd_ExportTitle, (Settings.PresetName ?? "preset") + ".rnqs", "rnqs") is { } path)
        {
            await File.WriteAllBytesAsync(path, Settings.Preset);
            editor.SetStatus(string.Format(Strings.Rnd_Exported, path));
        }
    }

    [RelayCommand]
    private async Task ResetPreset()
    {
        Settings.Preset = null;
        Settings.PresetName = null;
        Settings.PresetModified = false;
        editor.MarkDirty();
        OnPropertyChanged(nameof(PresetText));
        Options.Fail(Strings.Rnd_ReloadingOptions);
        await LoadOptionsAsync();
    }

    [RelayCommand]
    private async Task RandomizeAndBuild()
    {
        if (!Enabled)
            Enabled = true;
        await editor.BuildRomCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private async Task AdaptSaveNow() => await editor.AdaptSaveCommand.ExecuteAsync(null);

    // ------------------------------------------------------------------ results

    /// <summary>Called by the editor after building the ROM or adapting the save.</summary>
    public void OnBuilt(UprResult? random, SaveUpdateResult? save)
    {
        RefreshSave();
        if (random is not null)
            LoadLog(random.LogPath);

        SaveChanges.Clear();
        if (save is null)
        {
            SaveChangesSummary = "";
            OnPropertyChanged(nameof(HasSaveChangeDetails));
            return;
        }
        foreach (var c in save.Changes)
            SaveChanges.Add(new SaveChangeLine(Location(c.Slot), SpeciesName(c.Species), Describe(c)));
        OnPropertyChanged(nameof(HasSaveChangeDetails));
        SaveChangesSummary = save.Changes.Count == 0
            ? string.Format(Strings.Rnd_SaveChecked, save.PokemonChecked)
            : string.Format(Strings.Rnd_SaveUpdatedSummary, save.Changes.Count, save.PokemonChecked, save.BackupPath);
    }

    private static string Location(SaveSlot slot) => slot.Box is { } box
        ? string.Format(Strings.Change_Box, box + 1, slot.Slot + 1)
        : string.Format(Strings.Change_Party, slot.Slot + 1);

    private string SpeciesName(ushort species) =>
        species < editor.Names.Species.Count ? editor.Names.Species[species] : $"#{species}";

    private string Describe(PokemonChange change) => change.Kind switch
    {
        ChangeKind.Ability => string.Format(Strings.Change_Ability, AbilityName(change.Before[0]), AbilityName(change.After[0])),
        _ => string.Format(Strings.Change_Stats, string.Join('/', change.Before), string.Join('/', change.After)),
    };

    private string AbilityName(int id) => id >= 0 && id < editor.Names.Abilities.Count ? editor.Names.Abilities[id] : id.ToString();

    // ------------------------------------------------------------------ log

    /// <summary>There is a log of the current randomization. It is only shown on request: it spoils the whole game.</summary>
    public bool HasLog => logLines.Length > 0;

    public bool HasSaveChangeDetails => SaveChanges.Count > 0;

    [RelayCommand]
    private void ShowLog() => dialogs.ShowLog(this);

    partial void OnLogFilterChanged(string value) => ApplyLogFilter();
    partial void OnSelectedLogSectionChanged(string? value) => ApplyLogFilter();

    private static bool IsSectionHeader(string line) => line.Length > 4 && line.StartsWith("--") && line.EndsWith("--");

    private void LoadLog(string path)
    {
        try
        {
            logLines = File.ReadAllLines(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logLines = [];
        }

        string? keep = SelectedLogSection;
        LogSections.Clear();
        LogSections.Add(AllSections);
        foreach (string header in logLines.Where(IsSectionHeader).Distinct())
            LogSections.Add(header);
        SelectedLogSection = keep is not null && LogSections.Contains(keep) ? keep : AllSections;
        ApplyLogFilter();
        OnPropertyChanged(nameof(HasLog));
    }

    private IEnumerable<string> SectionLines()
    {
        if (SelectedLogSection is null || SelectedLogSection == AllSections)
            return logLines;
        int start = Array.IndexOf(logLines, SelectedLogSection);
        return start < 0 ? logLines : logLines.Skip(start).TakeWhile((line, i) => i == 0 || !IsSectionHeader(line));
    }

    private void ApplyLogFilter()
    {
        string filter = LogFilter.Trim();
        var source = SectionLines().ToArray();
        var matches = filter.Length == 0
            ? source
            : source.Where(l => l.Contains(filter, StringComparison.CurrentCultureIgnoreCase)).ToArray();

        VisibleLog.Clear();
        foreach (string line in matches.Take(MaxLogLines))
            VisibleLog.Add(line);

        LogSummary = logLines.Length == 0
            ? Strings.Log_Empty
            : matches.Length > MaxLogLines
                ? string.Format(Strings.Log_TooMany, matches.Length, MaxLogLines)
                : string.Format(Strings.Log_Count, matches.Length, logLines.Length);
    }
}

