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

    /// <summary>The open project's version history, shown as a button here; null on the start screen.</summary>
    public HistoryViewModel? History { get; }

    public bool HasHistory => History is not null;

    [RelayCommand]
    private Task OpenHistory()
    {
        if (History is null)
            return Task.CompletedTask;
        History.Refresh();
        return dialogs.ShowHistoryAsync(History);
    }

    public SettingsViewModel(AppSettings settings, UprService upr, IDialogs dialogs, Project? project, GameTitle title,
        HistoryViewModel? history = null)
    {
        History = history;
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
        ProfileAvatar = new AvatarViewModel(new Multiplayer.PlayerProfile(settings.Profile.Name.Trim().Length > 0 ? settings.Profile.Name : "?",
            settings.Profile.Sprite, SafeImage(settings.Profile.Image), settings.Profile.Color));
        _ = SearchSpritesAsync("");
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

    private static byte[]? SafeImage(string? base64)
    {
        try { return base64 is null ? null : Convert.FromBase64String(base64); }
        catch (FormatException) { return null; }
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

    /// <summary>Program used by "Play", with how it was found.</summary>
    public string ExecutableText
    {
        get
        {
            string? path = settings.EffectiveEmulatorExecutable(project?.ResolveRomFile(), project?.DumpDirectory);
            if (path is null)
                return Strings.Set_ProgramNotFound;
            string how = settings.EmulatorExecutable is { } chosen && File.Exists(chosen) ? Strings.Set_ProgramChosen : Strings.Set_ProgramDetected;
            return string.Format(Strings.Set_Program, EmulatorExecutables.NameOf(path), path, how);
        }
    }

    public bool HasChosenExecutable => settings.EmulatorExecutable is not null;

    [RelayCommand]
    private async Task BrowseExecutable()
    {
        if (await dialogs.PickOpenFileAsync(Strings.Set_PickProgram, OperatingSystem.IsWindows() ? ["*.exe"] : ["*"]) is not { } path)
            return;
        settings.EmulatorExecutable = path;
        settings.Save();
        OnPropertyChanged(nameof(ExecutableText));
        OnPropertyChanged(nameof(HasChosenExecutable));
    }

    [RelayCommand]
    private void DetectExecutable()
    {
        settings.EmulatorExecutable = null;
        settings.Save();
        OnPropertyChanged(nameof(ExecutableText));
        OnPropertyChanged(nameof(HasChosenExecutable));
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

/// <summary>A trainer sprite offered in the profile picker; its picture downloads in the background.</summary>
public sealed partial class SpriteOption : ObservableObject
{
    private readonly SettingsViewModel owner;

    public SpriteOption(SettingsViewModel owner, string name)
    {
        this.owner = owner;
        Name = name;
        _ = LoadAsync();
    }

    public string Name { get; }

    [ObservableProperty]
    public partial Avalonia.Media.Imaging.Bitmap? Image { get; private set; }

    private async Task LoadAsync()
    {
        var bitmap = await TrainerSprites.GetAsync(Name);
        Avalonia.Threading.Dispatcher.UIThread.Post(() => Image = bitmap);
    }

    [RelayCommand]
    private void Choose() => owner.ChooseSprite(Name);
}

public sealed record ColorOption(string Hex)
{
    public Avalonia.Media.IBrush Brush { get; } = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(Hex));
}

public partial class SettingsViewModel
{
    // ------------------------------------------------------------------ profile (multiplayer)

    public static IReadOnlyList<ColorOption> ProfileColors { get; } =
    [
        new("#1C3A70"), new("#0078D7"), new("#2E8B57"), new("#7AC74C"), new("#E0A800"), new("#EE8130"),
        new("#D13438"), new("#D685AD"), new("#A33EA1"), new("#6F35FC"), new("#705746"), new("#4A4A4A"),
    ];

    private ProfileSettings Profile => settings.Profile;

    public string ProfileName
    {
        get => Profile.Name;
        set
        {
            if (Profile.Name == value)
                return;
            Profile.Name = value;
            ProfileSaved();
        }
    }

    [ObservableProperty]
    public partial AvatarViewModel ProfileAvatar { get; private set; } = null!;

    public string ProfileSpriteText => Profile.Image is not null
        ? Strings.Profile_UsingImage
        : Profile.Sprite is { } s ? string.Format(Strings.Profile_UsingSprite, s) : Strings.Profile_NoPicture;

    public bool HasCustomImage => Profile.Image is not null;

    public System.Collections.ObjectModel.ObservableCollection<SpriteOption> SpriteResults { get; } = [];

    [ObservableProperty]
    public partial string SpriteQuery { get; set; } = "";

    [ObservableProperty]
    public partial string SpriteStatus { get; private set; } = "";

    private int searchVersion;

    private void ProfileSaved()
    {
        settings.Save();
        ProfileAvatar = new AvatarViewModel(new Multiplayer.PlayerProfile(
            Profile.Name.Trim().Length > 0 ? Profile.Name : "?", Profile.Sprite,
            SafeImage(Profile.Image), Profile.Color));
        OnPropertyChanged(nameof(ProfileSpriteText));
        OnPropertyChanged(nameof(HasCustomImage));
    }

    partial void OnSpriteQueryChanged(string value) => _ = SearchSpritesAsync(value);

    public async Task SearchSpritesAsync(string query)
    {
        int version = ++searchVersion;
        await Task.Delay(250); // typing
        if (version != searchVersion)
            return;
        SpriteStatus = Strings.Profile_Searching;
        var found = await TrainerSprites.SearchAsync(query, limit: 48);
        if (version != searchVersion)
            return;
        SpriteResults.Clear();
        foreach (string name in found)
            SpriteResults.Add(new SpriteOption(this, name));
        SpriteStatus = found.Count == 0
            ? (await TrainerSprites.IndexAsync()).Count == 0 ? Strings.Profile_Offline : Strings.Profile_NoResults
            : query.Trim().Length == 0 ? Strings.Profile_Featured : string.Format(Strings.Profile_Results, found.Count);
    }

    public void ChooseSprite(string name)
    {
        Profile.Sprite = name;
        Profile.Image = null;
        ProfileSaved();
    }

    [RelayCommand]
    private void ChooseColor(ColorOption color)
    {
        Profile.Color = color.Hex;
        ProfileSaved();
    }

    [RelayCommand]
    private async Task ChooseImage()
    {
        if (await dialogs.PickOpenFileAsync(Strings.Profile_PickImage, ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif"]) is not { } path)
            return;
        if (TrainerSprites.ProfileImage(path) is { } bytes)
        {
            Profile.Image = Convert.ToBase64String(bytes);
            ProfileSaved();
            SpriteStatus = "";
        }
        else
        {
            SpriteStatus = Strings.Profile_BadImage;
        }
    }

    [RelayCommand]
    private void RemoveImage()
    {
        Profile.Image = null;
        ProfileSaved();
    }
}
