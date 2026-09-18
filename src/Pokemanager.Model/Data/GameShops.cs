using Pokemanager.Model.Dump;

namespace Pokemanager.Model.Data;

/// <summary>
/// Where a game keeps what each shop sells: a flat table of item ids, one run per shop. Generation 6 keeps it inside the
/// decompressed <c>code.bin</c>, Generation 7 inside the romfs module <c>Shop.cro</c>.
/// </summary>
/// <param name="Locator">
/// Generation 6: the first bytes of the table, as the game ships them. There it is not at a fixed address (it moves
/// between game versions), so it is found by searching for this; a pattern that does not appear exactly once means the
/// game is not one we can edit, and nothing is written. Empty when <paramref name="File"/> says where the table is.
/// </param>
/// <param name="Sizes">How many items each shop sells, in order.</param>
/// <param name="Regular">The shops that are ordinary Poké Marts (the ones an "add this to the shops" option touches).</param>
/// <param name="File">
/// The romfs file the table lives in (Generation 7: <c>Shop.cro</c>), or null for the games that keep it in
/// <c>code.bin</c>. A file is not searched: the module is the same in every copy of the game, so the table is at
/// <paramref name="Offset"/> — which is still checked before anything is written.
/// </param>
/// <param name="Offset">Where the table starts inside <paramref name="File"/>.</param>
public sealed record ShopLayout(byte[] Locator, int[] Sizes, int[] Regular, string? File = null, int Offset = 0)
{
    /// <summary>How many item ids the whole table holds.</summary>
    public int Total => Sizes.Sum();

    /// <summary>Where each shop's run starts inside the table.</summary>
    public int Start(int shop) => Sizes.Take(shop).Sum();
}

/// <summary>Reads and writes the shop table of a game.</summary>
/// <remarks>
/// **Verified on the real Pokémon X dump**: the table is 26 shops and 217 item ids at the locator, and reading it with
/// these sizes gives exactly the game's shops (the first Poké Mart sells a Poké Ball and a Potion, the four TM shops
/// hold only TMs). **Verified on the user's Alpha Sapphire game folder**: found at 0x47AA3E, 24 shops, the ten ordinary
/// Poké Marts first. **Verified on the user's Ultra Moon**: <c>Shop.cro</c> is 40 KB and the table is at 0x50BC, where
/// shop 0 is the Poké Ball, Potion and status healers of a Poké Mart — the very offset UPR ZX carries as its constant
/// for Ultra Sun/Ultra Moon. Sun/Moon (0x50A8) comes from that same source and has **not** been checked against a dump;
/// a wrong offset simply disables the feature, because every id of the table has to be a real item before anything is
/// written.
/// </remarks>
public static class GameShops
{
    /// <summary>Rare Candy, the same id in both generations (item 50; verified in the X dump's item names).</summary>
    public const int RareCandy = 50;

    public static ShopLayout? For(GameTitle title) => title.Family() switch
    {
        // Poké Ball, Potion, Poké Ball, Great Ball — the start of the table in X and Y.
        GameFamily.XY => new ShopLayout(
            [0x04, 0x00, 0x11, 0x00, 0x04, 0x00, 0x03, 0x00],
            [2, 11, 14, 17, 18, 19, 19, 19, 19, 1, 4, 10, 3, 9, 1, 1, 3, 3, 5, 5, 6, 7, 5, 5, 8, 3],
            [0, 1, 2, 3, 4, 5, 6, 7, 8, 14, 15]),
        // Poké Ball, Potion, Antidote, Poké Ball, Great Ball.
        GameFamily.ORAS => new ShopLayout(
            [0x04, 0x00, 0x11, 0x00, 0x12, 0x00, 0x04, 0x00, 0x03, 0x00],
            [3, 10, 14, 17, 18, 19, 19, 19, 19, 1, 9, 6, 4, 3, 8, 8, 3, 3, 4, 3, 6, 8, 7, 4],
            [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]),
        // Generation 7: inside Shop.cro, at the offset UPR ZX uses; the first eight shops are the ordinary Poké Marts.
        GameFamily.SM => new ShopLayout([],
            [9, 11, 13, 15, 17, 19, 20, 21, 9, 4, 8, 12, 5, 4, 11, 3, 10, 6, 10, 6, 4, 5, 7, 1],
            [0, 1, 2, 3, 4, 5, 6, 7], ShopFile, 0x50A8),
        GameFamily.USUM => new ShopLayout([],
            [9, 12, 14, 16, 18, 20, 21, 22, 9, 4, 8, 12, 5, 4, 11, 3, 5, 6, 10, 5, 4, 5, 7, 5, 8, 8, 8, 8],
            [0, 1, 2, 3, 4, 5, 6, 7], ShopFile, 0x50BC),
        _ => null,
    };

