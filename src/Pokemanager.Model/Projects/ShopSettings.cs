namespace Pokemanager.Model.Projects;

/// <summary>
/// Changes to the game's shops, applied when the ROM is built. They are part of the project, not of the randomizer's
/// settings: changing the seed does not touch them, and they can be changed without randomizing again.
/// </summary>
/// <remarks>
/// The game decides what a shop sells with a table of item ids inside <c>code.bin</c>, and how many items each shop has
/// is fixed in its code — so items are put in the last slots of each ordinary Poké Mart, replacing what was there. See
/// <see cref="Data.GameShops"/> for which games can do this.
/// </remarks>
public sealed class ShopSettings
{
    /// <summary>
    /// Rare Candies in every ordinary Poké Mart, and their price set to 0. Both halves are needed: a free item nobody
    /// sells is not free, and one that is sold at 4800 is not either.
    /// </summary>
    public bool FreeRareCandies { get; set; }

    /// <summary>
    /// Items the game never sells, put on sale in the ordinary Poké Marts: the Mega Stones of Sun and Moon, an
    /// evolution item a randomized run made necessary, whatever the player decides.
    /// </summary>
    public List<int> ExtraItems { get; set; } = [];

    /// <summary>
    /// The game's Mega Stones on sale in the ordinary Poké Marts, so a run can mega evolve before the point the game
    /// decides. They are **dealt out** across those shops rather than all put in each: there are thirty in X and
    /// forty-two in Generation 7, and no single shop has that many shelves.
    /// </summary>
    public bool MegaStonesInShops { get; set; }

    /// <summary>Whether anything at all has to be done at build time.</summary>
    public bool IsEmpty => !FreeRareCandies && !MegaStonesInShops && ExtraItems.Count == 0;

    public ShopSettings Clone() =>
        new() { FreeRareCandies = FreeRareCandies, MegaStonesInShops = MegaStonesInShops, ExtraItems = [.. ExtraItems] };
}
