using System.Buffers.Binary;
using pk3DS.Core.CTR;
using pk3DS.Core.Structures.PersonalInfo;
using Pokemanager.Model.Data;
using Pokemanager.Model.Dump;

namespace Pokemanager.Tests.Editing;

/// <summary>
/// How many species a ROM has comes from its own personal table, not from a constant, so a ROM with a species-expansion
/// mod (the USUM one adds the Pokémon of Generations 8 and 9) is not cut short.
/// </summary>
/// <remarks>
/// The safety net is <see cref="DerivedCountMatchesTheConstantOnAStockTable"/>: on a table shaped like the stock game's
/// the derived value has to be **exactly** the constant the application has always used. The tables here are synthetic —
/// no dump is needed — but they have the real shape: the species in order from row 1, then one row per alternate form,
/// handed out in an order that is not the species' order (row 722 is a Deoxys form in Pokémon X, not a Venusaur one),
/// and each form row carrying a copy of its species' pointer, which the derivation has to ignore.
/// </remarks>
public class SpeciesCountTests
{
    /// <summary>Species with alternate forms, in the order the form rows are handed out (so the first one owns the lowest).</summary>
    private static readonly (int Species, int Forms)[] WithForms = [(386, 2), (3, 1), (700, 3)];

    private static int EntrySize(GameTitle title) => title.Family() switch
    {
        GameFamily.XY => PersonalInfoXY.SIZE,
        GameFamily.ORAS => PersonalInfoORAS.SIZE,
        _ => PersonalInfoSM.SIZE,
    };

    private static PersonalInfoXY Entry(GameTitle title, byte[] data) => title.Family() switch
    {
        GameFamily.XY => new PersonalInfoXY(data),
        GameFamily.ORAS => (PersonalInfoXY)new PersonalInfoORAS(data),
        _ => new PersonalInfoSM(data),
    };

    /// <summary>
    /// The entries of a personal table with <paramref name="lastSpecies"/> species (row 0 included) and the forms of
    /// <see cref="WithForms"/> after them. The final concatenation the GARC also holds is not one of these.
    /// </summary>
    private static byte[][] PersonalEntries(GameTitle title, int lastSpecies)
    {
        int size = EntrySize(title);
        int rows = lastSpecies + 1 + WithForms.Sum(f => f.Forms);
        var entries = Enumerable.Range(0, rows).Select(_ => new byte[size]).ToArray();
        int next = lastSpecies + 1;
        foreach (var (species, forms) in WithForms)
        {
            Assert.True(species <= lastSpecies, "the species that own the forms have to be inside the table");
            // FormeCount counts the species itself; FormStatsIndex is where its forms start.
            foreach (int row in new[] { species }.Concat(Enumerable.Range(next, forms)))
            {
                entries[row][0x20] = (byte)(forms + 1);
                BinaryPrimitives.WriteUInt16LittleEndian(entries[row].AsSpan(0x1C), (ushort)next);
            }
            next += forms;
        }
        return entries;
    }

    private static PersonalInfoXY[] Table(GameTitle title, int lastSpecies) =>
        [.. PersonalEntries(title, lastSpecies).Select(e => Entry(title, e))];

    /// <summary>
    /// The personal GARC the game has: one file per entry plus the concatenation of all of them as the last one, which
    /// the reader drops. Packed as the synthetic romfs packs its own (Generation 6's GARC version).
    /// </summary>
    private static byte[] PersonalGarc(GameTitle title, int lastSpecies)
    {
        var entries = PersonalEntries(title, lastSpecies);
        byte[][] files = [.. entries, entries.SelectMany(e => e).ToArray()];
        return GARC.PackGARC(files, GARC.VER_4, 4).Data;
    }

    // ------------------------------------------------------------------ the safety net

