using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Core;
using Pokemanager.App.Resources;
using Pokemanager.App.Services;
using Pokemanager.Model.Data;
using Pokemanager.Model.Dump;
using Pokemanager.Save;

namespace Pokemanager.App.ViewModels;

/// <summary>Trainer data, following PKHeX's trainer editor for X/Y.</summary>
public partial class SaveTrainerViewModel : ObservableObject
{
    private readonly SaveEditorViewModel owner;
    private readonly SaveDocument doc;
    private readonly List<RecordRowViewModel> allRecords;

    private readonly GameTitle game;

    public SaveTrainerViewModel(SaveEditorViewModel owner, SaveDocument doc, SaveNames names, GameTitle game, IconImage?[]? milestoneImages, AvatarViewModel avatar)
    {
        this.owner = owner;
        this.doc = doc;
        this.game = game;
        Avatar = avatar;
        var milestones = game.Milestones();
        VivillonPatterns = names.VivillonPatterns;
        Badges = Enumerable.Range(0, Math.Min(doc.MilestoneCount, milestones.Count))
            .Select(i => new BadgeViewModel(this, doc, i, milestones[i].Name, milestoneImages is { } images && i < images.Length ? images[i] : null)).ToList();
        Sayings = Enumerable.Range(0, 5).Select(i => new SayingViewModel(owner, doc, i)).ToList();
        Maison = SaveDocument.MaisonStyles.Select(s => new MaisonRowViewModel(owner, doc, s)).ToList();
        allRecords = doc.RecordNames.Select(r => new RecordRowViewModel(owner, doc, r.Id, r.Name)).ToList();
        ApplyRecordFilter();
    }

    /// <summary>The save editor refreshes the trainer card with every change (<see cref="RefreshCard"/>).</summary>
    private void Changed() => owner.Touch();

    // ------------------------------------------------------------------ trainer card (read only, follows the edits)

    public AvatarViewModel Avatar { get; }

    public string CardGame => game.DisplayName();

    /// <summary>Battle Points of the card.</summary>
    public string CardBattlePoints => doc.BattlePoints.ToString();

    /// <summary>Badge slots in a single row, as the card of the game shows them side by side.</summary>
    public int CardMilestoneColumns => Math.Max(1, Badges.Count);

    /// <summary>The card in the color of the game's box art.</summary>
    public Avalonia.Media.IBrush CardBrush => field ??= new Avalonia.Media.LinearGradientBrush
    {
        StartPoint = new Avalonia.RelativePoint(0, 0, Avalonia.RelativeUnit.Relative),
        EndPoint = new Avalonia.RelativePoint(1, 1, Avalonia.RelativeUnit.Relative),
        GradientStops = { new(Avalonia.Media.Color.Parse(GameColor(game)), 0), new(Darker(Avalonia.Media.Color.Parse(GameColor(game))), 1) },
    };

    private static Avalonia.Media.Color Darker(Avalonia.Media.Color c) => Avalonia.Media.Color.FromRgb((byte)(c.R * 0.62), (byte)(c.G * 0.62), (byte)(c.B * 0.62));

    private static string GameColor(GameTitle game) => game switch
    {
        GameTitle.X => "#2A5DB0", GameTitle.Y => "#C0392B", GameTitle.OmegaRuby => "#B8322A", GameTitle.AlphaSapphire => "#1F5FA8",
        GameTitle.Sun => "#E07B20", GameTitle.Moon => "#5B3F9E", GameTitle.UltraSun => "#D9601F", GameTitle.UltraMoon => "#44349A",
        _ => "#1C3A70",
    };

    public string CardName => doc.TrainerName;
    public string CardId => doc.TrainerId.ToString("00000");
    public string CardMoney => string.Format(Strings.Trainer_CardMoney, doc.Money);
    public string CardPlayTime => $"{doc.PlayedHours}:{doc.PlayedMinutes:00}";
    public string CardStarted => doc.GameStarted.ToString("d");
    public string CardFame => doc.HallOfFame is { } fame ? fame.ToString("d") : "—";

    public string CardDex
    {
        get
        {
            int seen = 0, caught = 0;
            for (ushort s = 1; s <= doc.MaxSpecies; s++)
            {
                if (doc.GetSeen(s)) seen++;
                if (doc.GetCaught(s)) caught++;
            }
            return string.Format(Strings.Trainer_CardDex, seen, caught);
        }
    }

