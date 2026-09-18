using pk3DS.Core.CTR;
using pk3DS.Core.Structures;
using Pokemanager.Model.Build;
using Pokemanager.Model.Data;
using Pokemanager.Model.Dump;
using Pokemanager.Model.Editing;
using Pokemanager.Model.Edits;
using Pokemanager.Model.Projects;

namespace Pokemanager.Tests.Dump;

/// <summary>
/// The shops and the item prices of the real Pokémon X dump (skipped without <c>POKEMANAGER_DUMP</c>). The numbers are
/// measured, not assumed: 718 items, the table is 26 shops and 217 ids, and the first Poké Mart sells a Poké Ball and
/// a Potion.
/// </summary>
public class RealDumpShopTests
{
    private static string RequireDump()
    {
        string? dumpDir = Environment.GetEnvironmentVariable("POKEMANAGER_DUMP");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(dumpDir), "POKEMANAGER_DUMP is not set.");
        Assert.SkipUnless(Directory.Exists(Path.Combine(dumpDir!, "romfs")), $"{dumpDir} does not contain romfs.");
        return dumpDir!;
    }

    [Fact]
    public void Items_AreReadWithTheirPrice()
    {
        var data = GameData.Load(Path.Combine(RequireDump(), "romfs"));

        Assert.Equal(718, data.Items.Length);
        Assert.Equal(200, data.Items[4].BuyPrice);              // Poké Ball
        Assert.Equal(4800, data.Items[GameShops.RareCandy].BuyPrice);
        Assert.Equal(2400, data.Items[GameShops.RareCandy].SellPrice);
    }

    [Fact]
    public void ShopTable_IsFoundAndReadsTheGamesShops()
    {
        string dumpDir = RequireDump();
        byte[] code = File.ReadAllBytes(Path.Combine(dumpDir, "exefs", "code.bin"));
        var layout = GameShops.For(GameTitle.X)!;

        int offset = GameShops.Find(code, layout, 718) ?? throw new Xunit.Sdk.XunitException("shop table not found");
        int[] table = GameShops.Read(code, offset, layout);

        Assert.Equal(26, layout.Sizes.Length);
        Assert.Equal(217, layout.Total);
        Assert.Equal(217, table.Length);
        // The first Poké Mart: a Poké Ball and a Potion.
        Assert.Equal([4, 17], table[..2]);
        // The second one adds the Great Ball and the status healers.
        Assert.Equal([4, 3, 17, 26, 18, 22, 21, 19, 20, 78, 79], table.Skip(layout.Start(1)).Take(11));
        // The TM shops hold only TMs, which is what says the table was read with the right sizes.
        foreach (int shop in new[] { 18, 19, 22, 23 })
        {
            foreach (int item in table.Skip(layout.Start(shop)).Take(layout.Sizes[shop]))
                Assert.True(item is (>= 328 and <= 427) or (>= 618 and <= 620), $"shop {shop} sells item {item}, which is not a TM");
        }
    }

    [Fact]
    public void FreeRareCandies_SellsThemEverywhereAndCostsNothing()
    {
        string dumpDir = RequireDump();
        var project = new Project { DumpDirectory = dumpDir };
        project.Shops.FreeRareCandies = true;
        var session = EditorSession.Open(project);

        var built = ModBuilder.BuildEdits(session);

        // The price, in the item table.
        var layout = GameTitle.X.Layout();
        byte[][] items = new GARC.MemGARC(built["romfs/" + layout.Items]).Files;
        // The game keeps one price and derives both from it, so free to buy is also worth nothing to sell.
        Assert.Equal(0, new Item(items[GameShops.RareCandy]).BuyPrice);
        Assert.Equal(0, new Item(items[GameShops.RareCandy]).SellPrice);
        Assert.Equal(200, new Item(items[4]).BuyPrice); // nothing else moved

        // The shops, in code.bin.
        byte[] original = File.ReadAllBytes(Path.Combine(dumpDir, "exefs", "code.bin"));
        byte[] code = built[ModBuilder.CodeFile];
        Assert.Equal(original.Length, code.Length);
        var shops = GameShops.For(GameTitle.X)!;
        int offset = GameShops.Find(original, shops, 718)!.Value;
        int[] table = GameShops.Read(code, offset, shops);
        // Every ordinary shop with room for it sells them; a one-slot stall keeps what it had.
        foreach (int shop in shops.Regular.Where(s => shops.Sizes[s] > 1))
            Assert.Contains(GameShops.RareCandy, table.Skip(shops.Start(shop)).Take(shops.Sizes[shop]));
        foreach (int shop in shops.Regular.Where(s => shops.Sizes[s] == 1))
            Assert.DoesNotContain(GameShops.RareCandy, table.Skip(shops.Start(shop)).Take(1));
        // Shops that are not ordinary Poké Marts are left alone.
        int[] before = GameShops.Read(original, offset, shops);
        foreach (int shop in Enumerable.Range(0, shops.Sizes.Length).Except(shops.Regular))
        {
            Assert.Equal(before.Skip(shops.Start(shop)).Take(shops.Sizes[shop]),
                table.Skip(shops.Start(shop)).Take(shops.Sizes[shop]));
        }
        // Nothing outside the shop table moved, and inside it exactly one slot per shop that had room.
        int expected = shops.Regular.Count(s => shops.Sizes[s] > 1);
        Assert.Equal(expected, before.Zip(table).Count(p => p.First != p.Second));
        int tableBytes = shops.Total * 2;
        Assert.Equal(0, original.Take(offset).Zip(code.Take(offset)).Count(p => p.First != p.Second));
        Assert.Equal(0, original.Skip(offset + tableBytes).Zip(code.Skip(offset + tableBytes)).Count(p => p.First != p.Second));
    }

    [Fact]
    public void ExtraItems_GoIntoTheOrdinaryShops()
    {
        string dumpDir = RequireDump();
        var project = new Project { DumpDirectory = dumpDir };
        project.Shops.ExtraItems.AddRange([659, 660]); // two Mega Stones of X
        var session = EditorSession.Open(project);

        var built = ModBuilder.BuildEdits(session);

        // No price change was asked for, so the item table is not rebuilt.
        Assert.False(built.ContainsKey("romfs/" + GameTitle.X.Layout().Items));
        var shops = GameShops.For(GameTitle.X)!;
        byte[] original = File.ReadAllBytes(Path.Combine(dumpDir, "exefs", "code.bin"));
        int offset = GameShops.Find(original, shops, 718)!.Value;
        int[] table = GameShops.Read(built[ModBuilder.CodeFile], offset, shops);
        foreach (int shop in shops.Regular.Where(s => shops.Sizes[s] > 2))
        {
            var sold = table.Skip(shops.Start(shop)).Take(shops.Sizes[shop]).ToList();
            Assert.Contains(659, sold);
            Assert.Contains(660, sold);
        }
    }

    [Fact]
    public void AHandEditedPrice_WinsOverTheFreeCandies()
    {
        string dumpDir = RequireDump();
        var project = new Project { DumpDirectory = dumpDir };
        project.Shops.FreeRareCandies = true;
        var session = EditorSession.Open(project);
        session.SetInt(GameTables.Items, GameShops.RareCandy, "price", 10);

        var built = ModBuilder.BuildEdits(session);

        byte[][] items = new GARC.MemGARC(built["romfs/" + GameTitle.X.Layout().Items]).Files;
        Assert.Equal(10, new Item(items[GameShops.RareCandy]).BuyPrice);
    }
}
