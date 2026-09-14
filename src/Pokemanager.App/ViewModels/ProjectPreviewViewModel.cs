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

/// <summary>A read-only stat row of a party Pokémon.</summary>
public sealed record StatLine(string Label, int Base, int Iv, int Ev, int Value);

/// <summary>A read-only move of a party Pokémon, colored by its type.</summary>
public sealed record MoveLine(string Name, string PP, string Type, Avalonia.Media.IBrush Background, Avalonia.Media.IBrush Foreground);

/// <summary>A type label with its color.</summary>
public sealed record TypeChip(string Name, Avalonia.Media.IBrush Background, Avalonia.Media.IBrush Foreground);

/// <summary>A Pokémon of the party in the project preview. Nothing here can be edited.</summary>
public sealed partial class PartyMemberViewModel : ObservableObject
{
    private readonly PK6 pk;
    private readonly SaveDocument doc;
    private readonly SaveNames names;
    private readonly PokemonSprites sprites;

    public SaveSlot Slot { get; }

    public PartyMemberViewModel(SaveDocument doc, SaveNames names, PokemonSprites sprites, SaveSlot slot)
    {
        this.doc = doc;
        this.names = names;
        this.sprites = sprites;
        Slot = slot;
        pk = doc.Get(slot);
    }

    public Bitmap? Icon => pk.IsEgg ? null : sprites.For(pk.Species, pk.Form, pk.Gender == 1, pk.IsShiny);
    public string Name => pk.IsEgg ? Strings.Save_Egg : pk.IsNicknamed ? pk.Nickname : names.SpeciesName(pk.Species);
    public string SpeciesName => names.SpeciesName(pk.Species);
    public bool HasNickname => pk.IsNicknamed && !pk.IsEgg;
    public string LevelText => string.Format(Strings.Preview_Level, doc.Level(pk));
    public string GenderText => pk.Gender switch { 0 => "♂", 1 => "♀", _ => "" };
    public bool IsShiny => pk.IsShiny;
    public string ItemText => pk.HeldItem == 0 ? Strings.Preview_NoItem : names.ItemName(pk.HeldItem);
    public bool HasItem => pk.HeldItem != 0;

    // ------------------------------------------------------------------ types (from the ROM being played)

    private int[] TypeIds => doc.Personal(pk.Species, pk.Form)?.Types is { Length: >= 2 } t ? [t[0], t[1]] : [0, 0];

    /// <summary>One color for single-type Pokémon, two (split diagonally) for dual types.</summary>
    public Avalonia.Media.IBrush TypeBackground => pk.IsEgg ? TypeColors.Background(0) : TypeColors.Background(TypeIds[0], TypeIds[1]);

    public Avalonia.Media.IBrush TypeForeground => TypeColors.Foreground(TypeIds);

    public IReadOnlyList<TypeChip> TypeChips => TypeIds.Distinct()
        .Select(t => new TypeChip(t < names.Types.Count ? names.Types[t] : $"#{t}", TypeColors.Background(t), TypeColors.Foreground(t)))
        .ToList();

    // ------------------------------------------------------------------ detail

    public string AbilityText
    {
        get
        {
            string slotName = pk.AbilityNumber switch { 2 => "2", 4 => Strings.Preview_Hidden, _ => "1" };
            return $"{names.AbilityName(pk.Ability)} ({slotName})";
        }
    }

    public string NatureText => (int)pk.Nature < names.Natures.Count ? names.Natures[(int)pk.Nature] : "—";
    public string BallText => pk.Ball < names.Balls.Count ? names.Balls[pk.Ball] : "—";
    public string FriendshipText => pk.CurrentFriendship.ToString();
    public string TrainerText => $"{pk.OriginalTrainerName} ({pk.TID16:00000})";
    public string EvTotalText => string.Format(Strings.Pkm_EvTotal, pk.EV_HP + pk.EV_ATK + pk.EV_DEF + pk.EV_SPA + pk.EV_SPD + pk.EV_SPE, SaveDocument.MaxEvTotal);

    public IReadOnlyList<StatLine> Stats
    {
        get
        {
            var p = doc.Personal(pk.Species, pk.Form);
            int[] values = doc.Stats(pk); // HP, Atk, Def, Spe, SpA, SpD
            return
            [
                new(Strings.Stat_HP, p?.HP ?? 0, pk.IV_HP, pk.EV_HP, values[0]),
                new(Strings.Stat_Atk, p?.ATK ?? 0, pk.IV_ATK, pk.EV_ATK, values[1]),
                new(Strings.Stat_Def, p?.DEF ?? 0, pk.IV_DEF, pk.EV_DEF, values[2]),
                new(Strings.Stat_SpA, p?.SPA ?? 0, pk.IV_SPA, pk.EV_SPA, values[4]),
                new(Strings.Stat_SpD, p?.SPD ?? 0, pk.IV_SPD, pk.EV_SPD, values[5]),
                new(Strings.Stat_Spe, p?.SPE ?? 0, pk.IV_SPE, pk.EV_SPE, values[3]),
            ];
        }
    }

