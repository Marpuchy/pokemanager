using Pokemanager.Model.Data;
using Pokemanager.Model.Dump;
using Pokemanager.Model.Edits;

namespace Pokemanager.Model.Editing;

/// <summary>
/// Makes a Pokémon's earlier stages follow what was done to it (2026-11-10, user: "haz ajustes a las preevoluciones de
/// los pokemon editados, por ejemplo en el ratio de captura y stats"). Each earlier stage gets, from the ROM's own values:
/// <list type="bullet">
/// <item>its base stat total scaled by what its evolutions' totals were scaled by (an earlier stage of several takes the
/// average), spread the way <see cref="StatSpread.Fit"/> spreads it — mostly onto its strongest stats;</item>
/// <item>its catch rate moved the way the game's catch rates move with the total at that stage (<see cref="CatchRates"/>);</item>
/// <item>the new types, except the first stage (user, issue #3: "menos en la primera"), and only when it had the same
/// types as its evolution to begin with;</item>
/// <item>the experience group of its evolution, so evolving never moves its level.</item>
/// </list>
/// Everything is worked out from the ROM's values, so running it again gives the same result.
/// </summary>
public sealed class PreevolutionSync
{
    private static readonly string[] StatFields = ["hp", "atk", "def", "spa", "spd", "spe"];
    private const string P = GameTables.Personal;

    private readonly EditorSession session;
    private readonly Dictionary<int, List<int>> before = [];   // row → rows that evolve into it
    private readonly Dictionary<int, List<int>> after = [];    // row → rows it evolves into
    private readonly CatchRates catchRates;

    public PreevolutionSync(EditorSession session)
    {
        this.session = session;
        var data = session.Current;
        var index = PersonalIndex.Of(data);
        for (int row = 0; row < data.Evolutions.Length && row < data.Personal.Length; row++)
            foreach (var (_, evolution) in data.Evolutions[row].InUse())
            {
                int species = evolution.Species;
                int count = species > 0 && species < data.Personal.Length && data.Personal[species].FormeCount is > 0 and var n ? n : 1;
                int form = EvolutionSlots.TargetForm(evolution, index.Pokemon(row)?.Form ?? 0, count, data.Title.Generation() == 7);
                int target = index.Row(evolution.Species, form);
                if (target < 0)
                    target = index.Row(evolution.Species, 0);
                if (target <= 0 || target == row || target >= data.Personal.Length)
                    continue;
                Add(before, target, row);
                Add(after, row, target);
            }
        catchRates = new CatchRates(session.Original, row => before.ContainsKey(row), row => after.ContainsKey(row));
    }

    private static void Add(Dictionary<int, List<int>> map, int key, int value)
    {
        if (!map.TryGetValue(key, out var list))
            map[key] = list = [];
        if (!list.Contains(value))
            list.Add(value);
    }

    /// <summary>Every earlier stage of <paramref name="row"/>, nearest first.</summary>
    public IReadOnlyList<int> EarlierStages(int row)
    {
        var found = new List<int>();
        var queue = new Queue<int>([row]);
        while (queue.Count > 0)
            foreach (int earlier in before.GetValueOrDefault(queue.Dequeue()) ?? [])
                if (earlier != row && !found.Contains(earlier))
                {
                    found.Add(earlier);
                    queue.Enqueue(earlier);
                }
        return found;
    }

    private int[] Stats(Data.GameData data, int row)
    {
        var p = data.Personal[row];
        return [p.HP, p.ATK, p.DEF, p.SPA, p.SPD, p.SPE];
    }

    /// <summary>
    /// What a row's total was scaled by: its own change when <paramref name="isSource"/> says so, otherwise the average
    /// of its evolutions' (a last stage nobody edited counts as unchanged).
    /// </summary>
    private double? Factor(int row, Func<int, bool> isSource, HashSet<int> seen)
    {
        if (!seen.Add(row))
            return null;
        if (isSource(row))
        {
            int original = session.Original.Personal[row].BST;
            return original > 0 ? (double)session.Current.Personal[row].BST / original : null;
        }
        var next = after.GetValueOrDefault(row) ?? [];
        if (next.Count == 0)
            return 1.0;   // a last stage nobody edited: it did not change
        var factors = next.Select(n => Factor(n, isSource, new HashSet<int>(seen))).OfType<double>().ToList();
        return factors.Count > 0 ? factors.Average() : null;
    }

