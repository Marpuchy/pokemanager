using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pokemanager.App.Services;
using Pokemanager.Bridge;
using Pokemanager.Model.Dump;
using Pokemanager.Model.Projects;
using Pokemanager.Randomizer;

namespace Pokemanager.App.ViewModels;

/// <summary>Pestaña del randomizer: UPR ZX, preset, semilla, log y avisos sobre la partida.</summary>
public partial class RandomizerViewModel : ObservableObject
{
    private const int MaxLogLines = 3000;

    private readonly EditorViewModel editor;
    private readonly IDialogs dialogs;
    private readonly UprService upr;
    private string[] logLines = [];

    private Project Project => editor.Session.Project;
    private RandomizationSettings Settings => Project.Randomization;

    public ObservableCollection<string> PresetsNextToJar { get; } = [];
    public ObservableCollection<string> VisibleLog { get; } = [];

    /// <summary>Secciones del log de UPR (líneas <c>--Título--</c>), con «Todo el log» al principio.</summary>
    public ObservableCollection<string> LogSections { get; } = [];

    private const string AllSections = "Todo el log";

    [ObservableProperty]
    public partial string? SelectedLogSection { get; set; }

    [ObservableProperty]
    public partial string ToolsStatus { get; set; } = "";

    [ObservableProperty]
    public partial bool ToolsReady { get; set; }

    [ObservableProperty]
    public partial string SeedText { get; set; } = "";

    [ObservableProperty]
    public partial string? SeedError { get; set; }

    [ObservableProperty]
    public partial string LogFilter { get; set; } = "";

    [ObservableProperty]
    public partial string LogSummary { get; set; } = "Aún no hay log: randomiza para generarlo.";

    [ObservableProperty]
    public partial string? SelectedPresetNextToJar { get; set; }

    public RandomizerViewModel(EditorViewModel editor, IDialogs dialogs, UprService upr)
    {
        this.editor = editor;
        this.dialogs = dialogs;
        this.upr = upr;
        SeedText = Settings.Seed > 0 ? Settings.Seed.ToString() : "";
        RefreshTools();
        if (upr.TryGetCached(Project) is { } cached)
            LoadLog(cached.LogPath);
    }

    public bool Enabled
    {
        get => Settings.Enabled;
        set
        {
            if (Settings.Enabled == value)
                return;
            Settings.Enabled = value;
            if (value && Settings.Seed <= 0)
                NewSeed();
            editor.MarkDirty();
            OnPropertyChanged();
            OnPropertyChanged(nameof(SaveWarning));
            OnPropertyChanged(nameof(HasSaveWarning));
        }
    }

    public string PresetText => Settings.PresetName is { } name ? name : "Ningún preset importado";
    public bool HasPreset => Settings.Preset is { Length: > 0 };
    public string? RomText => Project.ResolveRomFile() is { } rom ? rom : null;
    public bool HasRom => RomText is not null;

    /// <summary>Aviso si hay partida guardada y la semilla actual no es la instalada.</summary>
    public string? SaveWarning
    {
        get
        {
            if (Project.EmulatorUserDirectory is not { } user)
                return null;
            string save = EmulatorUserFolders.SaveFile(user, editor.Dump.Title.TitleId());
            if (!File.Exists(save))
                return null;

            string when = File.GetLastWriteTime(save).ToString("g");
            bool changing = Settings.Enabled && Settings.InstalledSeed is { } installed && installed != Settings.Seed;
            bool first = Settings.Enabled && Settings.InstalledSeed is null;
            if (!changing && !first)
                return null;

            return $"Tienes una partida guardada ({when}). Con otra semilla, tus Pokémon capturados conservan especie, nivel y movimientos aprendidos, "
                   + "pero sus stats base, tipos, habilidades y lo que aprendan a partir de ahora cambian. Los entrenadores y salvajes que aún no has visto también cambian.";
        }
    }

    public bool HasSaveWarning => SaveWarning is not null;

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
            SeedError = "La semilla debe ser un número entero positivo.";
        }
        OnPropertyChanged(nameof(SaveWarning));
        OnPropertyChanged(nameof(HasSaveWarning));
    }

    partial void OnLogFilterChanged(string value) => ApplyLogFilter();

    partial void OnSelectedLogSectionChanged(string? value) => ApplyLogFilter();

    private static bool IsSectionHeader(string line) => line.Length > 4 && line.StartsWith("--") && line.EndsWith("--");

    partial void OnSelectedPresetNextToJarChanged(string? value)
    {
        if (value is not null)
            ImportPreset(value);
    }

    [RelayCommand]
    private void NewSeed() => SeedText = UprRunner.NewSeed().ToString();

    [RelayCommand]
    private async Task ImportPresetFile()
    {
        if (await dialogs.PickOpenFileAsync("Preset de UPR ZX (.rnqs)", ["*.rnqs"]) is { } path)
            ImportPreset(path);
    }

    private void ImportPreset(string path)
    {
        try
        {
            Settings.Preset = File.ReadAllBytes(path);
            Settings.PresetName = Path.GetFileNameWithoutExtension(path);
            editor.MarkDirty();
            OnPropertyChanged(nameof(PresetText));
            OnPropertyChanged(nameof(HasPreset));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            editor.SetStatus($"No se pudo leer el preset: {ex.Message}", error: true);
        }
    }

    [RelayCommand]
    private async Task BrowseJar()
    {
        if (await dialogs.PickOpenFileAsync("PokeRandoZX.jar", ["*.jar"]) is { } path)
        {
            upr.SetJar(path);
            RefreshTools();
        }
    }

    [RelayCommand]
    private async Task BrowseRom()
    {
        if (await dialogs.PickOpenFileAsync("ROM descifrada de Pokémon X/Y", ["*.3ds", "*.cci", "*.cxi"]) is { } path)
        {
            Project.RomFile = path;
            editor.MarkDirty();
            OnPropertyChanged(nameof(RomText));
            OnPropertyChanged(nameof(HasRom));
        }
    }

    [RelayCommand]
    private async Task RandomizeAndInstall()
    {
        if (!Enabled)
            Enabled = true;
        await editor.InstallCommand.ExecuteAsync(null);
    }

    public void RefreshTools()
    {
        ToolsStatus = upr.StatusText;
        ToolsReady = upr.Tools is not null;
        PresetsNextToJar.Clear();
        foreach (string p in upr.PresetsNextToJar())
            PresetsNextToJar.Add(p);
    }

    /// <summary>Llamado por el editor tras instalar.</summary>
    public void OnInstalled(UprResult? result)
    {
        OnPropertyChanged(nameof(SaveWarning));
        OnPropertyChanged(nameof(HasSaveWarning));
        if (result is not null)
            LoadLog(result.LogPath);
    }

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
    }

    /// <summary>Líneas de la sección elegida (desde su cabecera hasta la siguiente), o todo el log.</summary>
    private IEnumerable<string> SectionLines()
    {
        if (SelectedLogSection is null or AllSections)
            return logLines;
        int start = Array.IndexOf(logLines, SelectedLogSection);
        if (start < 0)
            return logLines;
        return logLines.Skip(start).TakeWhile((line, i) => i == 0 || !IsSectionHeader(line));
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
            ? "Aún no hay log: randomiza para generarlo."
            : matches.Length > MaxLogLines
                ? $"{matches.Length} líneas coinciden; se muestran las primeras {MaxLogLines}. Afina la búsqueda."
                : $"{matches.Length} de {logLines.Length} líneas.";
    }
}
