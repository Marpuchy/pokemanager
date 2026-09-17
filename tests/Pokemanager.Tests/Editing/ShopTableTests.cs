using Pokemanager.Model.Data;
using Pokemanager.Model.Dump;

namespace Pokemanager.Tests.Editing;

/// <summary>
/// The shop table on hand-made data, so both ways of finding it are covered without a dump: Generation 6 searches the
/// executable for the first ids of the table, Generation 7 reads its module at a fixed offset.
/// </summary>
public class ShopTableTests
{
    private static byte[] Buffer(int length, int at, IEnumerable<int> items)
    {
        byte[] data = new byte[length];
        int i = 0;
        foreach (int item in items)
            BitConverter.GetBytes((ushort)item).CopyTo(data, at + (i++ * 2));
        return data;
    }

    /// <summary>A table where every shop sells the same thing, so what moves is easy to see.</summary>
    private static int[] Filled(ShopLayout layout, int item) => [.. Enumerable.Repeat(item, layout.Total)];

    [Fact]
    public void EveryGameOfGeneration6And7HasALayout()
    {
        foreach (var game in Enum.GetValues<GameTitle>())
        {
            var layout = GameShops.For(game);
            Assert.NotNull(layout);
            Assert.Equal(layout!.Sizes.Length, layout.Sizes.Length);
            Assert.All(layout.Regular, shop => Assert.InRange(shop, 0, layout.Sizes.Length - 1));
            // Generation 6 searches for the locator, Generation 7 knows the offset inside its module.
            Assert.Equal(game.Generation() == 6, layout.File is null);
        }
    }

    [Fact]
    public void Generation7_ReadsItsModuleAtTheKnownOffset()
    {
        var layout = GameShops.For(GameTitle.UltraMoon)!;
        Assert.Equal("Shop.cro", layout.File);
        byte[] module = Buffer(0xA000, layout.Offset, Filled(layout, 17));

        int offset = GameShops.Find(module, layout, itemCount: 960) ?? throw new Xunit.Sdk.XunitException("table not found");

        Assert.Equal(layout.Offset, offset);
        Assert.Equal(layout.Total, GameShops.Read(module, offset, layout).Length);
    }

    [Fact]
    public void AnIdThatIsNotAnItem_DisablesTheFeature()
    {
        var layout = GameShops.For(GameTitle.UltraMoon)!;
        byte[] module = Buffer(0xA000, layout.Offset, Filled(layout, 17));
        // A module that does not hold the table where this game keeps it: one id is beyond the game's items.
        BitConverter.GetBytes((ushort)5000).CopyTo(module, layout.Offset + 8);

        Assert.Null(GameShops.Find(module, layout, itemCount: 960));
        Assert.Null(GameShops.Find(new byte[0x100], layout, itemCount: 960));   // too short for the table
    }

    [Fact]
    public void Generation6_FindsTheTableByItsFirstIds()
    {
        var layout = GameShops.For(GameTitle.X)!;
        int at = 0x1234;
        var table = Filled(layout, 17);
        for (int i = 0; i < layout.Locator.Length / 2; i++)
            table[i] = BitConverter.ToUInt16(layout.Locator, i * 2);
        byte[] code = Buffer(0x4000, at, table);

        Assert.Equal(at, GameShops.Find(code, layout, itemCount: 718));
    }

    [Fact]
    public void ExtraItemsGoInTheLastSlots_AndEveryShopKeepsOne()
    {
        var layout = GameShops.For(GameTitle.UltraMoon)!;
        int[] table = Filled(layout, 17);

        int changed = GameShops.AddToRegularShops(table, layout, [50, 774]);

        Assert.Equal(layout.Regular.Sum(s => Math.Min(2, Math.Max(0, layout.Sizes[s] - 1))), changed);
        foreach (int shop in layout.Regular)
        {
            var sold = table.Skip(layout.Start(shop)).Take(layout.Sizes[shop]).ToList();
            Assert.Contains(50, sold);
            Assert.Contains(774, sold);
            Assert.Equal(17, sold[0]);                       // what the shop had is still first
            Assert.Equal([50, 774], sold.TakeLast(2));       // the additions go last, in order
        }
        // Shops that are not ordinary Poké Marts are left exactly as they were.
        foreach (int shop in Enumerable.Range(0, layout.Sizes.Length).Except(layout.Regular))
            Assert.All(table.Skip(layout.Start(shop)).Take(layout.Sizes[shop]), item => Assert.Equal(17, item));
    }

    [Fact]
    public void AnItemTheShopAlreadySells_IsNotAddedTwice()
    {
        var layout = GameShops.For(GameTitle.X)!;
        int[] table = Filled(layout, 50);

        Assert.Equal(0, GameShops.AddToRegularShops(table, layout, [50]));
        Assert.All(table, item => Assert.Equal(50, item));
    }

    [Fact]
    public void WritingBackATableOfAnotherSize_IsRefused()
    {
        var layout = GameShops.For(GameTitle.X)!;
        Assert.Throws<ArgumentException>(() => GameShops.Write(new byte[0x1000], 0, layout, [1, 2, 3]));
    }
}
