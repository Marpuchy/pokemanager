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
    /// The game's own Mega Stones go on sale in the ordinary Poké Marts from the start. In Generation 7 the game only
    /// sells them after the story, and the randomizer marks every one of them a bad item, so a run with "ban bad items"
    /// never sees one; this puts them on the shelves without randomizing again.
    /// </summary>
    public bool MegaStonesOnSale { get; set; }

    /// <summary>Whether anything at all has to be done at build time.</summary>
    public bool IsEmpty => !FreeRareCandies && !MegaStonesOnSale;

    public ShopSettings Clone() => new() { FreeRareCandies = FreeRareCandies, MegaStonesOnSale = MegaStonesOnSale };
}