    private bool StatsEdited(int row) => StatFields.Any(f => session.IsModified(P, row, f));

    /// <summary>
    /// After <paramref name="row"/> was edited: recomputes every earlier stage of it, over whatever they had. Returns
    /// how many rows changed.
    /// </summary>
    public int SyncFrom(int row)
    {
        var earlier = EarlierStages(row);
        // The edited row and the rows after an earlier stage that were edited by hand lead; the earlier stages of this
        // one are what is being recomputed.
        bool Source(int r) => r == row || (!earlier.Contains(r) && StatsEdited(r));
        return earlier.Count(e => Apply(e, Source, row));
    }

    /// <summary>
    /// The whole project: every earlier stage of an edited Pokémon whose stats were not edited itself. Returns how many
    /// rows changed.
    /// </summary>
    public int SyncAll()
    {
        var sources = Enumerable.Range(0, session.Current.Personal.Length).Where(StatsEdited).ToHashSet();
        var targets = sources.SelectMany(EarlierStages).Where(r => !sources.Contains(r)).Distinct().ToList();
        int changed = 0;
        foreach (int target in targets)
        {
            int lead = Lead(target, sources);
            if (Apply(target, sources.Contains, lead))
                changed++;
        }
        return changed;
    }

    /// <summary>The edited evolution of <paramref name="row"/> whose types and experience group it takes: the nearest.</summary>
    private int Lead(int row, HashSet<int> sources)
    {
        var queue = new Queue<int>([row]);
        var seen = new HashSet<int>();
        while (queue.Count > 0)
        {
            int r = queue.Dequeue();
            if (!seen.Add(r))
                continue;
            if (r != row && sources.Contains(r))
                return r;
            foreach (int next in after.GetValueOrDefault(r) ?? [])
                queue.Enqueue(next);
        }
        return row;
    }

    private bool Apply(int row, Func<int, bool> isSource, int lead)
    {
        var data = session.Current;
        var original = session.Original;
        var factor = Factor(row, r => r != row && isSource(r), []);
        if (factor is null)
            return false;
        bool changed = false;
        void Set(string field, int value)
        {
            if (session.GetInt(P, row, field) == value)
                return;
            session.SetInt(P, row, field, value);
            changed = true;
        }

        int[] from = Stats(original, row);
        int total = (int)Math.Round(from.Sum() * factor.Value);
        int[] stats = StatSpread.Fit(from, total);
        for (int i = 0; i < 6; i++)
            Set(StatFields[i], stats[i]);

        Set("catchRate", catchRates.For(row, original.Personal[row].CatchRate, from.Sum(), total));

        if (lead != row && lead < data.Personal.Length)
        {
            Set("expGrowth", data.Personal[lead].EXPGrowth);
            bool firstStage = !before.ContainsKey(row);
            var originalTypes = original.Personal[row].Types;
            var leadOriginal = original.Personal[lead].Types;
            if (!firstStage && originalTypes[0] == leadOriginal[0] && originalTypes[1] == leadOriginal[1])
            {
                Set("type1", data.Personal[lead].Types[0]);
                Set("type2", data.Personal[lead].Types[1]);
            }
        }
        return changed;
    }
}

/// <summary>Spreads a base stat total over the six stats.</summary>
public static class StatSpread
{
    /// <summary>Share of a raise for the highest stat, the second… (user, issue #3: "reforzar sus puntos fuertes").</summary>
    private static readonly double[] Weights = [0.32, 0.27, 0.18, 0.11, 0.08, 0.04];

