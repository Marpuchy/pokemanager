using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pokemanager.App.Services;
using Pokemanager.Bridge;
using Pokemanager.Model.Dump;
using Pokemanager.Model.Projects;
using Pokemanager.Randomizer;
using Pokemanager.Save;

namespace Pokemanager.App.ViewModels;

/// <summary>Una línea de «cambios en tu partida» ya con nombres.</summary>
public sealed record SaveChangeLine(string Location, string Pokemon, string Description);

/// <summary>Pestaña del randomizer: opciones, semilla, ROM de salida, partida y log.</summary>
public partial class RandomizerViewModel : ObservableObject
{
    private const int MaxLogLines = 3000;
    private const string AllSections = "Todo el log";

    private readonly EditorViewModel editor;
    private readonly IDialogs dialogs;
    private readonly UprService upr;
    private readonly AppSettings settings;
    private string[] logLines = [];
    private bool optionsLoading;

    private Project Project => editor.Session.Project;
    private RandomizationSettings Settings => Project.Randomization;

    public UprOptionsViewModel Options { get; }

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
    public partial string LogSummary { get; set; } = "Aún no hay log: randomiza para generarlo.";

    [ObservableProperty]
    public partial string SaveChangesSummary { get; set; } = "";

    public RandomizerViewModel(EditorViewModel editor, IDialogs dialogs, UprService upr, AppSettings settings)
    {
        this.editor = editor;
        this.dialogs = dialogs;
        this.upr = upr;
        this.settings = settings;
        Options = new UprOptionsViewModel(editor.MarkDirty);

        SeedText = Settings.Seed > 0 ? Settings.Seed.ToString() : UprRunner.NewSeed().ToString();
        OutputName = Settings.OutputName ?? DefaultOutputName();
        if (upr.TryGetCached(Project, Settings.Preset) is { } cached)
            LoadLog(cached.LogPath);
    }

    // ------------------------------------------------------------------ estado

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

    public string PresetText => Settings.PresetName is { } name ? $"Preset: {name}" : "Preset: ajustes por defecto de UPR ZX";

    public string? BaseRom => Project.ResolveRomFile();
    public string BaseRomText => BaseRom ?? "No se encuentra la ROM base (.3ds) en la carpeta del volcado. Cámbiala en Ajustes del proyecto.";

    public string? OutputPath => BaseRom is { } rom && RomBuilder.ValidateName(OutputName) is null ? RomBuilder.OutputPath(rom, OutputName) : null;
    public string? OutputError => RomBuilder.ValidateName(OutputName);
    public bool OutputExists => OutputPath is { } p && File.Exists(p);

    public string EmulatorName => settings.EffectiveEmulatorName;

    public string? SavePath => settings.EffectiveEmulatorDirectory is { } dir
        ? EmulatorUserFolders.SaveFile(dir, editor.Dump.Title.TitleId())
        : null;

    /// <summary>Resumen de la partida del emulador (entrenador, equipo, fecha), o por qué no la hay.</summary>
    public string SaveText
    {
        get
        {
            if (SavePath is not { } path)
                return "No se ha encontrado ningún emulador. Elígelo en Ajustes.";
            if (!File.Exists(path))
                return $"No hay partida de Pokémon {editor.Dump.Title} en {EmulatorName}.";
            try
            {
                var sav = SaveUpdater.Load(path);
                return $"Partida de {EmulatorName}: {sav.OT}, {sav.PartyCount} en el equipo, guardada el {File.GetLastWriteTime(path):g}.";
            }
            catch (Exception ex) when (ex is IOException or SaveUpdateException or UnauthorizedAccessException)
            {
                return $"No se puede leer la partida de {EmulatorName}: {ex.Message}";
            }
        }
    }

    public bool HasSave => SavePath is { } p && File.Exists(p);

    public string? SaveWarning
    {
        get
        {
            if (!HasSave || !Settings.Enabled)
                return null;
            return UpdateSave
                ? "Al crear la ROM se adaptará tu partida: cada Pokémon del equipo y de las cajas recibirá la habilidad que le toca en la nueva ROM "
                  + "y el equipo recalculará sus stats. Especie, nivel, IV, EV, naturaleza y movimientos no cambian. Antes se hace una copia de seguridad."
                : "Tu partida no se tocará: los Pokémon que ya tienes conservarán sus habilidades y stats actuales aunque la nueva ROM tenga otras.";
        }
    }

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
            SeedError = "La semilla debe ser un número entero positivo.";
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

    public void RefreshSave()
    {
        OnPropertyChanged(nameof(EmulatorName));
        OnPropertyChanged(nameof(SavePath));
        OnPropertyChanged(nameof(SaveText));
        OnPropertyChanged(nameof(HasSave));
        OnPropertyChanged(nameof(SaveWarning));
        OnPropertyChanged(nameof(HasSaveWarning));
        OnPropertyChanged(nameof(OutputExists));
    }

