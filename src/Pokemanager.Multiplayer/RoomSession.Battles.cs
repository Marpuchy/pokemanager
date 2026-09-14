using LiteNetLib;
using Pokemanager.Battle;

namespace Pokemanager.Multiplayer;

/// <summary>A battle as a player of the room sees it.</summary>
/// <param name="MySide">"p1" (challenger) or "p2" (opponent) for the local player, null when watching.</param>
/// <param name="Log">Showdown protocol lines received so far, reduced to what this player may see.</param>
/// <param name="Request">What the local player has to decide now (Showdown's request JSON), or null.</param>
public sealed record RoomBattle(
    string Id, string ChallengerId, string OpponentId, BattleRules Rules, IReadOnlyList<string> Ready, BattlePhase Phase,
    string? Winner, IReadOnlyList<string> Problems, IReadOnlyList<string> Log, string? Request, string? MySide)
{
    public bool IsActive => Phase is BattlePhase.Proposed or BattlePhase.Running;
}

public sealed partial class RoomSession
{
    /// <summary>A battle run by this app as the host.</summary>
    private sealed class HostedBattle(BattleState state)
    {
        public BattleState State { get; set; } = state;
        public Dictionary<string, BattleTeam> Teams { get; } = [];
        public ShowdownBattle? Simulator { get; set; }

        public string? SideOf(string playerId) =>
            playerId == State.ChallengerId ? "p1" : playerId == State.OpponentId ? "p2" : null;

        public string PlayerOf(string side) => side == "p1" ? State.ChallengerId : State.OpponentId;
    }

    private readonly Dictionary<string, HostedBattle> hosted = [];
    private readonly Dictionary<string, RoomBattle> battles = [];

    /// <summary>Battles the local player takes part in, newest first.</summary>
    public IReadOnlyList<RoomBattle> Battles
    {
        get { lock (gate) return battles.Values.Reverse().ToList(); }
    }

    // ------------------------------------------------------------------ player actions (any thread)

    /// <summary>Challenges another player with a first proposal of rules. Returns the battle id.</summary>
    public string Challenge(string opponentId, BattleRules rules)
    {
        string id = Guid.NewGuid().ToString("N")[..12];
        ToHost(new BattleChallenge(id, opponentId, rules));
        return id;
    }

    /// <summary>Proposes other rules for a battle not started yet (both players have to get ready again).</summary>
    public void SetBattleRules(string battleId, BattleRules rules) => ToHost(new BattleRulesSet(battleId, rules));

    /// <summary>Ready with this team, or not ready any more (null).</summary>
    public void SetBattleReady(string battleId, BattleTeam? team) => ToHost(new BattleReady(battleId, team));

    /// <summary>Cancels a proposed battle or forfeits a running one.</summary>
    public void CancelBattle(string battleId) => ToHost(new BattleCancel(battleId));

    /// <summary>A decision for the current request ("move 1", "switch 2", "move 3 mega", "team 213456"…).</summary>
    public void ChooseInBattle(string battleId, string choice)
    {
        lock (gate)
        {
            if (battles.TryGetValue(battleId, out var battle))
                battles[battleId] = battle with { Request = null };
        }
        ToHost(new BattleChoice(battleId, choice));
    }

    /// <summary>Forgets a finished or cancelled battle.</summary>
    public void DismissBattle(string battleId) => work.Enqueue(() =>
    {
        lock (gate)
        {
            if (battles.TryGetValue(battleId, out var battle) && !battle.IsActive)
                battles.Remove(battleId);
        }
        RaiseChanged();
    });

    private void ToHost(RoomMessage message) => work.Enqueue(() =>
    {
        if (IsHost)
            HostBattleMessage(LocalPlayerId, message);
        else if (hostPeer is { } peer)
            Send(peer, message);
    });

    // ------------------------------------------------------------------ host