    /// <summary>
    /// <paramref name="stats"/> brought to <paramref name="total"/>: a raise goes mostly to the strongest stats, a cut
    /// comes off every stat in proportion; each stays within 1-255. <paramref name="fixedHp"/> keeps HP as given (a mega).
    /// </summary>
    public static int[] Fit(IReadOnlyList<int> stats, int total, int fixedHp = -1)
    {
        var values = stats.ToArray();
        if (fixedHp >= 0)
            values[0] = fixedHp;
        var free = Enumerable.Range(0, 6).Where(i => fixedHp < 0 || i != 0).ToList();
        int delta = total - values.Sum();
        if (delta == 0 || free.Count == 0)
            return values;
        var share = new double[6];
        if (delta > 0)
        {
            var order = free.OrderByDescending(i => values[i]).ThenBy(i => i).ToList();
            double sum = order.Select((_, k) => Weights[k]).Sum();
            for (int k = 0; k < order.Count; k++)
                share[order[k]] = delta * Weights[k] / sum;
        }
        else
        {
            double sum = free.Sum(i => values[i]);
            foreach (int i in free)
                share[i] = sum > 0 ? delta * values[i] / sum : delta / (double)free.Count;
        }
        var result = values.Select((v, i) => v + (int)Math.Floor(share[i])).ToArray();
        int left = total - result.Sum();
        var byRemainder = free.OrderByDescending(i => share[i] - Math.Floor(share[i])).ThenBy(i => i).ToList();
        for (int guard = 0; left > 0 && guard < 6 * 255; guard++)
        {
            int i = byRemainder[guard % byRemainder.Count];
            if (result[i] < 255)
            {
                result[i]++;
                left--;
            }
        }
        for (int i = 0; i < 6; i++)
        {
            if (result[i] < 1)
                result[i] = 1;
            if (result[i] > 255)
            {
                int over = result[i] - 255;
                result[i] = 255;
                foreach (int j in free.Where(j => result[j] < 255).OrderByDescending(j => result[j]))
                {
                    int room = Math.Min(over, 255 - result[j]);
                    result[j] += room;
                    over -= room;
                    if (over == 0)
                        break;
                }
            }
        }
        return result;
    }
}

/// <summary>
/// How the game's catch rates fall as the base stat total rises, per stage (user: "por fase, como en el juego original"):
/// a line through ln(catch rate) against the total, fitted on the game's own first stages and, apart, its middle stages.
/// A Pokémon whose total changes keeps its own rate moved along that line, so a few points change it a little.
/// </summary>
public sealed class CatchRates
{
    private readonly double firstSlope, middleSlope;
    private readonly Func<int, bool> hasEarlier;

    public CatchRates(Data.GameData original, Func<int, bool> hasEarlier, Func<int, bool> hasLater)
    {
        this.hasEarlier = hasEarlier;
        // The game's own species only (a mod's later generations come with catch rates of their own kind), and not the
        // gifts — starters, fossils, Eevee: seven in eight male, all at 45.
        int last = Math.Min(Math.Min(original.LastSpecies, original.Title.Generation() == 7 ? 807 : 721), original.Personal.Length - 1);
        var first = new List<(double X, double Y)>();
        var middle = new List<(double X, double Y)>();
        for (int row = 1; row <= last; row++)
        {
            if (!hasLater(row) || Legendaries.Contains(row))
                continue;
            var p = original.Personal[row];
            if (p.CatchRate == 0 || p.BST == 0 || p.Gender == 31)
                continue;
            (hasEarlier(row) ? middle : first).Add((p.BST, Math.Log(p.CatchRate)));
        }
        firstSlope = Slope(first);
        middleSlope = Slope(middle);
    }

    /// <summary>Least-squares slope; never above 0 (a stronger Pokémon is never easier to catch). Default: the games' own.</summary>
    private static double Slope(List<(double X, double Y)> points)
    {
        if (points.Count < 10)
            return -0.008;
        double mx = points.Average(p => p.X), my = points.Average(p => p.Y);
        double sxx = points.Sum(p => (p.X - mx) * (p.X - mx));
        return sxx == 0 ? -0.008 : Math.Min(0, points.Sum(p => (p.X - mx) * (p.Y - my)) / sxx);
    }

    public double SlopeFor(int row) => hasEarlier(row) ? middleSlope : firstSlope;

    /// <summary>The catch rate of <paramref name="row"/> once its total goes from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public int For(int row, int rate, int from, int to) =>
        Math.Clamp((int)Math.Round(rate * Math.Exp(SlopeFor(row) * (to - from))), 3, 255);
}
