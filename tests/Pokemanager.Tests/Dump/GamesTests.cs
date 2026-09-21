using System.Text;
using Pokemanager.Model.Data;
using Pokemanager.Model.Dump;

namespace Pokemanager.Tests.Dump;

public class GamesTests
{
    [Fact]
    public void TitleIds_RoundTrip_AndUnknownIsNull()
    {
        foreach (var game in Enum.GetValues<GameTitle>())
            Assert.Equal(game, GameTitleExtensions.FromTitleId(game.TitleId()));
        Assert.Null(GameTitleExtensions.FromTitleId(0x0004000000030800)); // not a Pokémon game
        Assert.Equal(GameTitle.UltraMoon, GameTitleExtensions.FromTitleId(0x00040000001B5100));
    }

    [Fact]
    public void Families_Generations_AndMilestones()
    {
        Assert.Equal(GameFamily.ORAS, GameTitle.AlphaSapphire.Family());
        Assert.Equal(7, GameTitle.Moon.Generation());
        Assert.Equal(8, GameTitle.OmegaRuby.Milestones().Count);
        Assert.Equal(5, GameTitle.UltraSun.Milestones().Count);
        Assert.True(GameTitle.Y.HasBadges());
        Assert.False(GameTitle.Sun.HasBadges());
        // pk3DS archive numbers (GARCReference): personal 218 / 195 / 017.
        Assert.Equal("a/2/1/8", GameTitle.X.Layout().Personal);
        Assert.Equal("a/1/9/5", GameTitle.OmegaRuby.Layout().Personal);
        Assert.Equal("a/0/1/7", GameTitle.UltraMoon.Layout().Personal);
        Assert.Equal("a/0/7/2", GameImporter.GarcPath(72));
    }

    [Fact]
    public void RequiredFiles_TextsInEveryLanguage()
    {
        var files = GameImporter.RequiredFiles(GameTitle.UltraMoon).ToList();
        Assert.Contains("a/0/3/0", files);
        Assert.Contains("a/0/3/9", files); // 10 languages in Gen 7
        Assert.Contains("a/1/0/6", files); // trainers, read since 3.0
        Assert.Contains("a/1/0/7", files);
        Assert.Contains("a/0/1/9", files); // items, read for the shops since 3.0
        Assert.Contains("Shop.cro", files); // and what the shops sell, which Gen 7 keeps apart
        Assert.Contains("a/0/1/5", files); // mega evolutions: where the Mega Stones are named
        Assert.Contains("a/1/5/9", files); // the fixed Pokemon, where the starters are
        Assert.Contains("a/0/1/6", files); // the experience table, for the level cap
        Assert.Contains("a/1/6/2", files); // the player's own portraits, from the screen that starts the adventure
        Assert.Equal(12 + 10, files.Count);
        var x = GameImporter.RequiredFiles(GameTitle.X).ToList();
        Assert.Equal(9 + 8, x.Count); // one fewer than Generation 7: it keeps the shops in code.bin
        Assert.Contains("a/2/1/7", x);
        Assert.Contains("a/0/3/8", x);
        Assert.Contains("a/0/4/0", x);
        Assert.Contains("a/2/2/0", x);
    }

    /// <summary>SARC with two files, names in SFNT at offsets (×4), data relative to the data offset.</summary>
    [Fact]
    public void ReadSarc_FilesByName()
    {
        byte[] sarc = BuildSarc(("timg/a.bflim", [1, 2, 3]), ("timg/stamp.bflim", [9, 8]));

        var files = MilestoneIcons.ReadSarc([0x41, 0x4C, 0x59, 0x54, .. new byte[12], .. sarc])!; // after an ALYT header

        Assert.Equal([1, 2, 3], files["timg/a.bflim"]);
        Assert.Equal([9, 8], MilestoneIcons.Find(files, "STAMP.bflim"));
    }