    public string CardMilestones => string.Format(doc.Generation == 6 ? Strings.Locke_BadgesCount : Strings.Locke_TrialsCount, Badges.Count(b => b.IsChecked), Badges.Count);

    /// <summary>After an edit anywhere in the save (trainer page, Pokédex…).</summary>
    public void RefreshCard()
    {
        foreach (string name in new[] { nameof(CardName), nameof(CardMoney), nameof(CardPlayTime), nameof(CardStarted), nameof(CardFame), nameof(CardDex), nameof(CardMilestones) })
            OnPropertyChanged(name);
    }

    internal void BadgeChanged() => owner.Touch();

    // ------------------------------------------------------------------ general

    public string Name
    {
        get => doc.TrainerName;
        set { if (value.Length is > 0 and <= SaveDocument.MaxNameLength && value != doc.TrainerName) { doc.TrainerName = value; Changed(); } }
    }

    public IReadOnlyList<string> Genders { get; } = [Strings.Pkm_Male, Strings.Pkm_Female];

    public int GenderIndex
    {
        get => doc.TrainerGender;
        set { if (value is 0 or 1 && value != doc.TrainerGender) { doc.TrainerGender = value; Changed(); } }
    }

    public string IdText => string.Format(Strings.Trainer_Ids, doc.TrainerId.ToString("00000"), doc.SecretId.ToString("00000"));

    public decimal? Money
    {
        get => doc.Money;
        set { if (value is { } v && (uint)v != doc.Money) { doc.Money = (uint)Math.Clamp(v, 0, SaveDocument.MaxMoney); Changed(); } }
    }

    public decimal? BattlePoints
    {
        get => doc.BattlePoints;
        set { if (value is { } v && (int)v != doc.BattlePoints) { doc.BattlePoints = (int)v; Changed(); } }
    }

    public decimal? Hours
    {
        get => doc.PlayedHours;
        set { if (value is { } v && (int)v != doc.PlayedHours) { doc.PlayedHours = (int)v; Changed(); } }
    }

    public decimal? Minutes
    {
        get => doc.PlayedMinutes;
        set { if (value is { } v && (int)v != doc.PlayedMinutes) { doc.PlayedMinutes = (int)v; Changed(); } }
    }

    public decimal? Seconds
    {
        get => doc.PlayedSeconds;
        set { if (value is { } v && (int)v != doc.PlayedSeconds) { doc.PlayedSeconds = (int)v; Changed(); } }
    }

    public IReadOnlyList<BadgeViewModel> Badges { get; }

    /// <summary>"Badges" in Generation 6, "Trials" in Generation 7.</summary>
    public string MilestonesTitle => doc.Generation == 6 ? Strings.Trainer_Badges : Strings.Trainer_Trials;

    // What only some games have.
    public bool HasSayings => doc.HasSayings;
    public bool HasGen6Extras => doc.HasGen6Extras;
    public bool HasXYExtras => doc.HasXYExtras;
    public bool HasPosition => doc.HasPosition;
    public bool HasZMoves => doc.HasZMoves;

    public bool ZMoves
    {
        get => doc.ZMovesUnlocked;
        set { if (value != doc.ZMovesUnlocked) { doc.ZMovesUnlocked = value; Changed(); } }
    }

    public bool MegaEvolution
    {
        get => doc.MegaEvolutionUnlocked;
        set { if (value != doc.MegaEvolutionUnlocked) { doc.MegaEvolutionUnlocked = value; Changed(); } }
    }

    public IReadOnlyList<string> VivillonPatterns { get; }

    public int VivillonIndex
    {
        get => doc.Vivillon;
        set { if (value >= 0 && value < VivillonPatterns.Count && value != doc.Vivillon) { doc.Vivillon = value; Changed(); } }
    }

    public int BoxCount => doc.BoxCount;

    public decimal? BoxesUnlocked
    {
        get => doc.BoxesUnlocked;
        set { if (value is { } v && (int)v != doc.BoxesUnlocked) { doc.BoxesUnlocked = (int)v; Changed(); } }
    }

    public IReadOnlyList<SayingViewModel> Sayings { get; }

    public DateTime? StartedDate
    {
        get => doc.GameStarted.Date;
        set { if (value is { } d) { doc.GameStarted = d.Date + doc.GameStarted.TimeOfDay; Changed(); } }
    }