    /// <summary>The romfs module that holds the shops in Generation 7 (40 KB in Ultra Moon).</summary>
    public const string ShopFile = "Shop.cro";

    /// <summary>
    /// Where the table starts in <paramref name="data"/> (the game's <c>code.bin</c>, or the file the layout names), or
    /// null when it cannot be told beyond doubt: a locator has to appear exactly once, and either way every id of the
    /// table has to be a real item of the game.
    /// </summary>
    public static int? Find(byte[] data, ShopLayout layout, int itemCount)
    {
        int at;
        if (layout.File is not null)
        {
            at = layout.Offset;
        }
        else
        {
            at = IndexOf(data, layout.Locator, 0);
            if (at < 0 || IndexOf(data, layout.Locator, at + 1) >= 0)
                return null;
        }
        if (at < 0 || at + (layout.Total * 2) > data.Length)
            return null;
        for (int i = 0; i < layout.Total; i++)
        {
            int item = BitConverter.ToUInt16(data, at + (i * 2));
            if (item <= 0 || item >= itemCount)
                return null;
        }
        return at;
    }

    /// <summary>The whole table as item ids.</summary>
    public static int[] Read(byte[] code, int offset, ShopLayout layout) =>
        [.. Enumerable.Range(0, layout.Total).Select(i => (int)BitConverter.ToUInt16(code, offset + (i * 2)))];

    /// <summary>Writes the table back; the array has to be exactly as long as the one that was read.</summary>
    public static void Write(byte[] code, int offset, ShopLayout layout, IReadOnlyList<int> items)
    {
        if (items.Count != layout.Total)
            throw new ArgumentException($"The shop table holds {layout.Total} items, not {items.Count}.", nameof(items));
        for (int i = 0; i < items.Count; i++)
            BitConverter.GetBytes((ushort)items[i]).CopyTo(code, offset + (i * 2));
    }

    /// <summary>
    /// Fills the last slots of every ordinary Poké Mart. <paramref name="everywhere"/> goes in all of them (one item,
    /// like the Rare Candies); <paramref name="spread"/> is dealt out across them instead, shop by shop, because a set
    /// like the Mega Stones does not fit in any single one — the game gives each shop a fixed number of shelves and
    /// that number is in its code, not in this table. **One slot of every shop is always left as the game had it.**
    /// </summary>
    /// <returns>How many slots changed.</returns>
    public static int AddToRegularShops(int[] table, ShopLayout layout, IReadOnlyList<int> everywhere,
        IReadOnlyList<int>? spread = null)
    {
        var rest = (spread ?? []).Distinct().ToList();
        int changed = 0, cursor = 0;
        foreach (int shop in layout.Regular)
        {
            int start = layout.Start(shop), size = layout.Sizes[shop], room = Math.Max(0, size - 1);
            var current = table.Skip(start).Take(size).ToList();
            var wanted = new List<int>();

            foreach (int id in everywhere.Distinct())
            {
                if (wanted.Count < room && !current.Contains(id))
                    wanted.Add(id);
            }
            while (wanted.Count < room && cursor < rest.Count)
            {
                int id = rest[cursor++];
                if (!current.Contains(id))
                    wanted.Add(id);
            }

            // They take the end of the shelf, in the order they were asked for.
            for (int i = 0; i < wanted.Count; i++)
            {
                int slot = start + size - wanted.Count + i;
                if (table[slot] == wanted[i])
                    continue;
                table[slot] = wanted[i];
                changed++;
            }
        }
        return changed;
    }

    private static int IndexOf(byte[] data, byte[] pattern, int from)
    {
        for (int i = from; i + pattern.Length <= data.Length; i++)
        {
            int j = 0;
            while (j < pattern.Length && data[i + j] == pattern[j])
                j++;
            if (j == pattern.Length)
                return i;
        }
        return -1;
    }
}
