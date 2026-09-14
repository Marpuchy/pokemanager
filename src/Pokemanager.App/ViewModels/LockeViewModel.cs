using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pokemanager.App.Resources;
using Pokemanager.App.Services;
using Pokemanager.Model.Dump;
using Pokemanager.Model.Projects;

namespace Pokemanager.App.ViewModels;

/// <summary>A prize of the badge roulette, editable.</summary>
public sealed partial class PrizeRowViewModel(LockeViewModel owner, LockePrize prize) : ObservableObject
{
    public LockePrize Prize { get; private set; } = prize;

    public static IReadOnlyList<string> KindNames { get; } =
        [Strings.Locke_KindItem, Strings.Locke_KindMoney, Strings.Locke_KindLife, Strings.Locke_KindNothing];

    public IReadOnlyList<string> ItemNames => owner.ItemNames;

    public int KindIndex
    {
        get => (int)Prize.Kind;
        set
        {
            if (value < 0 || value >= KindNames.Count || value == (int)Prize.Kind)
                return;
            var kind = (LockePrizeKind)value;
            Update(Prize with
            {
                Kind = kind,
                ItemId = kind == LockePrizeKind.Item ? Math.Max(1, Prize.ItemId) : 0,
                Amount = kind switch { LockePrizeKind.Money => Math.Max(1000, Prize.Amount), LockePrizeKind.Item => Math.Clamp(Prize.Amount, 1, 99), _ => 1 },
            });
        }
    }

    public int ItemIndex
    {
        get => Prize.ItemId;
        set { if (value > 0 && value < ItemNames.Count && value != Prize.ItemId) Update(Prize with { ItemId = value }); }
    }

    public decimal? Amount
    {
        get => Prize.Amount;
        set { if (value is { } v && (int)v != Prize.Amount) Update(Prize with { Amount = (int)Math.Max(1, v) }); }
    }

    public decimal? Weight
    {
        get => Prize.Weight;
        set { if (value is { } v && (int)v != Prize.Weight) Update(Prize with { Weight = (int)Math.Max(0, v) }); }
    }

    public bool IsItem => Prize.Kind == LockePrizeKind.Item;
    public bool HasAmount => Prize.Kind is LockePrizeKind.Item or LockePrizeKind.Money;

    public string ChanceText => owner.ChanceOf(this);

    private void Update(LockePrize updated)
    {
        Prize = updated;
        OnPropertyChanged(string.Empty);
        owner.Commit();
    }

    public void RefreshChance() => OnPropertyChanged(nameof(ChanceText));

    [RelayCommand]
    private void Remove() => owner.Remove(this);
}

/// <summary>A badge already spun.</summary>
public sealed partial class SpinRowViewModel(LockeViewModel owner, LockeSpin spin, string badgeName, string prizeText)
{
    public string Badge { get; } = badgeName;
    public string PrizeText { get; } = prizeText;
    public string When { get; } = spin.When.ToString("g");
    public string StateText { get; } = spin.Claimed ? Strings.Locke_StateClaimed : Strings.Locke_StatePending;

    [RelayCommand]
    private void Forget() => owner.ForgetSpin(spin.Badge);
}

/// <summary>Locke tab of the editor: lives of the run and the badge roulette's prizes.</summary>
public sealed partial class LockeViewModel : ObservableObject
{
    private readonly EditorViewModel editor;

    private LockeSettings Locke => editor.Session.Project.Locke;

    public IReadOnlyList<string> ItemNames => editor.Names.Items;

    public ObservableCollection<PrizeRowViewModel> Prizes { get; } = [];
    public ObservableCollection<SpinRowViewModel> Spins { get; } = [];

    public LockeViewModel(EditorViewModel editor)
    {
        this.editor = editor;
        Refresh();
    }

    public void Refresh()
    {
        Prizes.Clear();
        foreach (var prize in Locke.Prizes)
            Prizes.Add(new PrizeRowViewModel(this, prize));
        RefreshSpins();
        OnPropertyChanged(string.Empty);
    }

    private void RefreshSpins()
    {
        Spins.Clear();
        foreach (var spin in Locke.Spins.OrderBy(s => s.Badge))
            Spins.Add(new SpinRowViewModel(this, spin, LockeRewards.MilestoneName(Game, spin.Badge),
                LockeRewards.Describe(spin.Prize, ItemNames)));
        OnPropertyChanged(nameof(HasSpins));
    }

    public bool HasSpins => Spins.Count > 0;

    private GameTitle Game => editor.Session.Project.Game;

    /// <summary>Badges (Gen 6) or trials (Gen 7) of the game: the roulette interval goes up to this.</summary>
    public int MilestoneCount => Game.Milestones().Count;