    /// <summary>One ETC1A4 block in each of the four positions of a 4×4... an 8×8 texture: a solid color with alpha.</summary>
    [Fact]
    public void DecodeFlim_Etc1A4_SolidBlock()
    {
        // Individual mode, table 0, all pixel indexes 0 (+2): base colors 0xA / 0xA (170) → 172.
        ulong color = ((ulong)0xAA << 56) | ((ulong)0x55 << 48) | ((ulong)0x00 << 40);
        ulong alpha = 0xFFFF_FFFF_FFFF_FFFF;
        var data = new List<byte>();
        for (int b = 0; b < 4; b++)
        {
            data.AddRange(BitConverter.GetBytes(alpha));
            data.AddRange(BitConverter.GetBytes(color));
        }
        var footer = new byte[0x28];
        "FLIM"u8.CopyTo(footer);
        "imag"u8.CopyTo(footer.AsSpan(0x14));
        BitConverter.GetBytes((ushort)8).CopyTo(footer, 0x1C);
        BitConverter.GetBytes((ushort)8).CopyTo(footer, 0x1E);
        footer[0x22] = 0x0B; // ETC1A4

        var image = PokemonIcons.DecodeBclim([.. data, .. footer])!;

        Assert.Equal((8, 8), (image.Width, image.Height));
        Assert.All(Enumerable.Range(0, 64), i => Assert.Equal([172, 87, 2, 255], image.Rgba[(i * 4)..((i * 4) + 4)]));
    }

    [Fact]
    public void RealRoms_ImportEveryGame()
    {
        string? roms = Environment.GetEnvironmentVariable("POKEMANAGER_TEST_ROMS");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(roms), "POKEMANAGER_TEST_ROMS (ROM paths separated by ;) is not set.");

        string root = Directory.CreateTempSubdirectory("pokemanager-import-").FullName;
        try
        {
            foreach (string rom in roms!.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var imported = GameImporter.Import(rom, root);
                var dump = GameDump.Open(Path.Combine(imported.Directory, "romfs"), Path.Combine(imported.Directory, "exefs"), GameLanguage.English);
                var data = GameData.Load(new RomFsLayers(Path.Combine(imported.Directory, "romfs")), imported.Game);

                Assert.Equal(imported.Game, dump.Title);
                Assert.Equal("Pikachu", dump.Config.GetText(pk3DS.Core.TextName.SpeciesNames)[25]);
                Assert.Equal(12, data.Personal[25].Types[0]); // Electric
                Assert.True(GameImporter.Import(rom, root).Reused);
                Assert.NotNull(MilestoneIcons.Load(new RomFsLayers(Path.Combine(imported.Directory, "romfs")), imported.Game));
            }
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static byte[] BuildSarc(params (string Name, byte[] Data)[] files)
    {
        var names = new List<byte>();
        var nameOffsets = new List<int>();
        foreach (var (name, _) in files)
        {
            nameOffsets.Add(names.Count / 4);
            names.AddRange(Encoding.ASCII.GetBytes(name + "\0"));
            while (names.Count % 4 != 0)
                names.Add(0);
        }
        int sfatSize = 0x0C + (files.Length * 16);
        int dataOffset = 0x14 + sfatSize + 8 + names.Count;
        var sfat = new List<byte>();
        sfat.AddRange("SFAT"u8.ToArray());
        sfat.AddRange(BitConverter.GetBytes((ushort)0x0C));
        sfat.AddRange(BitConverter.GetBytes((ushort)files.Length));
        sfat.AddRange(BitConverter.GetBytes(0x65));
        var body = new List<byte>();
        for (int i = 0; i < files.Length; i++)
        {
            sfat.AddRange(BitConverter.GetBytes(0));
            sfat.AddRange(BitConverter.GetBytes(0x01000000 | nameOffsets[i]));
            sfat.AddRange(BitConverter.GetBytes(body.Count));
            body.AddRange(files[i].Data);
            sfat.AddRange(BitConverter.GetBytes(body.Count));
        }
        var header = new List<byte>();
        header.AddRange("SARC"u8.ToArray());
        header.AddRange(BitConverter.GetBytes((ushort)0x14));
        header.AddRange(BitConverter.GetBytes((ushort)0xFEFF));
        header.AddRange(BitConverter.GetBytes(dataOffset + body.Count));
        header.AddRange(BitConverter.GetBytes(dataOffset));
        header.AddRange(BitConverter.GetBytes(0x0100));
        return [.. header, .. sfat, .. "SFNT"u8.ToArray(), .. BitConverter.GetBytes((ushort)8), .. BitConverter.GetBytes((ushort)0), .. names, .. body];
    }
}
