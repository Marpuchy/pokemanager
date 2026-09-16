using System.Text.Json.Serialization;

namespace Pokemanager.Model.Projects;

/// <summary>What a roulette prize gives.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<LockePrizeKind>))]
public enum LockePrizeKind
{
    /// <summary><see cref="LockePrize.Amount"/> of item <see cref="LockePrize.ItemId"/> into the bag.</summary>
    Item,

    /// <summary><see cref="LockePrize.Amount"/> Pokédollars.</summary>
    Money,

    /// <summary>One extra life: a lost life comes back, or the maximum grows when none was lost.</summary>
    Life,

    /// <summary>Bad luck.</summary>
    Nothing,

    /// <summary>
    /// Free text (<see cref="LockePrize.Text"/>), for prizes the app cannot put into the save by itself: "an evolution
    /// item of your choice", "a Pokémon from the box"… The player gives it and marks it as already added.
    /// </summary>
    Text,
}

/// <summary>A segment of the badge roulette. <see cref="Weight"/> sets both its chance and its size on the wheel.</summary>
public sealed record LockePrize(LockePrizeKind Kind, int ItemId = 0, int Amount = 1, int Weight = 1, string? Text = null);

/// <summary>A badge roulette already spun. Items and money stay unclaimed until they are written into the save.</summary>
public sealed record LockeSpin(int Badge, LockePrize Prize, DateTime When, bool Claimed = true);

/// <summary>
/// Nuzlocke-style rules kept with the project: lives, and the roulette that can be spun once per badge earned in the
/// save.
/// </summary>
public sealed class LockeSettings
{
    public const int BadgeCount = 8;

    /// <summary>Lives for the run; 0 means lives are not tracked.</summary>
    public int MaxLives { get; set; } = DefaultMaxLives;

    public const int DefaultMaxLives = 10;

    public int LivesLost { get; set; }

    [JsonIgnore]
    public int LivesLeft => Math.Max(0, MaxLives - LivesLost);

    public List<LockePrize> Prizes { get; set; } = DefaultPrizes();

    /// <summary>First badge (1-based) that has a roulette.</summary>
    public int RouletteFirst { get; set; } = 1;

    /// <summary>Last badge (1-based) that can have a roulette.</summary>
    public int RouletteLast { get; set; } = BadgeCount;

    /// <summary>A roulette every this many badges, counting from <see cref="RouletteFirst"/> (1 = every badge).</summary>
    public int RouletteEvery { get; set; } = 1;

    /// <summary>Whether badge <paramref name="badge"/> (0-based) gets a roulette with the interval settings.</summary>
    public bool HasRoulette(int badge)
    {
        int number = badge + 1, first = Math.Max(1, RouletteFirst), every = Math.Max(1, RouletteEvery);
        return number >= first && number <= RouletteLast && (number - first) % every == 0;
    }

    public List<LockeSpin> Spins { get; set; } = [];

    public LockeSpin? SpinOf(int badge) => Spins.LastOrDefault(s => s.Badge == badge);

    /// <summary>A sensible starting set: healing and progression items, some money, an extra life and bad luck.</summary>
    public static List<LockePrize> DefaultPrizes() =>
    [
        new(LockePrizeKind.Item, ItemId: 50, Amount: 3, Weight: 3),   // Rare Candy
        new(LockePrizeKind.Item, ItemId: 29, Amount: 2, Weight: 2),   // Max Revive
        new(LockePrizeKind.Item, ItemId: 23, Amount: 3, Weight: 2),   // Full Restore
        new(LockePrizeKind.Item, ItemId: 51, Amount: 2, Weight: 2),   // PP Up
        new(LockePrizeKind.Money, Amount: 10000, Weight: 2),
        new(LockePrizeKind.Item, ItemId: 234, Amount: 1, Weight: 1),  // Leftovers
        new(LockePrizeKind.Item, ItemId: 645, Amount: 1, Weight: 1),  // Ability Capsule
        new(LockePrizeKind.Item, ItemId: 1, Amount: 1, Weight: 1),    // Master Ball
        new(LockePrizeKind.Life, Weight: 1),
        new(LockePrizeKind.Nothing, Weight: 1),
    ];

    /// <summary>Picks a prize index with probability proportional to its weight (non-positive weights never win).</summary>
    public int Roll(Random random)
    {
        int total = Prizes.Sum(p => Math.Max(0, p.Weight));
        if (total == 0)
            return -1;
        int ticket = random.Next(total);
        for (int i = 0; i < Prizes.Count; i++)
        {
            ticket -= Math.Max(0, Prizes[i].Weight);
            if (ticket < 0)
                return i;
        }
        return Prizes.Count - 1;
    }

    /// <summary>
    /// Records what the roulette gave for a badge, as soon as it stops (so it cannot be re-spun by closing the window).
    /// Lives and bad luck take effect at once; items and money wait for <see cref="MarkClaimed"/>.
    /// </summary>
    public void RecordWin(int badge, LockePrize prize, DateTime when)
    {
        Spins.RemoveAll(s => s.Badge == badge && !s.Claimed);
        Spins.Add(new LockeSpin(badge, prize, when, Claimed: prize.Kind is LockePrizeKind.Life or LockePrizeKind.Nothing));
        if (prize.Kind == LockePrizeKind.Life)
        {
            if (LivesLost > 0)
                LivesLost--;
            else
                MaxLives++;
        }
    }

    /// <summary>The prize of the badge was written into the save.</summary>
    public void MarkClaimed(int badge)
    {
        int index = Spins.FindLastIndex(s => s.Badge == badge);
        if (index >= 0)
            Spins[index] = Spins[index] with { Claimed = true };
    }

    /// <summary>Lets a badge be spun again (removes its spins).</summary>
    public void ForgetSpin(int badge) => Spins.RemoveAll(s => s.Badge == badge);
}
