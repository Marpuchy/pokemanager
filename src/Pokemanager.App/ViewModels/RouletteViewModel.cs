using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pokemanager.App.Controls;
using Pokemanager.App.Resources;
using Pokemanager.App.Services;
using Pokemanager.Model.Projects;

namespace Pokemanager.App.ViewModels;

/// <summary>
/// The badge roulette. The prize is drawn (by weight) when spinning starts and recorded in the project as soon as the
/// wheel stops, so closing the window does not allow another try. Lives and bad luck apply at once; items and money are
/// put into the save with "Claim", which can be retried later if it fails (for example, with the emulator open).
/// </summary>
public sealed partial class RouletteViewModel : ObservableObject
{
    private static readonly Color[] Palette =
    [
        Color.Parse("#E74C3C"), Color.Parse("#3498DB"), Color.Parse("#F1C40F"), Color.Parse("#2ECC71"), Color.Parse("#9B59B6"),
        Color.Parse("#E67E22"), Color.Parse("#1ABC9C"), Color.Parse("#EC407A"), Color.Parse("#95A5A6"), Color.Parse("#34495E"),
    ];

    private readonly LockeSettings locke;
    private readonly IReadOnlyList<string> itemNames;
    private readonly Action<LockePrize> onWin;
    private readonly Func<LockePrize, (bool Ok, string Message)> claim;
    private readonly Action markAdded;
    private LockePrize? won;

    public string BadgeName { get; }
    public string Title => string.Format(Strings.Locke_RouletteTitle, BadgeName);
    public IReadOnlyList<WheelSegment> Segments { get; }

    /// <summary>Winning segment, chosen when spinning starts; -1 before (or for a pending prize no longer on the wheel).</summary>
    public int WinnerIndex { get; private set; } = -1;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SpinCommand), nameof(ClaimCommand), nameof(AlreadyAddedCommand))]
    public partial RouletteState State { get; set; } = RouletteState.Ready;

    [ObservableProperty]
    public partial string ResultText { get; set; } = "";

    [ObservableProperty]
    public partial bool ResultIsError { get; set; }

    /// <summary>Something was given (to the save or the project): the preview should reload when the window closes.</summary>
    public bool Changed { get; private set; }

    /// <summary>Raised when a spin starts; the view animates the wheel and then calls <see cref="SpinFinished"/>.</summary>
    public event EventHandler<int>? SpinRequested;

    /// <param name="pending">A prize already won for this badge but not yet put into the save.</param>
    /// <param name="markAdded">The player gave the prize by hand: it counts as given without writing to the save.</param>
    public RouletteViewModel(string badgeName, LockeSettings locke, IReadOnlyList<string> itemNames, LockePrize? pending,
        Action<LockePrize> onWin, Func<LockePrize, (bool Ok, string Message)> claim, Action markAdded)
    {
        BadgeName = badgeName;
        this.locke = locke;
        this.itemNames = itemNames;
        this.onWin = onWin;
        this.claim = claim;
        this.markAdded = markAdded;
        Segments = locke.Prizes
            .Select((p, i) => new WheelSegment(LockeRewards.Describe(p, itemNames), Math.Max(0, p.Weight), Palette[i % Palette.Length],
                LockeRewards.Icon(p), LockeRewards.Glyph(p)))
            .ToList();

        if (pending is not null)
        {
            won = pending;
            WinnerIndex = locke.Prizes.IndexOf(pending);
            ResultText = string.Format(Strings.Locke_Pending, LockeRewards.Describe(pending, itemNames));
            State = RouletteState.Won;
        }
    }

    private bool CanSpin() => State == RouletteState.Ready && Segments.Any(s => s.Weight > 0);

    [RelayCommand(CanExecute = nameof(CanSpin))]
    private void Spin()
    {
        WinnerIndex = locke.Roll(Random.Shared);
        if (WinnerIndex < 0)
            return;
        State = RouletteState.Spinning;
        SpinRequested?.Invoke(this, WinnerIndex);
    }

    /// <summary>The wheel stopped: record the prize; lives and bad luck are done, items and money wait for Claim.</summary>
    public void SpinFinished()
    {
        if (State != RouletteState.Spinning)
            return;
        won = locke.Prizes[WinnerIndex];
        onWin(won);
        Changed = true;
        string label = LockeRewards.Describe(won, itemNames);
        if (won.Kind is LockePrizeKind.Item or LockePrizeKind.Money or LockePrizeKind.Text)
        {
            ResultText = string.Format(Strings.Locke_YouWon, label);
            State = RouletteState.Won;
        }
        else
        {
            ResultText = won.Kind == LockePrizeKind.Life ? Strings.Locke_ClaimedLife : Strings.Locke_ClaimedNothing;
            State = RouletteState.Claimed;
        }
    }

    /// <summary>Only what the app can write into the save; a free-text prize is always given by hand.</summary>
    private bool CanClaim() => State == RouletteState.Won && won is not null && won.Kind is LockePrizeKind.Item or LockePrizeKind.Money;

    /// <summary>Any won prize can be marked as given by hand (an item the player put in the game themselves, a text prize).</summary>
    private bool CanMarkAdded() => State == RouletteState.Won && won is not null;

    [RelayCommand(CanExecute = nameof(CanMarkAdded))]
    private void AlreadyAdded()
    {
        if (!CanMarkAdded())
            return;
        markAdded();
        Changed = true;
        ResultText = string.Format(Strings.Locke_MarkedAdded, LockeRewards.Describe(won!, itemNames));
        ResultIsError = false;
        State = RouletteState.Claimed;
    }

    [RelayCommand(CanExecute = nameof(CanClaim))]
    private void Claim()
    {
        if (!CanClaim())
            return;
        var (ok, message) = claim(won!);
        ResultText = message;
        ResultIsError = !ok;
        if (ok)
        {
            Changed = true;
            State = RouletteState.Claimed;
        }
    }
}

public enum RouletteState
{
    Ready,
    Spinning,
    Won,
    Claimed,
}