    public TimeSpan? StartedTime
    {
        get => doc.GameStarted.TimeOfDay;
        set { if (value is { } t) { doc.GameStarted = doc.GameStarted.Date + t; Changed(); } }
    }

    public bool HasHallOfFame
    {
        get => doc.HallOfFame is not null;
        set
        {
            if (value == HasHallOfFame)
                return;
            doc.HallOfFame = value ? DateTime.Now.Date + TimeSpan.FromHours(12) : null;
            OnPropertyChanged(nameof(FameDate));
            OnPropertyChanged(nameof(FameTime));
            Changed();
        }
    }

    public DateTime? FameDate
    {
        get => doc.HallOfFame?.Date;
        set { if (value is { } d && doc.HallOfFame is { } f) { doc.HallOfFame = d.Date + f.TimeOfDay; Changed(); } }
    }

    public TimeSpan? FameTime
    {
        get => doc.HallOfFame?.TimeOfDay;
        set { if (value is { } t && doc.HallOfFame is { } f) { doc.HallOfFame = f.Date + t; Changed(); } }
    }

    // Hour and minute as separate numbers (compact, like the play time).
    public decimal? StartedHour
    {
        get => StartedTime?.Hours;
        set { if (value is { } h && StartedTime is { } t) StartedTime = new TimeSpan((int)Math.Clamp(h, 0, 23), t.Minutes, 0); }
    }

    public decimal? StartedMinute
    {
        get => StartedTime?.Minutes;
        set { if (value is { } m && StartedTime is { } t) StartedTime = new TimeSpan(t.Hours, (int)Math.Clamp(m, 0, 59), 0); }
    }

    public decimal? FameHour
    {
        get => FameTime?.Hours;
        set { if (value is { } h && FameTime is { } t) FameTime = new TimeSpan((int)Math.Clamp(h, 0, 23), t.Minutes, 0); }
    }

    public decimal? FameMinute
    {
        get => FameTime?.Minutes;
        set { if (value is { } m && FameTime is { } t) FameTime = new TimeSpan(t.Hours, (int)Math.Clamp(m, 0, 59), 0); }
    }

    public string LastSavedText => doc.LastSaved is { } d ? string.Format(Strings.Trainer_LastSaved, d) : "";

    // ------------------------------------------------------------------ records

    public ObservableCollection<RecordRowViewModel> Records { get; } = [];

    [ObservableProperty]
    public partial string RecordFilter { get; set; } = "";

    partial void OnRecordFilterChanged(string value) => ApplyRecordFilter();

    private void ApplyRecordFilter()
    {
        string filter = RecordFilter.Trim();
        Records.Clear();
        foreach (var r in allRecords.Where(r => filter.Length == 0
                                               || (int.TryParse(filter.TrimStart('#'), out int n) ? r.Id == n : r.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase))))
            Records.Add(r);
    }

    // ------------------------------------------------------------------ Battle Maison

    public IReadOnlyList<MaisonRowViewModel> Maison { get; }

    // ------------------------------------------------------------------ unlocks

    public decimal? OPowerPoints
    {
        get => doc.OPowerPoints;
        set { if (value is { } v && (int)v != doc.OPowerPoints) { doc.OPowerPoints = (int)v; Changed(); } }
    }

    public string PuffText => string.Format(Strings.Trainer_PuffCount, doc.PokePuffCount);

    [RelayCommand]
    private void UnlockOPowers() => Unlock(doc.UnlockAllOPowers, Strings.Trainer_UnlockOPowers);

    [RelayCommand]
    private void UnlockFriendSafari() => Unlock(doc.UnlockAllFriendSafari, Strings.Trainer_UnlockSafari);

    [RelayCommand]
    private void UnlockFashion() => Unlock(doc.UnlockAllFashion, Strings.Trainer_UnlockFashion);

    [RelayCommand]
    private void UnlockSuperTraining() => Unlock(doc.UnlockAllSuperTraining, Strings.Trainer_UnlockSuperTraining);

    [RelayCommand]
    private void FillPuffs()
    {
        Unlock(doc.FillPokePuffs, Strings.Trainer_FillPuffs);
        OnPropertyChanged(nameof(PuffText));
    }

    [RelayCommand]
    private void UnlockAllBoxes()
    {
        doc.BoxesUnlocked = doc.BoxCount;
        OnPropertyChanged(nameof(BoxesUnlocked));
        Changed();
    }

    private void Unlock(Action action, string what)
    {
        action();
        Changed();
        owner.SetStatus(string.Format(Strings.Trainer_Unlocked, what));
    }

    // ------------------------------------------------------------------ map

    public decimal? Map
    {
        get => doc.Position.Map;
        set { if (value is { } v) SetPosition(p => p with { Map = (int)v }); }
    }

    public decimal? X
    {
        get => (decimal)doc.Position.X;
        set { if (value is { } v) SetPosition(p => p with { X = (float)v }); }
    }

    public decimal? Y
    {
        get => (decimal)doc.Position.Y;
        set { if (value is { } v) SetPosition(p => p with { Y = (float)v }); }
    }

    public decimal? Z
    {
        get => (decimal)doc.Position.Z;
        set { if (value is { } v) SetPosition(p => p with { Z = (float)v }); }
    }

    public decimal? Rotation
    {
        get => doc.Position.Rotation;
        set { if (value is { } v) SetPosition(p => p with { Rotation = (int)v }); }
    }

    private void SetPosition(Func<(int Map, float X, float Y, float Z, int Rotation), (int Map, float X, float Y, float Z, int Rotation)> change)
    {
        var old = doc.Position;
        var p = change(old);
        if (p == old)
            return;
        doc.SetPosition(p.Map, p.X, p.Y, p.Z, p.Rotation);
        Changed();
    }
}

