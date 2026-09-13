using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pokemanager.App.Resources;
using Pokemanager.App.Services;
using Pokemanager.Bridge;
using Pokemanager.Model.Dump;
using Pokemanager.Model.Projects;
using Pokemanager.Randomizer;

namespace Pokemanager.App.ViewModels;

/// <summary>A UI language shown with its own (native) name.</summary>
public sealed record UiLanguageOption(string Code, string Name);

/// <summary>"Pokemanager settings" window: language, emulator, the project's base ROM and the bundled tools.</summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettings settings;
    private readonly UprService upr;
    private readonly IDialogs dialogs;
    private readonly Project? project;
    private readonly GameTitle title;

    // Showing the current selection when the window opens is not a change: only what the user picks is saved.
    private readonly bool initialized;

    public static IReadOnlyList<UiLanguageOption> UiLanguages { get; } = [new("en", "English"), new("es", "Español")];

    public IReadOnlyList<EmulatorOption> Emulators { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SaveText))]
    public partial EmulatorOption? SelectedEmulator { get; set; }

    [ObservableProperty]
    public partial UiLanguageOption SelectedLanguage { get; set; }

    [ObservableProperty]
    public partial bool LanguageChanged { get; set; }

    public SettingsViewModel(AppSettings settings, UprService upr, IDialogs dialogs, Project? project, GameTitle title)
    {
        this.settings = settings;
        this.upr = upr;
        this.dialogs = dialogs;
        this.project = project;
        this.title = title;

        var detected = EmulatorUserFolders.Detect();
        var options = new List<EmulatorOption> { new(null, string.Format(Strings.Set_Automatic, detected.FirstOrDefault()?.Name ?? Strings.Set_None)) };
        options.AddRange(detected.Select(e => new EmulatorOption(e.Path, string.Format(Strings.Set_DetectedItem, e.Name, e.Path, e.LastUsed))));
        if (settings.EmulatorUserDirectory is { } custom && options.All(o => !string.Equals(o.Path, custom, StringComparison.OrdinalIgnoreCase)))
            options.Add(new EmulatorOption(custom, string.Format(Strings.Set_Custom, custom)));
        Emulators = options;
        SelectedEmulator = options.FirstOrDefault(o => string.Equals(o.Path, settings.EmulatorUserDirectory, StringComparison.OrdinalIgnoreCase)) ?? options[0];
        SelectedLanguage = UiLanguages.FirstOrDefault(l => l.Code == settings.UiLanguage) ?? UiLanguages[0];
        initialized = true;
    }

    partial void OnSelectedEmulatorChanged(EmulatorOption? value)
    {
        if (!initialized)
            return;
        settings.EmulatorUserDirectory = value?.Path;
        settings.Save();
        OnPropertyChanged(nameof(EffectiveText));
    }

    partial void OnSelectedLanguageChanged(UiLanguageOption value)
    {
        if (!initialized || value.Code == settings.UiLanguage)
            return;
        settings.UiLanguage = value.Code;
        settings.Save();
        LanguageChanged = true;
    }

    public string EffectiveText => string.Format(Strings.Set_Using, settings.EffectiveEmulatorName, settings.EffectiveEmulatorDirectory ?? Strings.Set_NotFound);

    public string SaveText
    {
        get
        {
            if (settings.EffectiveEmulatorDirectory is not { } dir)
                return Strings.Set_NoEmulatorSave;
            string save = EmulatorUserFolders.SaveFile(dir, title.TitleId());
            return File.Exists(save)
                ? string.Format(Strings.Set_SaveFound, title, save, File.GetLastWriteTime(save))
                : string.Format(Strings.Set_SaveMissing, title);
        }
    }

    public bool HasProject => project is not null;
    public string BaseRomText => project?.ResolveRomFile() ?? Strings.Set_BaseRomMissing;

    public string UprText => File.Exists(UprLocator.BundledJar)
        ? string.Format(Strings.Set_Upr, UprLocator.BundledJar)
        : string.Format(Strings.Set_UprMissing, UprLocator.BundledJar);

    public string PkhexText => string.Format(Strings.Set_Pkhex, typeof(PKHeX.Core.SaveFile).Assembly.GetName().Version);
    public string JavaText => upr.Tools is { } t ? string.Format(Strings.Set_Java, t.JavaMajorVersion, t.JavaPath) : string.Format(Strings.Set_JavaMissing, UprLocator.MinimumJava);
    public string CacheText => string.Format(Strings.Set_Cache, upr.Cache.Root);
    public string BackupText => string.Format(Strings.Set_Backups, AppSettings.BackupRoot);

    [RelayCommand]
    private async Task BrowseEmulator()
    {
        if (await dialogs.PickFolderAsync(Strings.Set_PickEmulator, settings.EffectiveEmulatorDirectory) is not { } path)
            return;
        var option = Emulators.FirstOrDefault(o => string.Equals(o.Path, path, StringComparison.OrdinalIgnoreCase));
        if (option is null)
        {
            settings.EmulatorUserDirectory = path;
            settings.Save();
            option = new EmulatorOption(path, string.Format(Strings.Set_Custom, path));
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
        if (await dialogs.PickOpenFileAsync(Strings.Set_PickBaseRom, ["*.3ds", "*.cci", "*.cxi"]) is { } path)
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
            // Some file in use: leave it; the cache is regenerable.
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