    /// <summary>
    /// A stock table gives exactly the number of species the application has always used, in the four games. If this
    /// ever fails the derivation and the constant are counting different things, and the constant is the one that was
    /// measured on the real dumps: fix the derivation, never the constant.
    /// </summary>
    [Theory]
    [InlineData(GameTitle.X)]
    [InlineData(GameTitle.Y)]
    [InlineData(GameTitle.OmegaRuby)]
    [InlineData(GameTitle.AlphaSapphire)]
    [InlineData(GameTitle.Sun)]
    [InlineData(GameTitle.Moon)]
    [InlineData(GameTitle.UltraSun)]
    [InlineData(GameTitle.UltraMoon)]
    public void DerivedCountMatchesTheConstantOnAStockTable(GameTitle title)
    {
        int constant = PersonalIndex.LastSpecies(title);
        Assert.Equal(constant, PersonalIndex.DeriveLastSpecies(Table(title, constant), title));
    }

    /// <summary>
    /// The two constants count the same thing one apart: <see cref="GameLayout.SpeciesCount"/> includes species 0 and
    /// <see cref="PersonalIndex.LastSpecies"/> is the last one. <c>GameData.IsExpanded</c> compares the derived count
    /// against the first, so they have to stay in step.
    /// </summary>
    [Theory]
    [InlineData(GameTitle.X)]
    [InlineData(GameTitle.Y)]
    [InlineData(GameTitle.OmegaRuby)]
    [InlineData(GameTitle.AlphaSapphire)]
    [InlineData(GameTitle.Sun)]
    [InlineData(GameTitle.Moon)]
    [InlineData(GameTitle.UltraSun)]
    [InlineData(GameTitle.UltraMoon)]
    public void TheTwoConstantsAgree(GameTitle title) =>
        Assert.Equal(title.Layout().SpeciesCount, PersonalIndex.LastSpecies(title) + 1);

    /// <summary>
    /// And both agree with pk3DS's own constant, which is what the fork's <c>GameInfo.MaxSpeciesID</c> hands out. Only
    /// the Generation 6 one can be read from here (the Generation 7 ones are <c>internal</c> to the fork), but it is the
    /// same relation: the last species, one below the count.
    /// </summary>
    [Theory]
    [InlineData(GameTitle.X)]
    [InlineData(GameTitle.OmegaRuby)]
    public void TheConstantsAgreeWithPk3ds(GameTitle title)
    {
        Assert.Equal(pk3DS.Core.Legal.MaxSpeciesID_6, PersonalIndex.LastSpecies(title));
        Assert.Equal(pk3DS.Core.Legal.MaxSpeciesID_6 + 1, title.Layout().SpeciesCount);
    }

    // ------------------------------------------------------------------ the derivation itself

    /// <summary>A table with the species of the Expansion Mod gives its count, not the game's.</summary>
    [Fact]
    public void DerivedCountFollowsAnExpandedTable()
    {
        const int expanded = 1025; // what the USUM Expansion Mod reaches
        Assert.Equal(expanded, PersonalIndex.DeriveLastSpecies(Table(GameTitle.UltraMoon, expanded), GameTitle.UltraMoon));
    }

    /// <summary>
    /// A table with no alternate forms says nothing about where the species end (no pointer to read), so it falls back
    /// to the game's own count, and to the rows that are there when it has fewer.
    /// </summary>
    [Fact]
    public void ATableWithoutFormsFallsBackToTheConstant()
    {
        var small = Enumerable.Range(0, 5).Select(_ => new PersonalInfoXY(new byte[PersonalInfoXY.SIZE])).ToArray();
        Assert.Equal(4, PersonalIndex.DeriveLastSpecies(small, GameTitle.X));
        Assert.Equal(0, PersonalIndex.DeriveLastSpecies([], GameTitle.X));
    }

    // ------------------------------------------------------------------ through GameData

    /// <summary>
    /// A stock-shaped table read through <see cref="GameData"/>: the count is the game's and the ROM is not marked as
    /// expanded. The personal GARC is layered over the synthetic romfs, the way a mod's files sit over a dump.
    /// </summary>
    [Fact]
    public void GameDataReadsTheStockCountAndMarksTheRomStock()
    {
        using var romfs = new SyntheticRomFs();
        using var layer = new PersonalOverride(PersonalGarc(GameTitle.X, PersonalIndex.LastSpecies(GameTitle.X)));
        var data = GameData.Load(new RomFsLayers(layer.Root, romfs.RomFs), GameTitle.X);

        Assert.Equal(GameTitle.X.Layout().SpeciesCount, data.SpeciesCount);
        Assert.Equal(PersonalIndex.LastSpecies(GameTitle.X), data.LastSpecies);
        Assert.False(data.IsExpanded);
        Assert.Equal(0, data.ExtraSpecies);
    }