public sealed class BadgeViewModel(SaveTrainerViewModel owner, SaveDocument doc, int index, string name, IconImage? image) : ObservableObject
{
    public string Label => name;

    /// <summary>Badge number, shown on the trainer card when the game images are not available.</summary>
    public string Number => (index + 1).ToString();

    public bool IsChecked
    {
        get => doc.GetBadge(index);
        set
        {
            if (value == doc.GetBadge(index))
                return;
            doc.SetBadge(index, value);
            OnPropertyChanged(nameof(Icon));
            owner.BadgeChanged();
        }
    }

    /// <summary>The game's badge or seal, faded while not earned (as on the trainer card of the project preview).</summary>
    public Avalonia.Media.Imaging.Bitmap? Icon => image is null ? null : PokemonSprites.ToBitmap(IsChecked ? image : MilestoneIcons.Faded(image));

    public bool HasIcon => image is not null;
}

public sealed class SayingViewModel(SaveEditorViewModel owner, SaveDocument doc, int index) : ObservableObject
{
    public string Label => string.Format(Strings.Trainer_Saying, index + 1);

    public string Text
    {
        get => doc.GetSaying(index);
        set { if (value != doc.GetSaying(index) && value.Length <= SaveDocument.MaxSayingLength) { doc.SetSaying(index, value); owner.Touch(); } }
    }
}

public sealed class RecordRowViewModel(SaveEditorViewModel owner, SaveDocument doc, int id, string name) : ObservableObject
{
    public string Name { get; } = name;
    public int Id => id;
    public string Number => $"#{id:000}";
    public int Max => doc.GetRecordMax(id);

    public decimal? Value
    {
        get => doc.GetRecord(id);
        set { if (value is { } v && (int)v != doc.GetRecord(id)) { doc.SetRecord(id, (int)Math.Clamp(v, 0, Max)); owner.Touch(); } }
    }
}

public sealed class MaisonRowViewModel(SaveEditorViewModel owner, SaveDocument doc, BattleStyle6 style) : ObservableObject
{
    public string Style => style switch
    {
        BattleStyle6.Single => Strings.Maison_Single,
        BattleStyle6.Double => Strings.Maison_Double,
        BattleStyle6.Triple => Strings.Maison_Triple,
        BattleStyle6.Rotation => Strings.Maison_Rotation,
        _ => Strings.Maison_Multi,
    };

    public decimal? Current { get => doc.GetMaison(style, true, false); set => Set(true, false, value); }
    public decimal? Best { get => doc.GetMaison(style, false, false); set => Set(false, false, value); }
    public decimal? SuperCurrent { get => doc.GetMaison(style, true, true); set => Set(true, true, value); }
    public decimal? SuperBest { get => doc.GetMaison(style, false, true); set => Set(false, true, value); }

    private void Set(bool current, bool super, decimal? value)
    {
        if (value is { } v && (int)v != doc.GetMaison(style, current, super))
        {
            doc.SetMaison(style, current, super, (int)v);
            owner.Touch();
        }
    }
}
