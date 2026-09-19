using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Core;
using Pokemanager.App.Resources;
using Pokemanager.App.Services;
using Pokemanager.Bridge;
using Pokemanager.Model.Dump;
using Pokemanager.Model.Data;
using Pokemanager.Model.Projects;
using Pokemanager.Save;

namespace Pokemanager.App.ViewModels;

/// <summary>A read-only move of a party Pokémon, colored by its type, with the type and category icons of the save editor.</summary>
public sealed record MoveLine(string Name, string PP, string Type, Avalonia.Media.IBrush Background, Avalonia.Media.IBrush Foreground,
    Bitmap? TypeIcon = null, Avalonia.Media.IImage? CategoryIcon = null)
{
    /// <summary>An empty slot, drawn as the save editor draws one.</summary>
    public static MoveLine Empty { get; } = new("—", "", "", new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#F2F4F6")), Avalonia.Media.Brushes.Gray);

    public static MoveLine Of(string name, string pp, int type, string typeName, int category) =>
        new(name, pp, typeName, TypeColors.Background(type), TypeColors.Foreground(type),
            type >= 0 ? PkhexImages.Type(type) : null, category >= 0 ? PkhexImages.Category(category) : null);
}

/// <summary>A type label with its color.</summary>
public sealed record TypeChip(string Name, Avalonia.Media.IBrush Background, Avalonia.Media.IBrush Foreground);

/// <summary>A Pokémon of the party in the project preview. Nothing here can be edited.</summary>
public sealed partial class PartyMemberViewModel : ObservableObject, IMonCard
{
    private readonly PKM pk;
    private readonly SaveDocument doc;
    private readonly SaveNames names;
    private readonly PokemonSprites sprites;
    private readonly Action<PartyMemberViewModel> select;

    public SaveSlot Slot { get; }

    public PartyMemberViewModel(SaveDocument doc, SaveNames names, PokemonSprites sprites, SaveSlot slot, Action<PartyMemberViewModel> select)
    {
        this.doc = doc;
        this.names = names;
        this.sprites = sprites;
        this.select = select;
        Slot = slot;
        pk = doc.Get(slot);
    }

    public Bitmap? Icon => pk.IsEgg ? null : sprites.For(pk.Species, pk.Form, pk.Gender == 1, pk.IsShiny);
    public string Name => pk.IsEgg ? Strings.Save_Egg : pk.IsNicknamed ? pk.Nickname : names.SpeciesName(pk.Species);
    public string SpeciesName => names.SpeciesName(pk.Species);
    public string LevelText => string.Format(Strings.Preview_Level, doc.Level(pk));
    public string GenderText => pk.IsEgg ? "" : pk.Gender switch { 0 => "♂", 1 => "♀", _ => "" };
    public bool IsShiny => pk.IsShiny;
    public bool IsEgg => pk.IsEgg;
    public bool IsEmpty => false;
    public bool HasProblem => false;
    public string ItemText => pk.HeldItem == 0 ? Strings.Preview_NoItem : names.ItemName(pk.HeldItem);
    public bool HasItem => pk.HeldItem != 0;
    public Bitmap? ItemIcon => PkhexImages.Item(pk.HeldItem);
    public string Tooltip => $"{Name} · {SpeciesName} · {LevelText}";

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [RelayCommand]
    private void Select() => select(this);

    // ------------------------------------------------------------------ types (from the ROM being played)

    private int[] TypeIds => doc.Personal(pk.Species, pk.Form)?.Types is { Length: >= 2 } t ? [t[0], t[1]] : [0, 0];

    /// <summary>One color for single-type Pokémon, two (split diagonally) for dual types.</summary>
    public Avalonia.Media.IBrush TypeBackground => pk.IsEgg ? TypeColors.Background(0) : TypeColors.Background(TypeIds[0], TypeIds[1]);

    public Avalonia.Media.IBrush TypeForeground => TypeColors.Foreground(TypeIds);

    public IReadOnlyList<TypeChip> TypeChips => pk.IsEgg ? [] : TypeIds.Distinct()
        .Select(t => new TypeChip(t < names.Types.Count ? names.Types[t] : $"#{t}", TypeColors.Background(t), TypeColors.Foreground(t)))
        .ToList();

    // ------------------------------------------------------------------ detail, laid out as the save editor

    public MonSheet Sheet(IRelayCommand manage)
    {
        var p = doc.Personal(pk.Species, pk.Form);
        int[] values = doc.Stats(pk); // HP, Atk, Def, Spe, SpA, SpD
        SheetStat[] stats =
        [
            new(Strings.Stat_HP, values[0], p?.HP, pk.IV_HP, pk.EV_HP),
            new(Strings.Stat_Atk, values[1], p?.ATK, pk.IV_ATK, pk.EV_ATK, Nature: StatBars.NatureEffect((int)pk.StatAlignment, 1)),
            new(Strings.Stat_Def, values[2], p?.DEF, pk.IV_DEF, pk.EV_DEF, Nature: StatBars.NatureEffect((int)pk.StatAlignment, 2)),
            new(Strings.Stat_SpA, values[4], p?.SPA, pk.IV_SPA, pk.EV_SPA, Nature: StatBars.NatureEffect((int)pk.StatAlignment, 3)),
            new(Strings.Stat_SpD, values[5], p?.SPD, pk.IV_SPD, pk.EV_SPD, Nature: StatBars.NatureEffect((int)pk.StatAlignment, 4)),
            new(Strings.Stat_Spe, values[3], p?.SPE, pk.IV_SPE, pk.EV_SPE, Nature: StatBars.NatureEffect((int)pk.StatAlignment, 5)),
        ];
        (ushort Move, int PP, int Ups)[] slots =
        [
            (pk.Move1, pk.Move1_PP, pk.Move1_PPUps), (pk.Move2, pk.Move2_PP, pk.Move2_PPUps),
            (pk.Move3, pk.Move3_PP, pk.Move3_PPUps), (pk.Move4, pk.Move4_PP, pk.Move4_PPUps),
        ];
        var moves = slots.Select(m => m.Move == 0 ? MoveLine.Empty : MoveOf(m.Move, m.PP, m.Ups)).ToList();

        string slotName = pk.AbilityNumber switch { 2 => "2", 4 => Strings.Preview_Hidden, _ => "1" };
        string ability = $"{names.AbilityName(pk.Ability)} ({slotName})";
        string nature = (int)pk.Nature < names.Natures.Count ? names.Natures[(int)pk.Nature] : "—";
        var info = new List<RoomInfoLine>
        {
            new(Strings.Pkm_Species, SpeciesName),
            new(Strings.Pkm_AbilityLabel, ability),
            new(Strings.Pkm_Nature, nature),
            new(Strings.Pkm_HeldItem, ItemText),
            new(Strings.Pkm_Friendship, pk.CurrentFriendship.ToString()),
            new(Strings.Pkm_Ball, pk.Ball < names.Balls.Count ? names.Balls[pk.Ball] : "—"),
            new(Strings.Pkm_OriginalTrainer, $"{pk.OriginalTrainerName} ({pk.TID16:00000})"),
        };
        string evTotal = string.Format(Strings.Pkm_EvTotal, pk.EV_HP + pk.EV_ATK + pk.EV_DEF + pk.EV_SPA + pk.EV_SPD + pk.EV_SPE, SaveDocument.MaxEvTotal);
        return new MonSheet(this, $"{LevelText} · {nature} · {names.AbilityName(pk.Ability)}", PkhexImages.Ball(pk.Ball), stats, moves, info,
            evTotal, manage, Strings.Preview_ManagePokemon);
    }

    /// <summary>A move with the type and category it has in the ROM (the randomizer may change them).</summary>
    private MoveLine MoveOf(ushort move, int pp, int ups)
    {
        var data = move < doc.Rom.Moves.Length ? doc.Rom.Moves[move] : null;
        int type = data?.Type ?? -1;
        string name = move < names.Moves.Count ? names.Moves[move] : $"#{move}";
        string typeName = type >= 0 && type < names.Types.Count ? names.Types[type] : "";
        return MoveLine.Of(name, string.Format(Strings.Pkm_PP, pp, doc.MaxPP(move, ups)), type, typeName, data?.Category ?? -1);
    }
}
/// <summary>A gym badge in the preview: earned or not (from the save), and its roulette.</summary>
/// <param name="crystal">Generation 7: the trial's Z-crystal, used instead of the game's seal (the fifth trial has none).</param>
public sealed partial class BadgeItemViewModel(ProjectPreviewViewModel owner, int index, string name, int type, bool earned, LockeSpin? spin,
    IReadOnlyList<string> itemNames, IconImage? image, bool hasRoulette, int? crystalType = null, IconImage? crystalImage = null) : ObservableObject
{
    /// <summary>
    /// The badge or seal — or the trial's Z-crystal, the game's own icon when the ROM has it —, greyed out while it is not
    /// earned. The image is made here and not in the constructor: this view model is built off the UI thread and an
    /// Avalonia image belongs to the thread that creates it.
    /// </summary>
    public Avalonia.Media.IImage? Icon =>
        field ??= crystalImage is { } crystal ? PokemonSprites.ToBitmap(crystal)
            : crystalType is { } type ? PkhexImages.TrialCrystalArt(type)
            : image is null ? null : PokemonSprites.ToBitmap(Earned ? image : MilestoneIcons.Faded(image));

    public bool HasIcon => Icon is not null;

    /// <summary>A crystal keeps its colour while the trial is not cleared — greyed out, every type would look the same.</summary>
    public double IconOpacity => crystalType is not null && !Earned ? 0.4 : 1;

    public int Index { get; } = index;
    public string Name { get; } = name;
    public string Number => (Index + 1).ToString();
    public bool Earned { get; } = earned;

    public Avalonia.Media.IBrush Background => HasIcon ? Avalonia.Media.Brushes.Transparent : Earned ? TypeColors.Background(type) : new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#DADADA"));
    public Avalonia.Media.IBrush Foreground => Earned ? TypeColors.Foreground(type) : new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#8A8A8A"));

    /// <summary>✓ prize given · ! prize won but not in the save yet · empty otherwise.</summary>
    public string Mark => spin is null ? "" : spin.Claimed ? "✓" : "!";
    public bool HasMark => spin is not null;
    public Avalonia.Media.IBrush MarkBrush => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(IsPending ? "#E07B00" : "#2E8B57"));
    public bool IsPending => spin is { Claimed: false };

    /// <summary>A pending prize can always be claimed, even if the roulette interval changed since.</summary>
    public bool CanSpin => Earned && (spin is { Claimed: false } || (spin is null && hasRoulette));

    public string Tooltip => !Earned
        ? string.Format(Strings.Locke_BadgeNotEarned, Name)
        : spin is null
            ? string.Format(hasRoulette ? Strings.Locke_BadgeSpin : Strings.Locke_BadgeNoRoulette, Name)
            : spin.Claimed
                ? string.Format(Strings.Locke_BadgeDone, Name, LockeRewards.Describe(spin.Prize, itemNames))
                : string.Format(Strings.Locke_BadgePending, Name, LockeRewards.Describe(spin.Prize, itemNames));

    [RelayCommand]
    private Task Spin() => owner.SpinBadgeAsync(this);

    internal LockePrize? PendingPrize => spin is { Claimed: false } s ? s.Prize : null;
}

