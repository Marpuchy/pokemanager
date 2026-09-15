using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pokemanager.App.Resources;
using Pokemanager.Battle;
using Pokemanager.Multiplayer;

namespace Pokemanager.App.ViewModels;

/// <summary>
/// A battle of the local player in the room: the rules both players agree on before it starts (either can change them,
/// which asks both to get ready again), who is ready, and the way to its battle screen.
/// </summary>
public sealed partial class RoomBattleCard : ObservableObject
{
    private readonly RoomViewModel owner;
    private bool updating;

    public RoomBattleCard(RoomViewModel owner, RoomBattle battle)
    {
        this.owner = owner;
        Battle = battle;
        Update(battle);
    }

    public RoomBattle Battle { get; private set; }
    public string Id => Battle.Id;

    [ObservableProperty]
    public partial string Title { get; private set; } = "";

    [ObservableProperty]
    public partial string ReadyText { get; private set; } = "";

    [ObservableProperty]
    public partial string ProblemsText { get; private set; } = "";

    [ObservableProperty]
    public partial AvatarViewModel? OpponentAvatar { get; private set; }

    public bool HasProblems => ProblemsText.Length > 0;
    public bool IsProposed => Battle.Phase == BattlePhase.Proposed;
    public bool IsRunning => Battle.Phase == BattlePhase.Running;
    public bool IsOver => !Battle.IsActive;
    public bool CanOpen => Battle.Phase != BattlePhase.Proposed && Battle.Log.Count > 0;
    public bool IAmReady => Battle.Ready.Contains(owner.LocalPlayerId);
    public bool IAmNotReady => IsProposed && !IAmReady;
    public string CancelText => IsRunning ? Strings.Battle_Forfeit : Strings.Battle_Cancel;

    /// <summary>The room sent a new state of this battle.</summary>
    public void Update(RoomBattle battle)
    {
        Battle = battle;
        string opponentId = battle.MySide == "p2" ? battle.ChallengerId : battle.OpponentId;
        var opponent = owner.FindPlayer(opponentId);
        string name = opponent?.Name ?? "?";
        if (OpponentAvatar is null && opponent is not null)
            OpponentAvatar = new AvatarViewModel(opponent.Profile);

        Title = battle.Phase switch
        {
            BattlePhase.Proposed when battle.MySide == "p2" => string.Format(Strings.Battle_FromThem, name),
            BattlePhase.Proposed => string.Format(Strings.Battle_ToThem, name),
            BattlePhase.Running => string.Format(Strings.Battle_Running, name),
            BattlePhase.Finished when battle.Winner is null => string.Format(Strings.Battle_Tie, name),
            BattlePhase.Finished when battle.Winner == owner.LocalPlayerId => string.Format(Strings.Battle_Won, name),
            BattlePhase.Finished => string.Format(Strings.Battle_Lost, name),
            _ => string.Format(Strings.Battle_Cancelled, name),
        };
        string State(string id) => battle.Ready.Contains(id) ? Strings.Battle_IsReady : Strings.Battle_NotReady;
        ReadyText = string.Format(Strings.Battle_ReadyState, State(owner.LocalPlayerId), name, State(opponentId));
        ProblemsText = string.Join("\n", battle.Problems);

        updating = true;
        try
        {
            var r = battle.Rules;
            GenerationIndex = r.Generation == 6 ? 0 : 1;
            LevelIndex = r.Level switch { 50 => 1, 100 => 2, _ => 0 };
            HealBefore = r.HealBefore;
            TeamPreview = r.TeamPreview;
            SleepClause = r.SleepClause;
            SpeciesClause = r.SpeciesClause;
            ItemClause = r.ItemClause;
            OhkoClause = r.OhkoClause;
            EvasionClause = r.EvasionClause;
            BatonPassClause = r.BatonPassClause;
            NoMegas = r.NoMegas;
            NoZMoves = r.NoZMoves;
        }
        finally
        {
            updating = false;
        }
        foreach (string property in new[]
                 {
                     nameof(HasProblems), nameof(IsProposed), nameof(IsRunning), nameof(IsOver), nameof(CanOpen), nameof(IAmReady),
                     nameof(IAmNotReady), nameof(CancelText),
                 })
            OnPropertyChanged(property);
    }

    // ------------------------------------------------------------------ rules (two-way)

    [ObservableProperty] public partial int GenerationIndex { get; set; }
    [ObservableProperty] public partial int LevelIndex { get; set; }
    [ObservableProperty] public partial bool HealBefore { get; set; }
    [ObservableProperty] public partial bool TeamPreview { get; set; }
    [ObservableProperty] public partial bool SleepClause { get; set; }
    [ObservableProperty] public partial bool SpeciesClause { get; set; }
    [ObservableProperty] public partial bool ItemClause { get; set; }
    [ObservableProperty] public partial bool OhkoClause { get; set; }
    [ObservableProperty] public partial bool EvasionClause { get; set; }
    [ObservableProperty] public partial bool BatonPassClause { get; set; }
    [ObservableProperty] public partial bool NoMegas { get; set; }
    [ObservableProperty] public partial bool NoZMoves { get; set; }

    private BattleRules EditedRules() => new(
        Generation: GenerationIndex == 0 ? 6 : 7,
        Level: LevelIndex switch { 1 => 50, 2 => 100, _ => 0 },
        HealBefore: HealBefore, TeamPreview: TeamPreview, SleepClause: SleepClause, SpeciesClause: SpeciesClause,
        ItemClause: ItemClause, OhkoClause: OhkoClause, EvasionClause: EvasionClause, BatonPassClause: BatonPassClause,
        NoMegas: NoMegas, NoZMoves: NoZMoves);

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (updating || !IsProposed)
            return;
        if (e.PropertyName is nameof(GenerationIndex) or nameof(LevelIndex) or nameof(HealBefore) or nameof(TeamPreview)
            or nameof(SleepClause) or nameof(SpeciesClause) or nameof(ItemClause) or nameof(OhkoClause) or nameof(EvasionClause)
            or nameof(BatonPassClause) or nameof(NoMegas) or nameof(NoZMoves)
            && EditedRules() is var rules && rules != Battle.Rules)
            owner.SetBattleRules(Id, rules);
    }

    // ------------------------------------------------------------------ actions

    [RelayCommand]
    private Task Ready() => owner.GetReadyAsync(this);

    [RelayCommand]
    private void NotReady() => owner.SetNotReady(Id);

    [RelayCommand]
    private void Cancel() => owner.CancelBattle(Id);

    [RelayCommand]
    private void Open() => owner.OpenBattle(Id);

    [RelayCommand]
    private void Dismiss() => owner.DismissBattle(Id);
}