    public string IntervalHint => Game.HasBadges() ? Strings.Locke_IntervalHint : Strings.Locke_IntervalHintTrials;
    public string IntervalFirstLabel => Game.HasBadges() ? Strings.Locke_IntervalFirst : Strings.Locke_IntervalFirstTrial;
    public string IntervalLastLabel => Game.HasBadges() ? Strings.Locke_IntervalLast : Strings.Locke_IntervalLastTrial;
    public string IntervalEveryLabel => Game.HasBadges() ? Strings.Locke_IntervalEvery : Strings.Locke_IntervalEveryTrials;

    public decimal? MaxLives
    {
        get => Locke.MaxLives;
        set
        {
            if (value is not { } v || (int)v == Locke.MaxLives)
                return;
            Locke.MaxLives = (int)Math.Clamp(v, 0, 99);
            Locke.LivesLost = Math.Min(Locke.LivesLost, Locke.MaxLives);
            LivesChanged();
        }
    }

    public decimal? LivesLost
    {
        get => Locke.LivesLost;
        set
        {
            if (value is not { } v || (int)v == Locke.LivesLost)
                return;
            Locke.LivesLost = (int)Math.Clamp(v, 0, Locke.MaxLives);
            LivesChanged();
        }
    }

    public string LivesText => Locke.MaxLives == 0
        ? Strings.Locke_NoLives
        : new string('♥', Locke.LivesLeft) + new string('♡', Locke.LivesLost) + "  " + string.Format(Strings.Locke_Lives, Locke.LivesLeft, Locke.MaxLives);

    private void LivesChanged()
    {
        OnPropertyChanged(nameof(MaxLives));
        OnPropertyChanged(nameof(LivesLost));
        OnPropertyChanged(nameof(LivesText));
        editor.MarkDirty();
    }

    // ------------------------------------------------------------------ roulette interval

    public decimal? RouletteFirst
    {
        get => Locke.RouletteFirst;
        set { if (value is { } v && (int)v != Locke.RouletteFirst) { Locke.RouletteFirst = (int)Math.Clamp(v, 1, MilestoneCount); IntervalChanged(); } }
    }

    public decimal? RouletteLast
    {
        get => Math.Min(Locke.RouletteLast, MilestoneCount);
        set { if (value is { } v && (int)v != Locke.RouletteLast) { Locke.RouletteLast = (int)Math.Clamp(v, 1, MilestoneCount); IntervalChanged(); } }
    }

    public decimal? RouletteEvery
    {
        get => Locke.RouletteEvery;
        set { if (value is { } v && (int)v != Locke.RouletteEvery) { Locke.RouletteEvery = (int)Math.Clamp(v, 1, MilestoneCount); IntervalChanged(); } }
    }

    /// <summary>Which badges get a roulette with the current interval.</summary>
    public string RouletteBadgesText
    {
        get
        {
            var milestones = Game.Milestones();
            var names = Enumerable.Range(0, milestones.Count).Where(Locke.HasRoulette).Select(i => milestones[i].Name).ToList();
            return names.Count == 0 ? Strings.Locke_IntervalNone : string.Format(Strings.Locke_IntervalBadges, string.Join(", ", names));
        }
    }

    private void IntervalChanged()
    {
        OnPropertyChanged(nameof(RouletteFirst));
        OnPropertyChanged(nameof(RouletteLast));
        OnPropertyChanged(nameof(RouletteEvery));
        OnPropertyChanged(nameof(RouletteBadgesText));
        editor.MarkDirty();
    }

    public string ChanceOf(PrizeRowViewModel row)
    {
        int total = Prizes.Sum(p => Math.Max(0, p.Prize.Weight));
        return total == 0 ? "—" : $"{100.0 * Math.Max(0, row.Prize.Weight) / total:0.#} %";
    }

    /// <summary>Writes the rows back to the project.</summary>
    public void Commit()
    {
        Locke.Prizes = Prizes.Select(p => p.Prize).ToList();
        foreach (var row in Prizes)
            row.RefreshChance();
        editor.MarkDirty();
    }

    public void Remove(PrizeRowViewModel row)
    {
        Prizes.Remove(row);
        Commit();
    }

    [RelayCommand]
    private void AddPrize()
    {
        Prizes.Add(new PrizeRowViewModel(this, new LockePrize(LockePrizeKind.Item, ItemId: 50, Amount: 1, Weight: 1)));
        Commit();
    }

    [RelayCommand]
    private void ResetPrizes()
    {
        Locke.Prizes = LockeSettings.DefaultPrizes();
        Prizes.Clear();
        foreach (var prize in Locke.Prizes)
            Prizes.Add(new PrizeRowViewModel(this, prize));
        editor.MarkDirty();
    }

    public void ForgetSpin(int badge)
    {
        Locke.ForgetSpin(badge);
        RefreshSpins();
        editor.MarkDirty();
    }
}