    public IReadOnlyList<MoveLine> Moves
    {
        get
        {
            (ushort Move, int PP, int Ups)[] moves =
            [
                (pk.Move1, pk.Move1_PP, pk.Move1_PPUps), (pk.Move2, pk.Move2_PP, pk.Move2_PPUps),
                (pk.Move3, pk.Move3_PP, pk.Move3_PPUps), (pk.Move4, pk.Move4_PP, pk.Move4_PPUps),
            ];
            return moves.Select(m => m.Move == 0
                    ? new MoveLine("—", "", "", Avalonia.Media.Brushes.Transparent, Avalonia.Media.Brushes.Gray)
                    : MoveOf(m.Move, m.PP, m.Ups))
                .ToList();
        }
    }

    /// <summary>A move with the type it has in the ROM (the randomizer may change it).</summary>
    private MoveLine MoveOf(ushort move, int pp, int ups)
    {
        int type = move < doc.Rom.Moves.Length ? doc.Rom.Moves[move].Type : -1;
        string name = move < names.Moves.Count ? names.Moves[move] : $"#{move}";
        string typeName = type >= 0 && type < names.Types.Count ? names.Types[type] : "";
        return new MoveLine(name, string.Format(Strings.Pkm_PP, pp, doc.MaxPP(move, ups)), typeName, TypeColors.Background(type), TypeColors.Foreground(type));
    }
}

/// <summary>A gym badge in the preview: earned or not (from the save), and its roulette.</summary>
public sealed partial class BadgeItemViewModel(ProjectPreviewViewModel owner, int index, string name, int type, bool earned, LockeSpin? spin,
    IReadOnlyList<string> itemNames, IconImage? image, bool hasRoulette) : ObservableObject
{
    /// <summary>The badge as in the trainer card (grey and faded when not earned); null when the dump has no image.</summary>
    public Bitmap? Icon => field ??= image is null ? null : PokemonSprites.ToBitmap(Earned ? image : BadgeIcons.Faded(image));

    public bool HasIcon => image is not null;

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

    public ObservableCollection<PartyMemberViewModel> Party { get; } = [];

    public ObservableCollection<BadgeItemViewModel> Badges { get; } = [];

    public string BadgesText => string.Format(Strings.Locke_BadgesCount, Badges.Count(b => b.Earned), LockeSettings.BadgeCount);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    public partial PartyMemberViewModel? Selected { get; set; }

    public bool HasSelection => Selected is not null;

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

        GameText = string.Format(Strings.Preview_Game, loaded.Dump.Title, project.Edits.Count == 1
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
            SaveMessage = string.Format(Strings.Rnd_NoSave, loaded.Dump.Title, settings.EffectiveEmulatorName);
        }
        else
        {
            try
            {
                doc = SaveDocument.Open(savePath, loaded.Session.Current);
                var names = new SaveNames(gameNames, GameTextLanguage.Current, doc.MaxSpecies);
                Sprites = PokemonSprites.Load(project, loaded.Session.Original);
                TrainerText = string.Format(Strings.Preview_Trainer, doc.TrainerName, Enumerable.Range(0, 8).Count(doc.GetBadge), doc.Money,
                    $"{doc.PlayedHours}:{doc.PlayedMinutes:00}", settings.EffectiveEmulatorName, File.GetLastWriteTime(savePath));
                for (int i = 0; i < doc.PartyCount; i++)
                    Party.Add(new PartyMemberViewModel(doc, names, Sprites, new SaveSlot(null, i)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SaveUpdateException)
            {
                SaveMessage = string.Format(Strings.Rnd_SaveUnreadable, settings.EffectiveEmulatorName, ex.Message);
                doc = null;
            }
        }

        var badges = LockeRewards.Badges;
        var badgeImages = BadgeIcons.Load(new RomFsLayers(project.RomFsPath));
        for (int i = 0; i < LockeSettings.BadgeCount; i++)
            Badges.Add(new BadgeItemViewModel(this, i, badges[i].Name, badges[i].Type, doc?.GetBadge(i) ?? false, project.Locke.SpinOf(i), itemNames,
                badgeImages?[i], project.Locke.HasRoulette(i)));
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
            prize => LockeRewards.Claim(Loaded, main.Settings, badge.Index, prize, itemNames));
        await main.Dialogs.ShowRouletteAsync(roulette);
        if (roulette.Changed)
            await reload();
    }

    [RelayCommand]
    private void Manage() => main.Manage(Loaded);

    /// <summary>Opens the editor on the save editor with the selected Pokémon.</summary>
    [RelayCommand]
    private void ManagePokemon()
    {
        if (Selected is { } member)
            main.Manage(Loaded, member.Slot);
    }
}
