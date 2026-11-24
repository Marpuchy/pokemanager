namespace Pokemanager.Multiplayer;

/// <summary>A prize of the room's battle roulette (2026-11-24, user: the winner of a battle spins it).</summary>
/// <param name="Kind">"item", "money", "text" or "nothing".</param>
/// <param name="Item">Item id (kind "item"), as the games number them.</param>
/// <param name="Amount">How many items, or how much money.</param>
/// <param name="Text">What is won, for a prize the application cannot put in a save (kind "text").</param>
/// <param name="Weight">How likely it is, against the others.</param>
public sealed record RoomPrize(string Kind, int Item = 0, int Amount = 1, string? Text = null, int Weight = 1);

/// <summary>Someone won a prize of the roulette after winning a battle.</summary>
public sealed record RoomPrizeWon(string BattleId, string PlayerId, RoomPrize Prize, DateTime When);

/// <summary>The rules of the room as a PDF chosen by the host.</summary>
public sealed record RoomRulesDocument(string FileName, byte[] Pdf);

public sealed partial class RoomSession
{
    /// <summary>The largest rules PDF a room sends (it goes to every player, and again to each who joins).</summary>
    public const int MaxRulesPdfBytes = 16 * 1024 * 1024;

    private List<RoomPrize> prizes = [];
    private RoomRulesDocument? rulesDocument;
    private readonly List<RoomPrizeWon> prizesWon = [];
    private readonly HashSet<string> spunBattles = [];

    /// <summary>Host: who won each battle that ended with a winner (the hosted battle is gone by then).</summary>
    private readonly Dictionary<string, string> battleWinners = [];

    /// <summary>The prizes of the battle roulette, set by the host.</summary>
    public IReadOnlyList<RoomPrize> Prizes
    {
        get { lock (gate) return prizes.ToList(); }
    }

    /// <summary>The rules PDF of the room, when the host chose one.</summary>
    public RoomRulesDocument? RulesDocument
    {
        get { lock (gate) return rulesDocument; }
    }

    /// <summary>Prizes won in this session, newest first.</summary>
    public IReadOnlyList<RoomPrizeWon> PrizesWon
    {
        get { lock (gate) return prizesWon.AsEnumerable().Reverse().ToList(); }
    }

    /// <summary>Whether the roulette was already spun for that battle (one spin per battle won).</summary>
    public bool WasSpun(string battleId)
    {
        lock (gate) return spunBattles.Contains(battleId);
    }

    /// <summary>Host: the prizes of the battle roulette.</summary>
    public void SetPrizes(IEnumerable<RoomPrize> list) => work.Enqueue(() =>
    {
        if (!IsHost)
            return;
        var copy = list.Where(p => p.Weight > 0).ToList();
        lock (gate) prizes = copy;
        Broadcast(new PrizesSet(copy), except: null);
        RaiseChanged();
    });

    /// <summary>Host: the rules PDF (null takes it away).</summary>
    /// <exception cref="ArgumentException">Larger than <see cref="MaxRulesPdfBytes"/>.</exception>
    public void SetRulesDocument(RoomRulesDocument? document)
    {
        if (document is { Pdf.Length: > MaxRulesPdfBytes })
            throw new ArgumentException("The PDF is too large.", nameof(document));
        work.Enqueue(() =>
        {
            if (!IsHost)
                return;
            lock (gate) rulesDocument = document;
            Broadcast(new RulesDocumentSet(document?.FileName, document?.Pdf), except: null);
            RaiseChanged();
        });
    }

    /// <summary>
    /// The winner of a battle spun the roulette: what came out (rolled by the winner's app, which then puts it in their
    /// save). The host checks that this player won that battle and did not spin for it already, and tells everyone.
    /// </summary>
    public void ReportPrize(string battleId, RoomPrize prize) => ToHost(new PrizeSpun(battleId, prize));

    /// <summary>Host: what a newcomer needs after the welcome.</summary>
    private void SendExtras(LiteNetLib.NetPeer peer)
    {
        List<RoomPrize> list;
        RoomRulesDocument? document;
        lock (gate)
        {
            list = prizes.ToList();
            document = rulesDocument;
        }
        if (list.Count > 0)
            Send(peer, new PrizesSet(list));
        if (document is not null)
            Send(peer, new RulesDocumentSet(document.FileName, document.Pdf));
    }

    /// <summary>Host: a spin of a player (a guest, or the host's own).</summary>
    private void HostPrizeSpun(string from, PrizeSpun spun)
    {
        if (!battleWinners.TryGetValue(spun.BattleId, out string? winner) || winner != from)
            return;
        lock (gate)
        {
            if (!spunBattles.Add(spun.BattleId))
                return;
        }
        var won = new PrizeWon(spun.BattleId, from, spun.Prize);
        Broadcast(won, except: null);
        ReceivePrizeWon(won);
    }

    private void ReceivePrizeWon(PrizeWon won)
    {
        lock (gate)
        {
            spunBattles.Add(won.BattleId);
            if (prizesWon.All(p => p.BattleId != won.BattleId))
                prizesWon.Add(new RoomPrizeWon(won.BattleId, won.PlayerId, won.Prize, DateTime.Now));
        }
    }

    /// <summary>Guest: the messages of this part.</summary>
    private bool GuestReceiveExtras(RoomMessage message)
    {
        switch (message)
        {
            case PrizesSet set:
                lock (gate) prizes = set.Prizes;
                return true;
            case RulesDocumentSet document:
                lock (gate)
                    rulesDocument = document is { FileName: { } name, Pdf: { Length: > 0 and <= MaxRulesPdfBytes } pdf }
                        ? new RoomRulesDocument(name, pdf)
                        : null;
                return true;
            case PrizeWon won:
                ReceivePrizeWon(won);
                return true;
            default:
                return false;
        }
    }
}