    // ------------------------------------------------------------------ opciones

    /// <summary>Carga las opciones del preset en el editor (UPR tarda unos segundos la primera vez).</summary>
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
            Options.Load(description, available);
        }
        catch (Exception ex) when (ex is UprException or IOException or System.Text.Json.JsonException)
        {
            Options.Fail($"No se pudieron leer las opciones del randomizer: {ex.Message}");
        }
        finally
        {
            optionsLoading = false;
        }
    }

    /// <summary>Escribe al proyecto el preset con las opciones editadas (o los ajustes por defecto si no hay preset).</summary>
    public async Task CommitOptionsAsync()
    {
        if (upr.Tools is not { } tools)
            return;
        if (Options.IsLoaded && Options.HasChanges)
        {
            Settings.Preset = await new UprRunner(tools).WriteSettingsAsync(Settings.Preset, Options.Assignments());
            Settings.PresetName = Settings.PresetName is { } n && !n.EndsWith(" (modificado)") ? n + " (modificado)" : Settings.PresetName ?? "Personalizado";
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
        if (await dialogs.PickOpenFileAsync("Preset de UPR ZX (.rnqs)", ["*.rnqs"]) is not { } path)
            return;
        try
        {
            Settings.Preset = await File.ReadAllBytesAsync(path);
            Settings.PresetName = Path.GetFileNameWithoutExtension(path);
            editor.MarkDirty();
            OnPropertyChanged(nameof(PresetText));
            Options.Fail("Recargando opciones…");
            await LoadOptionsAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            editor.SetStatus($"No se pudo leer el preset: {ex.Message}", error: true);
        }
    }

    [RelayCommand]
    private async Task ExportPreset()
    {
        await CommitOptionsAsync();
        if (Settings.Preset is null)
            return;
        if (await dialogs.PickSaveFileAsync("Exportar preset", (Settings.PresetName ?? "preset") + ".rnqs", "rnqs") is { } path)
        {
            await File.WriteAllBytesAsync(path, Settings.Preset);
            editor.SetStatus($"Preset exportado a {path}. Se puede abrir en UPR ZX.");
        }
    }

    [RelayCommand]
    private async Task ResetPreset()
    {
        Settings.Preset = null;
        Settings.PresetName = null;
        editor.MarkDirty();
        OnPropertyChanged(nameof(PresetText));
        Options.Fail("Recargando opciones…");
        await LoadOptionsAsync();
    }

    [RelayCommand]
    private async Task RandomizeAndBuild()
    {
        if (!Enabled)
            Enabled = true;
        await editor.BuildRomCommand.ExecuteAsync(null);
    }

    // ------------------------------------------------------------------ resultados

    /// <summary>Llamado por el editor tras crear la ROM.</summary>
    public void OnBuilt(UprResult? random, SaveUpdateResult? save)
    {
        RefreshSave();
        if (random is not null)
            LoadLog(random.LogPath);

        SaveChanges.Clear();
        if (save is null)
        {
            SaveChangesSummary = "";
            return;
        }
        foreach (var c in save.Changes)
            SaveChanges.Add(new SaveChangeLine(c.Location, SpeciesName(c.Species), Describe(c.Description)));
        SaveChangesSummary = save.Changes.Count == 0
            ? $"Partida revisada ({save.PokemonChecked} Pokémon): no hacía falta cambiar nada."
            : $"Partida actualizada: {save.Changes.Count} cambio(s) en {save.PokemonChecked} Pokémon. Copia de seguridad: {save.BackupPath}";
    }

    private string SpeciesName(ushort species) =>
        species < editor.Names.Species.Count ? editor.Names.Species[species] : $"#{species}";

    /// <summary>«habilidad 26 → 51» → «habilidad Levitación → Vista Lince».</summary>
    private string Describe(string description)
    {
        const string prefix = "habilidad ";
        if (!description.StartsWith(prefix))
            return description;
        var parts = description[prefix.Length..].Split(" → ");
        string Name(string id) => int.TryParse(id, out int n) && n < editor.Names.Abilities.Count ? editor.Names.Abilities[n] : id;
        return parts.Length == 2 ? $"habilidad {Name(parts[0])} → {Name(parts[1])}" : description;
    }

    // ------------------------------------------------------------------ log

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
    }

    private IEnumerable<string> SectionLines()
    {
        if (SelectedLogSection is null or AllSections)
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
            ? "Aún no hay log: randomiza para generarlo."
            : matches.Length > MaxLogLines
                ? $"{matches.Length} líneas coinciden; se muestran las primeras {MaxLogLines}. Afina la búsqueda."
                : $"{matches.Length} de {logLines.Length} líneas.";
    }
}