    /// <summary>
    /// A table with more species than the game ships with: the count grows, the ROM is marked as expanded and the rows
    /// of the extra species are their own, not forms of something else.
    /// </summary>
    [Fact]
    public void GameDataReadsAnExpandedTableAndMarksTheRomExpanded()
    {
        const int expanded = 1025;
        using var romfs = new SyntheticRomFs();
        using var layer = new PersonalOverride(PersonalGarc(GameTitle.X, expanded));
        var data = GameData.Load(new RomFsLayers(layer.Root, romfs.RomFs), GameTitle.X);

        Assert.Equal(expanded, data.LastSpecies);
        Assert.Equal(expanded + 1, data.SpeciesCount);
        Assert.True(data.IsExpanded);
        Assert.Equal(expanded + 1 - GameTitle.X.Layout().SpeciesCount, data.ExtraSpecies);

        // The rows above the stock count are species of their own, and the forms still land where their pointers say.
        var index = PersonalIndex.Of(data);
        Assert.Equal(expanded, index.Row(expanded, 0));
        Assert.Equal(900, index.Row(900, 0));
        Assert.Equal((900, 0), index.Pokemon(900)!.Value);
        Assert.Equal((386, 1), index.Pokemon(expanded + 1)!.Value);
        Assert.Equal(expanded + 1, index.Row(386, 1));
    }

    /// <summary>The synthetic romfs as the other tests use it: five species, no forms, nothing breaks.</summary>
    [Fact]
    public void TheSyntheticRomFsStillReadsAsBefore()
    {
        using var romfs = new SyntheticRomFs();
        var data = GameData.Load(romfs.RomFs);

        Assert.Equal(SyntheticRomFs.Species, data.Personal.Length);
        Assert.Equal(SyntheticRomFs.Species - 1, data.LastSpecies);
        Assert.Equal(SyntheticRomFs.Species, data.SpeciesCount);
        Assert.False(data.IsExpanded);

        var index = PersonalIndex.Of(data);
        Assert.Equal(1, index.Row(1, 0));
        Assert.Equal(SyntheticRomFs.Species - 1, index.Row(SyntheticRomFs.Species - 1, 0));
        Assert.False(index.Pokemon(0).HasValue); // row 0 is the empty placeholder, not a Pokémon
    }

    // ------------------------------------------------------------------ on the real dump

    /// <summary>
    /// The same check on the user's own dump, which is the only place the real table can be read (skipped without
    /// <c>POKEMANAGER_DUMP</c>; <c>GamesTests.RealRoms_ImportEveryGame</c> does it for every ROM in
    /// <c>POKEMANAGER_TEST_ROMS</c>). A stock dump has to give exactly the constant.
    /// </summary>
    [Fact]
    public void TheRealDumpGivesTheConstant()
    {
        string? dumpDir = Environment.GetEnvironmentVariable("POKEMANAGER_DUMP");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(dumpDir), "POKEMANAGER_DUMP is not set.");
        string romFs = Path.Combine(dumpDir!, "romfs"), exeFs = Path.Combine(dumpDir!, "exefs");
        Assert.SkipUnless(Directory.Exists(romFs) && Directory.Exists(exeFs), $"{dumpDir} does not contain romfs and exefs.");

        var title = GameDump.Open(romFs, exeFs, GameLanguage.English).Title;
        var data = GameData.Load(new RomFsLayers(romFs), title);

        Assert.Equal(PersonalIndex.LastSpecies(title), data.LastSpecies);
        Assert.Equal(title.Layout().SpeciesCount, data.SpeciesCount);
        Assert.False(data.IsExpanded);
    }

    /// <summary>A romfs layer that holds nothing but a personal GARC, to stack over a dump or the synthetic romfs.</summary>
    private sealed class PersonalOverride : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("pokemanager-personal-").FullName;

        public PersonalOverride(byte[] garc)
        {
            string path = Path.Combine(Root, GameData.PersonalGarc.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, garc);
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch (IOException) { }
        }
    }
}