    private void HostBattleMessage(string from, RoomMessage message)
    {
        switch (message)
        {
            case BattleChallenge challenge:
                StartProposal(from, challenge);
                break;
            case BattleRulesSet set when hosted.TryGetValue(set.BattleId, out var battle) && battle.SideOf(from) is not null
                                        && battle.State.Phase == BattlePhase.Proposed:
                battle.Teams.Clear();
                Publish(battle, battle.State with { Rules = set.Rules, Ready = [], Problems = null });
                break;
            case BattleReady ready when hosted.TryGetValue(ready.BattleId, out var battle) && battle.SideOf(from) is not null
                                       && battle.State.Phase == BattlePhase.Proposed:
                if (ready.Team is null)
                    battle.Teams.Remove(from);
                else
                    battle.Teams[from] = ready.Team;
                Publish(battle, battle.State with { Ready = [.. battle.Teams.Keys], Problems = null });
                if (battle.Teams.Count == 2)
                    StartSimulator(battle);
                break;
            case BattleCancel cancel when hosted.TryGetValue(cancel.BattleId, out var battle) && battle.SideOf(from) is { } side:
                if (battle.State.Phase == BattlePhase.Proposed)
                    Finish(battle, BattlePhase.Cancelled, winner: null, problems: null);
                else if (battle.State.Phase == BattlePhase.Running)
                    battle.Simulator?.Forfeit(side);
                break;
            case BattleChoice choice when hosted.TryGetValue(choice.BattleId, out var battle) && battle.SideOf(from) is { } side
                                         && battle.State.Phase == BattlePhase.Running:
                battle.Simulator?.Choose(side, choice.Choice);
                break;
        }
    }

    private void StartProposal(string from, BattleChallenge challenge)
    {
        List<string>? problem = null;
        bool Online(string id) { lock (gate) return players.TryGetValue(id, out var p) && p.Online; }
        if (options.Battles is null)
            problem = ["The host of the room cannot run battles (the battle simulator is not installed)."];
        else if (from == challenge.OpponentId || !Online(challenge.OpponentId))
            problem = ["That player is not in the room."];
        else if (hosted.Values.Any(b => b.State.Phase is BattlePhase.Proposed or BattlePhase.Running
                                        && (b.SideOf(from) is not null || b.SideOf(challenge.OpponentId) is not null)))
            problem = ["One of the players is already in a battle."];

        var state = new BattleState(challenge.BattleId, from, challenge.OpponentId, challenge.Rules, [],
            problem is null ? BattlePhase.Proposed : BattlePhase.Cancelled, null, problem);
        if (problem is not null)
        {
            Deliver(from, state);
            return;
        }
        var battle = new HostedBattle(state);
        hosted[challenge.BattleId] = battle;
        Publish(battle, state);
    }

    private void StartSimulator(HostedBattle battle)
    {
        var state = battle.State;
        try
        {
            var simulator = ShowdownBattle.Start(options.Battles!, state.Rules, battle.Teams[state.ChallengerId], battle.Teams[state.OpponentId]);
            battle.Simulator = simulator;
            simulator.Received += message => work.Enqueue(() => OnSimulator(battle, message));
            Publish(battle, state with { Phase = BattlePhase.Running });
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            Finish(battle, BattlePhase.Cancelled, null, [ex.Message]);
        }
    }

    private void OnSimulator(HostedBattle battle, ShowdownMessage message)
    {
        var state = battle.State;
        switch (message.Type)
        {
            case "update":
                foreach (var (side, player) in new[] { ("p1", state.ChallengerId), ("p2", state.OpponentId) })
                    Deliver(player, new BattleLog(state.BattleId, ShowdownProtocol.ForSide(message.Lines, side)));
                break;
            case "side" when message.Side is "p1" or "p2":
                string owner = battle.PlayerOf(message.Side);
                foreach (string line in message.Lines)
                {
                    if (line.StartsWith("|request|", StringComparison.Ordinal))
                        Deliver(owner, new BattleRequest(state.BattleId, line[9..]));
                    else
                        Deliver(owner, new BattleLog(state.BattleId, [line]));
                }
                break;
            case "end" when state.Phase == BattlePhase.Running:
                string? winner = message.Winner is null ? null
                    : battle.Teams.FirstOrDefault(t => t.Value.Player == message.Winner).Key;
                Finish(battle, BattlePhase.Finished, winner, null);
                break;
            case "invalid":
                // A team breaks the rules: back to the proposal, not ready, with Showdown's reasons.
                var problems = (message.Teams ?? []).SelectMany(t =>
                    t.Problems.Select(p => $"{PlayerName(battle.PlayerOf(t.Side))}: {p}")).ToList();
                DisposeSimulator(battle);
                battle.Teams.Clear();
                Publish(battle, state with { Phase = BattlePhase.Proposed, Ready = [], Problems = problems });
                break;
            case "error" when state.Phase == BattlePhase.Running:
                Finish(battle, BattlePhase.Cancelled, null, [message.Message ?? "The battle simulator stopped."]);
                break;
        }
    }

