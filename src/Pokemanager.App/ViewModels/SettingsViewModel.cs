using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pokemanager.App.Services;
using Pokemanager.Bridge;
using Pokemanager.Model.Dump;
using Pokemanager.Model.Projects;
using Pokemanager.Randomizer;

namespace Pokemanager.App.ViewModels;

/// <summary>Ventana «Ajustes de Pokemanager»: emulador, ROM base del proyecto y herramientas incluidas.</summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettings settings;
    private readonly UprService upr;
    private readonly IDialogs dialogs;
    private readonly Project? project;
    private readonly GameTitle title;

    public IReadOnlyList<EmulatorOption> Emulators { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SaveText))]
    public partial EmulatorOption? SelectedEmulator { get; set; }

    public SettingsViewModel(AppSettings settings, UprService upr, IDialogs dialogs, Project? project, GameTitle title)
    {
        this.settings = settings;
        this.upr = upr;
        this.dialogs = dialogs;
        this.project = project;
        this.title = title;

        var detected = EmulatorUserFolders.Detect();
        var options = new List<EmulatorOption> { new(null, $"Automático (el último usado: {detected.FirstOrDefault()?.Name ?? "ninguno"})") };
        options.AddRange(detected.Select(e => new EmulatorOption(e.Path, $"{e.Name} — {e.Path} (usado el {e.LastUsed:g})")));
        if (settings.EmulatorUserDirectory is { } custom && options.All(o => !string.Equals(o.Path, custom, StringComparison.OrdinalIgnoreCase)))
            options.Add(new EmulatorOption(custom, $"Personalizada — {custom}"));
        Emulators = options;
        SelectedEmulator = options.FirstOrDefault(o => string.Equals(o.Path, settings.EmulatorUserDirectory, StringComparison.OrdinalIgnoreCase)) ?? options[0];
    }

    partial void OnSelectedEmulatorChanged(EmulatorOption? value)
    {
        settings.EmulatorUserDirectory = value?.Path;
        settings.Save();
        OnPropertyChanged(nameof(EffectiveText));
    }

    public string EffectiveText => $"Se usa: {settings.EffectiveEmulatorName} ({settings.EffectiveEmulatorDirectory ?? "no encontrado"})";

    public string SaveText
    {
        get
        {
            if (settings.EffectiveEmulatorDirectory is not { } dir)
                return "Sin emulador no se puede localizar la partida.";
            string save = EmulatorUserFolders.SaveFile(dir, title.TitleId());
            return File.Exists(save)
                ? $"Partida de Pokémon {title}: {save} (guardada el {File.GetLastWriteTime(save):g})"
                : $"No hay partida de Pokémon {title} en esa carpeta.";
        }
    }

    public bool HasProject => project is not null;
    public string BaseRomText => project?.ResolveRomFile() ?? "No se encuentra la ROM base (.3ds)";

    public string UprText => File.Exists(UprLocator.BundledJar)
        ? $"Universal Pokémon Randomizer ZX 4.6.1 — {UprLocator.BundledJar}"
        : $"No se encuentra el randomizer incluido en {UprLocator.BundledJar}";

    public string PkhexText => $"PKHeX.Core {typeof(PKHeX.Core.SaveFile).Assembly.GetName().Version} — incluido en la aplicación";
    public string JavaText => upr.Tools is { } t ? $"Java {t.JavaMajorVersion} — {t.JavaPath}" : $"No se encuentra Java {UprLocator.MinimumJava} o superior (necesario para el randomizer)";
    public string CacheText => $"Caché de randomizaciones: {upr.Cache.Root}";
    public string BackupText => $"Copias de seguridad de partidas: {AppSettings.BackupRoot}";

    [RelayCommand]
    private async Task BrowseEmulator()
    {
        if (await dialogs.PickFolderAsync("Carpeta de usuario del emulador (la que contiene sdmc y load)", settings.EffectiveEmulatorDirectory) is not { } path)
            return;
        var option = Emulators.FirstOrDefault(o => string.Equals(o.Path, path, StringComparison.OrdinalIgnoreCase));
        if (option is null)
        {
            settings.EmulatorUserDirectory = path;
            settings.Save();
            option = new EmulatorOption(path, $"Personalizada — {path}");
        }
        SelectedEmulator = option;
        OnPropertyChanged(nameof(EffectiveText));
        OnPropertyChanged(nameof(SaveText));
    }

    [RelayCommand]
    private async Task BrowseBaseRom()
    {
        if (project is null)
            return;
        if (await dialogs.PickOpenFileAsync("ROM base descifrada de Pokémon X/Y", ["*.3ds", "*.cci", "*.cxi"]) is { } path)
        {
            project.RomFile = path;
            OnPropertyChanged(nameof(BaseRomText));
        }
    }

    [RelayCommand]
    private void ClearCache()
    {
        try
        {
            if (Directory.Exists(upr.Cache.Root))
                Directory.Delete(upr.Cache.Root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Algún archivo en uso: se deja; la caché es regenerable.
        }
    }

    [RelayCommand]
    private void OpenBackups()
    {
        Directory.CreateDirectory(AppSettings.BackupRoot);
        Process.Start(new ProcessStartInfo(AppSettings.BackupRoot) { UseShellExecute = true });
    }
}

public sealed record EmulatorOption(string? Path, string Label);
