using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Pokemanager.App.Resources;
using Pokemanager.Battle;
using Pokemanager.Model.Dump;
using Pokemanager.Multiplayer;

namespace Pokemanager.App.ViewModels;

/// <summary>Battles between the players of the room: challenge, rules, ready with the party, and the battle screen.</summary>
public sealed partial class RoomViewModel
{
    /// <summary>ROM type of each move of the team sent for a battle (the battle screen shows those types).</summary>
    private readonly Dictionary<string, IReadOnlyDictionary<int, int>> battleMoveTypes = [];
    private readonly Dictionary<string, BattleViewModel> battleWindows = [];
    private readonly HashSet<string> battlesOpenedOnce = [];

    public ObservableCollection<RoomBattleCard> BattleCards { get; } = [];

    public string LocalPlayerId => Memory.PlayerId;

    public RoomPlayer? FindPlayer(string id) => session?.Players.FirstOrDefault(p => p.Id == id);

    /// <summary>The selected player can be challenged: someone else, online, not already in a battle with the local player.</summary>
    public bool CanChallenge => session is not null && SelectedPlayer?.Player is { IsLocal: false, Online: true } other
                                && !BattleCards.Any(c => c.Battle.IsActive && (c.Battle.ChallengerId == other.Id || c.Battle.OpponentId == other.Id));

    public string ChallengeText => SelectedPlayer?.Player is { IsLocal: false } other
        ? string.Format(Strings.Battle_Challenge, other.Name)
        : Strings.Battle_ChallengePick;

    [RelayCommand]
    private void Challenge()
    {
        if (session is null || !CanChallenge || SelectedPlayer?.Player is not { } opponent)
            return;
        // Mechanics of the newest game of the two; everything else as Showdown's usual singles.
        int mine = session.Players.FirstOrDefault(p => p.IsLocal)?.Snapshot?.Generation ?? shared?.Dump.Title.Generation() ?? 7;
        int theirs = opponent.Snapshot?.Generation ?? mine;
        session.Challenge(opponent.Id, new BattleRules(Generation: Math.Clamp(Math.Max(mine, theirs), 6, 7)));
        Say(string.Format(Strings.Battle_ChallengeSent, opponent.Name));
    }

    public void SetBattleRules(string battleId, BattleRules rules) => session?.SetBattleRules(battleId, rules);

    /// <summary>Ready with the party of the shared save, read now (what the game saved last).</summary>
    public async Task GetReadyAsync(RoomBattleCard card)
    {
        if (session is null)
            return;
        var save = await Task.Run(OpenSave);
        if (save is null)
        {
            Say(Strings.Room_NoSave, error: true);
            return;
        }
        var team = BattleTeam.FromParty(save, ProfileName);
        if (team.Mons.Count == 0)
        {
            Say(Strings.Battle_NoParty, error: true);
            return;
        }
        battleMoveTypes[card.Id] = team.Moves.ToDictionary(m => m.Id, m => m.Type);
        session.SetBattleReady(card.Id, team);
    }

    public void SetNotReady(string battleId) => session?.SetBattleReady(battleId, null);

    /// <summary>Cancels a proposed battle; a running one is forfeited after asking.</summary>
    public void CancelBattle(string battleId)
    {
        if (session?.Battles.FirstOrDefault(b => b.Id == battleId) is not { } battle)
            return;
        if (battle.Phase == BattlePhase.Running)
            ForfeitBattle(battleId, ask: true);
        else
            session.CancelBattle(battleId);
    }

    public async void ForfeitBattle(string battleId, bool ask)
    {
        if (session is null)
            return;
        if (ask && !await main.Dialogs.AskAsync(new QuestionViewModel(Strings.Battle_Forfeit, Strings.Battle_ForfeitConfirm,
                Strings.Battle_Forfeit, Strings.Common_Cancel, [])))
            return;
        session.CancelBattle(battleId);
    }

    /// <summary>From the battle screen, which already asked.</summary>
    public void ForfeitBattle(string battleId) => ForfeitBattle(battleId, ask: false);

    public void ChooseInBattle(string battleId, string choice) => session?.ChooseInBattle(battleId, choice);

    public void DismissBattle(string battleId) => session?.DismissBattle(battleId);

    public void OpenBattle(string battleId)
    {
        if (battleWindows.TryGetValue(battleId, out var open))
        {
            open.Activate();
            return;
        }
        if (session?.Battles.FirstOrDefault(b => b.Id == battleId) is not { } battle)
            return;
        string me = ProfileName;
        string opponent = FindPlayer(battle.MySide == "p2" ? battle.ChallengerId : battle.OpponentId)?.Name ?? "?";
        var vm = new BattleViewModel(this, battle, string.Format(Strings.Battle_WindowTitle, me, opponent),
            battleMoveTypes.GetValueOrDefault(battleId) ?? new Dictionary<int, int>());
        battleWindows[battleId] = vm;
        vm.Closed += () => battleWindows.Remove(battleId);
        main.Dialogs.ShowBattle(vm);
    }

    /// <summary>What the battle screen says at the end.</summary>
    public string BattleResultText(RoomBattle battle) => battle.Phase switch
    {
        BattlePhase.Finished when battle.Winner is null => Strings.Battle_ResultTie,
        BattlePhase.Finished when battle.Winner == LocalPlayerId => Strings.Battle_ResultWon,
        BattlePhase.Finished => string.Format(Strings.Battle_ResultLost, FindPlayer(battle.Winner)?.Name ?? "?"),
        _ => Strings.Battle_ResultCancelled,
    };

    private void RefreshBattles(RoomSession s)
    {
        var battles = s.Battles;
        foreach (var card in BattleCards.Where(c => battles.All(b => b.Id != c.Id)).ToList())
            BattleCards.Remove(card);
        for (int i = 0; i < battles.Count; i++)
        {
            var battle = battles[i];
            var card = BattleCards.FirstOrDefault(c => c.Id == battle.Id);
            if (card is null)
                BattleCards.Insert(Math.Min(i, BattleCards.Count), new RoomBattleCard(this, battle));
            else
                card.Update(battle);

            if (battleWindows.TryGetValue(battle.Id, out var window))
                window.Update(battle);
            // The battle screen opens by itself when a battle starts.
            else if (battle.Phase == BattlePhase.Running && battlesOpenedOnce.Add(battle.Id))
                OpenBattle(battle.Id);
        }
        OnPropertyChanged(nameof(HasBattles));
        OnPropertyChanged(nameof(CanChallenge));
        OnPropertyChanged(nameof(ChallengeText));
    }

    public bool HasBattles => BattleCards.Count > 0;
}