    private void Finish(HostedBattle battle, BattlePhase phase, string? winner, List<string>? problems)
    {
        DisposeSimulator(battle);
        Publish(battle, battle.State with { Phase = phase, Winner = winner, Problems = problems });
        hosted.Remove(battle.State.BattleId);
    }

    private static void DisposeSimulator(HostedBattle battle)
    {
        if (battle.Simulator is { } simulator)
        {
            battle.Simulator = null;
            _ = simulator.DisposeAsync().AsTask();
        }
    }

    private void PlayerLeftBattles(string playerId)
    {
        foreach (var battle in hosted.Values.ToList())
        {
            if (battle.SideOf(playerId) is not { } side)
                continue;
            if (battle.State.Phase == BattlePhase.Running && battle.Simulator is not null)
                battle.Simulator.Forfeit(side);
            else
                Finish(battle, BattlePhase.Cancelled, null, [$"{PlayerName(playerId)} left the room."]);
        }
    }

    private string PlayerName(string id)
    {
        lock (gate) return players.TryGetValue(id, out var p) ? p.Name : "?";
    }

    private void Publish(HostedBattle battle, BattleState state)
    {
        battle.State = state;
        Deliver(state.ChallengerId, state);
        Deliver(state.OpponentId, state);
    }

    /// <summary>To a player: applied here when it is the host's own player, sent otherwise.</summary>
    private void Deliver(string playerId, RoomMessage message)
    {
        if (playerId == LocalPlayerId)
        {
            ReceiveBattleMessage(message);
            RaiseChanged();
            return;
        }
        int peerId = peerPlayers.FirstOrDefault(p => p.Value == playerId).Key;
        if (peerPlayers.ContainsKey(peerId) && net.TryGetPeerById(peerId, out var peer))
            peer.Send(codec.Encode(message), DeliveryMethod.ReliableOrdered);
    }

    private async Task DisposeBattlesAsync()
    {
        foreach (var battle in hosted.Values.ToList())
        {
            if (battle.Simulator is { } simulator)
                await simulator.DisposeAsync();
        }
        hosted.Clear();
    }

    // ------------------------------------------------------------------ player (guest or the host's own player)

    private void ReceiveBattleMessage(RoomMessage message)
    {
        lock (gate)
        {
            switch (message)
            {
                case BattleState state:
                    battles.TryGetValue(state.BattleId, out var known);
                    bool starts = state.Phase == BattlePhase.Running && known?.Phase != BattlePhase.Running;
                    string? side = state.ChallengerId == LocalPlayerId ? "p1" : state.OpponentId == LocalPlayerId ? "p2" : null;
                    battles[state.BattleId] = new RoomBattle(
                        state.BattleId, state.ChallengerId, state.OpponentId, state.Rules, state.Ready, state.Phase, state.Winner,
                        state.Problems ?? [], starts || known is null ? [] : known.Log,
                        state.Phase == BattlePhase.Running ? known?.Request : null, side);
                    break;
                case BattleLog log when battles.TryGetValue(log.BattleId, out var battle):
                    battles[log.BattleId] = battle with { Log = [.. battle.Log, .. log.Lines] };
                    break;
                case BattleRequest request when battles.TryGetValue(request.BattleId, out var battle):
                    battles[request.BattleId] = battle with { Request = request.Request };
                    break;
            }
        }
    }
}
