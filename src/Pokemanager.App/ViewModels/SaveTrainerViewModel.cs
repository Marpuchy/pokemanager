using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Core;
using Pokemanager.App.Resources;
using Pokemanager.App.Services;
using Pokemanager.Save;

namespace Pokemanager.App.ViewModels;

/// <summary>Trainer data, following PKHeX's trainer editor for X/Y.</summary>
public partial class SaveTrainerViewModel : ObservableObject
{
    private readonly SaveEditorViewModel owner;
    private readonly SaveDocument doc;
    private readonly List<RecordRowViewModel> allRecords;

    public SaveTrainerViewModel(SaveEditorViewModel owner, SaveDocument doc, SaveNames names)
    {
        this.owner = owner;
        this.doc = doc;
        VivillonPatterns = names.VivillonPatterns;
        Badges = Enumerable.Range(0, 8).Select(i => new BadgeViewModel(owner, doc, i)).ToList();
        Sayings = Enumerable.Range(0, 5).Select(i => new SayingViewModel(owner, doc, i)).ToList();
        Maison = SaveDocument.MaisonStyles.Select(s => new MaisonRowViewModel(owner, doc, s)).ToList();
        allRecords = SaveDocument.RecordNames.Select(r => new RecordRowViewModel(owner, doc, r.Id, r.Name)).ToList();
        ApplyRecordFilter();
    }

    private void Changed() => owner.Touch();

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

public sealed class BadgeViewModel(SaveEditorViewModel owner, SaveDocument doc, int index) : ObservableObject
{
    public string Label => string.Format(Strings.Trainer_Badge, index + 1);

    public bool IsChecked
    {
        get => doc.GetBadge(index);
        set { if (value != doc.GetBadge(index)) { doc.SetBadge(index, value); owner.Touch(); } }
    }
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