/// <summary>
/// Right side of the project list: what the ROM is, the badges and lives of the run and the team of its save, read only.
/// "Manage" opens the editor.
/// </summary>
public sealed partial class ProjectPreviewViewModel : ObservableObject
{
    private readonly MainWindowViewModel main;
    private readonly Func<Task> reload;
    private readonly IReadOnlyList<string> itemNames;

    public LoadedProject Loaded { get; }

    public string Name => Path.GetFileNameWithoutExtension(Loaded.Path);

    public string GameText { get; }
    public string RomText { get; }
    public string RandomText { get; }

    /// <summary>Why there is no team to show (no emulator, no save…), or null.</summary>
    public string? SaveMessage { get; }
    public bool HasSave => SaveMessage is null;

    public string TrainerText { get; } = "";

    /// <summary>The level cap the save is on, when the project uses the cap by milestones.</summary>
    public string LevelCapText { get; } = "";

    public bool HasLevelCap => LevelCapText.Length > 0;

    public ObservableCollection<PartyMemberViewModel> Party { get; } = [];

    public ObservableCollection<BadgeItemViewModel> Badges { get; } = [];

    public string BadgesText => string.Format(Loaded.Project.Game.HasBadges() ? Strings.Locke_BadgesCount : Strings.Locke_TrialsCount,
        Badges.Count(b => b.Earned), Badges.Count);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(SelectedSheet))]
    public partial PartyMemberViewModel? Selected { get; set; }

    public bool HasSelection => Selected is not null;

    /// <summary>The selected Pokémon laid out as in the save editor (and as the other players in a room).</summary>
    public MonSheet? SelectedSheet => Selected?.Sheet(ManagePokemonCommand);

    partial void OnSelectedChanged(PartyMemberViewModel? value)
    {
        foreach (var member in Party)
            member.IsSelected = ReferenceEquals(member, value);
    }

    /// <summary>Loads everything but the bitmaps, so it can run off the UI thread.</summary>
    public ProjectPreviewViewModel(MainWindowViewModel main, LoadedProject loaded, Func<Task> reload)
    {
        this.main = main;
        this.reload = reload;
        Loaded = loaded;
        var project = loaded.Project;
        var r = project.Randomization;
        var gameNames = new GameNames(loaded.Dump, loaded.Session.Original);
        itemNames = gameNames.Items;

        GameText = string.Format(Strings.Preview_Game, loaded.Dump.Title.DisplayName(), project.Edits.Count == 1
            ? Strings.Editor_EditCountOne
            : string.Format(Strings.Editor_EditCount, project.Edits.Count));
        RomText = r.LastBuiltRom is { } rom && File.Exists(rom)
            ? string.Format(Strings.Preview_Rom, Path.GetFileName(rom), File.GetLastWriteTime(rom))
            : Strings.Preview_NoRom;
        RandomText = r.Enabled
            ? string.Format(Strings.History_Random, r.Seed, r.PresetName ?? Strings.Rnd_PresetDefaults)
            : Strings.History_NotRandom;

        var settings = main.Settings;
        string? savePath = settings.EffectiveEmulatorDirectory is { } dir ? EmulatorUserFolders.SaveFile(dir, loaded.Dump.Title.TitleId()) : null;
        SaveDocument? doc = null;
        if (savePath is null)
        {
            SaveMessage = Strings.Rnd_NoEmulator;
        }
        else if (!File.Exists(savePath))
        {
            SaveMessage = string.Format(Strings.Rnd_NoSave, loaded.Dump.Title.DisplayName(), settings.EffectiveEmulatorName);
        }
        else
        {
            try
            {
                doc = SaveDocument.Open(savePath, loaded.Session.Current);
                var names = new SaveNames(gameNames, GameTextLanguage.Current, doc.MaxSpecies);
                Sprites = PokemonSprites.Load(project, loaded.Session.Original);
                TrainerText = string.Format(doc.Generation == 6 ? Strings.Preview_Trainer : Strings.Preview_TrainerTrials, doc.TrainerName, Enumerable.Range(0, doc.MilestoneCount).Count(doc.GetMilestone), doc.Money,
                    $"{doc.PlayedHours}:{doc.PlayedMinutes:00}", settings.EffectiveEmulatorName, File.GetLastWriteTime(savePath));
                for (int i = 0; i < doc.PartyCount; i++)
                    Party.Add(new PartyMemberViewModel(doc, names, Sprites, new SaveSlot(null, i), m => Selected = m));
                LevelCapText = LevelCapViewModel.Indicator(loaded.Session, doc, itemNames) ?? "";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SaveUpdateException)
            {
                SaveMessage = string.Format(Strings.Rnd_SaveUnreadable, settings.EffectiveEmulatorName, ex.Message);
                doc = null;
            }
        }

        var milestones = project.Game.Milestones();
        var layers = new RomFsLayers(project.RomFsPath);
        var images = MilestoneIcons.Load(layers, project.Game);
        var crystals = ItemIcons.LoadTypeCrystals(layers, project.Game);
        for (int i = 0; i < milestones.Count; i++)
            Badges.Add(new BadgeItemViewModel(this, i, milestones[i].Name, milestones[i].Type, doc?.GetMilestone(i) ?? false, project.Locke.SpinOf(i), itemNames,
                images?[i], project.Locke.HasRoulette(i), project.Game.HasBadges() ? null : milestones[i].Type,
                crystals is not null && !project.Game.HasBadges() && (uint)milestones[i].Type < crystals.Length ? crystals[milestones[i].Type] : null));
    }

    public PokemonSprites Sprites { get; } = PokemonSprites.Empty;

    // ------------------------------------------------------------------ lives

    public bool TracksLives => Loaded.Project.Locke.MaxLives > 0;

    /// <summary>A full heart per life left, an empty one per life lost.</summary>
    public string Hearts => new string('♥', Loaded.Project.Locke.LivesLeft) + new string('♡', Math.Min(Loaded.Project.Locke.LivesLost, Loaded.Project.Locke.MaxLives));

    public string LivesText => string.Format(Strings.Locke_Lives, Loaded.Project.Locke.LivesLeft, Loaded.Project.Locke.MaxLives);

    public bool IsGameOver => TracksLives && Loaded.Project.Locke.LivesLeft == 0;

    [RelayCommand]
    private void LoseLife() => ChangeLives(+1);

    [RelayCommand]
    private void GainLife() => ChangeLives(-1);

    private void ChangeLives(int lostDelta)
    {
        var locke = Loaded.Project.Locke;
        int lost = Math.Clamp(locke.LivesLost + lostDelta, 0, locke.MaxLives);
        if (lost == locke.LivesLost)
            return;
        locke.LivesLost = lost;
        try
        {
            Loaded.Project.Save(Loaded.Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The count stays in memory; it is saved with the next change.
        }
        OnPropertyChanged(nameof(Hearts));
        OnPropertyChanged(nameof(LivesText));
        OnPropertyChanged(nameof(IsGameOver));
    }

    // ------------------------------------------------------------------ roulette

    internal async Task SpinBadgeAsync(BadgeItemViewModel badge)
    {
        if (!badge.CanSpin)
            return;
        var roulette = new RouletteViewModel(badge.Name, Loaded.Project.Locke, itemNames, badge.PendingPrize,
            prize => LockeRewards.RecordWin(Loaded, badge.Index, prize),
            prize => LockeRewards.Claim(Loaded, main.Settings, badge.Index, prize, itemNames),
            () => LockeRewards.MarkAddedByHand(Loaded, badge.Index));
        await main.Dialogs.ShowRouletteAsync(roulette);
        if (roulette.Changed)
            await reload();
    }

    [RelayCommand]
    private void Manage() => main.Manage(Loaded);

    /// <summary>What the last ▶ Play did (or why it could not), under the project's summary.</summary>
    [ObservableProperty]
    public partial string PlayStatus { get; private set; } = "";

    [ObservableProperty]
    public partial bool PlayFailed { get; private set; }

    /// <summary>
    /// ▶ Play without opening the editor: the project's last built ROM in the emulator, as the editor's button does. The
    /// editor also warns when the project has changes the ROM does not; here the project is the saved one, so what is
    /// built is what there is.
    /// </summary>
    [RelayCommand]
    private async Task Play()
    {
        var project = Loaded.Project;
        var r = project.Randomization;
        string? rom = r.LastBuiltRom is { } built && File.Exists(built)
            ? built
            : !r.Enabled && project.Edits.Count == 0 ? project.ResolveRomFile() : null;
        if (rom is null) { ShowPlay(Strings.Play_BuildFirst, error: true); return; }
        var settings = main.Settings;
        if (EmulatorUserFolders.RunningEmulators(settings.EffectiveEmulatorName) is { Count: > 0 } running)
        {
            ShowPlay(string.Format(Strings.Play_AlreadyRunning, string.Join(", ", running)), error: true);
            return;
        }
        string? exe = await Task.Run(() => settings.EffectiveEmulatorExecutable(rom, project.DumpDirectory));
        if (exe is null) { ShowPlay(Strings.Play_NoProgram, error: true); return; }
        try
        {
            EmulatorExecutables.Launch(exe, rom);
            ShowPlay(string.Format(Strings.Play_Started, EmulatorExecutables.NameOf(exe), Path.GetFileName(rom)), error: false);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            ShowPlay(string.Format(Strings.Play_Failed, exe, ex.Message), error: true);
        }
    }

    private void ShowPlay(string text, bool error)
    {
        PlayStatus = text;
        PlayFailed = error;
    }

    /// <summary>Opens the editor on the save editor with the selected Pokémon.</summary>
    [RelayCommand]
    private void ManagePokemon()
    {
        if (Selected is { } member)
            main.Manage(Loaded, member.Slot);
    }
}
