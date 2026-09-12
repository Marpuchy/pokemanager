using pk3DS.Core.CTR;
using Pokemanager.Model.Dump;

namespace Pokemanager.Tests.Dump;

public class DumpInspectorTests
{
    [Theory]
    [InlineData("Pokémon X", GameTitle.X)]
    [InlineData("Pokémon Y", GameTitle.Y)]
    [InlineData("ポケットモンスター Ｙ", GameTitle.Y)]
    public void ValidDump_IsIdentified(string smdhTitle, GameTitle expected)
    {
        using var dump = new SyntheticDump(smdhTitle);

        var result = DumpInspector.Inspect(dump.RomFs, dump.ExeFs);

        Assert.Empty(result.Problems);
        Assert.True(result.IsValid);
        Assert.Equal(expected, result.Title);
    }

    [Fact]
    public void WrongFileCount_IsReported()
    {
        using var dump = new SyntheticDump();
        dump.WriteRomFsFile("a/9/9/9", []);

        var result = DumpInspector.Inspect(dump.RomFs, dump.ExeFs);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Contains("272 archivos"));
    }

    [Fact]
    public void GarcWithWrongEntryCount_IsReported()
    {
        using var dump = new SyntheticDump();
        dump.WriteGarc("a/2/1/8", entries: 799);

        var result = DumpInspector.Inspect(dump.RomFs, dump.ExeFs);

        Assert.Contains(result.Problems, p => p.Contains("a/2/1/8") && p.Contains("799 entradas"));
    }

    [Fact]
    public void GarcWithGen7Version_IsReported()
    {
        using var dump = new SyntheticDump();
        dump.WriteGarc("a/2/1/2", entries: 618, version: GARC.VER_6);

        var result = DumpInspector.Inspect(dump.RomFs, dump.ExeFs);

        Assert.Contains(result.Problems, p => p.Contains("a/2/1/2") && p.Contains("0x0600"));
    }

    [Fact]
    public void NonGarcFile_IsReported()
    {
        using var dump = new SyntheticDump();
        dump.WriteRomFsFile("a/2/1/8", new byte[0x100]);

        var result = DumpInspector.Inspect(dump.RomFs, dump.ExeFs);

        Assert.Contains(result.Problems, p => p.Contains("a/2/1/8") && p.Contains("no es un GARC"));
    }

    [Fact]
    public void OtherGameTitle_IsNotIdentified()
    {
        using var dump = new SyntheticDump("Pokémon Omega Ruby");

        var result = DumpInspector.Inspect(dump.RomFs, dump.ExeFs);

        Assert.Null(result.Title);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void CompressedCode_IsReported()
    {
        using var dump = new SyntheticDump();
        _ = new BLZCoder(["-en", dump.CodePath]);

        Assert.True(DumpInspector.LooksBlzCompressed(dump.CodePath), "BLZCoder no produjo un archivo comprimido.");
        var result = DumpInspector.Inspect(dump.RomFs, dump.ExeFs);

        Assert.Contains(result.Problems, p => p.Contains("comprimido"));
    }

    [Fact]
    public void AllProblems_AreReportedTogether()
    {
        using var dump = new SyntheticDump();
        dump.WriteRomFsFile("a/9/9/9", []);
        File.Delete(dump.IconPath);
        File.Delete(dump.CodePath);

        var result = DumpInspector.Inspect(dump.RomFs, dump.ExeFs);

        Assert.Equal(3, result.Problems.Count);
    }

    [Fact]
    public void MissingFolders_AreReported()
    {
        var result = DumpInspector.Inspect(@"Z:\no\existe\romfs", @"Z:\no\existe\exefs");

        Assert.Equal(2, result.Problems.Count);
        Assert.Null(result.Title);
    }

    [Fact]
    public void Open_InvalidDump_ThrowsWithProblems()
    {
        using var dump = new SyntheticDump();
        File.Delete(dump.CodePath);

        var ex = Assert.Throws<InvalidDumpException>(() => GameDump.Open(dump.RomFs, dump.ExeFs, GameLanguage.Spanish));

        Assert.Single(ex.Problems);
    }

    [Theory]
    [InlineData(GameTitle.X, "0004000000055D00")]
    [InlineData(GameTitle.Y, "0004000000055E00")]
    public void TitleIdHex_MatchesEmulatorFolderName(GameTitle title, string expected)
    {
        Assert.Equal(expected, title.TitleIdHex());
    }
}
