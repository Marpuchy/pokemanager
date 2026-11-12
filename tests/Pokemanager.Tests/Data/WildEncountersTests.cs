using pk3DS.Core;
using pk3DS.Core.CTR;
using Pokemanager.Model.Data;
using Pokemanager.Model.Projects;

namespace Pokemanager.Tests.Data;

public class WildEncountersTests
{
    private const int Half = 0x164;

    /// <summary>An area file as the game has it: LZ11 around an EA pack of one table (4 bytes, day half, night half).</summary>
    private static byte[] Area(params (int Species, int Form, int Rate)[] normal)
    {
        var table = new byte[4 + 2 * Half];
        foreach (int half in new[] { 4, 4 + Half })
        {
            table[half] = 10;
            table[half + 1] = 12;
            for (int slot = 0; slot < normal.Length; slot++)
            {
                table[half + 2 + slot] = (byte)normal[slot].Rate;
                BitConverter.TryWriteBytes(table.AsSpan(half + 0xC + 4 * slot, 4), (uint)(normal[slot].Species | normal[slot].Form << 11));
            }
            // An SOS slot (row 1) with the first Pokémon too.
            BitConverter.TryWriteBytes(table.AsSpan(half + 0xC + 4 * 10, 4), (uint)normal[0].Species);
        }
        return LZSS.Compress(Mini.PackMini([table], "EA"));
    }

    private static byte[][] Archive(byte[] area)
    {
        var files = Enumerable.Range(0, 20).Select(_ => Array.Empty<byte>()).ToArray();
        files[WildEncounters7.FileOfArea(0)] = area;
        return files;
    }

    [Fact]
    public void ASwapChangesThatPokemonInEverySlotOfTheArea_AndNothingElse()
    {
        var files = Archive(Area((731, 0, 60), (734, 0, 40)));
        var edits = new EncounterEdits();
        edits.Wild[0] = new() { [EncounterEdits.Key(731, 0)] = EncounterEdits.Key(25, 0) };

        // Day and night, normal and SOS slot: four.
        Assert.Equal(4, WildEncounters7.Apply(files, edits));
        var mons = WildEncounters7.Summarize(files[WildEncounters7.FileOfArea(0)]);
        Assert.DoesNotContain(mons, m => m.Species == 731);
        Assert.Contains(mons, m => m.Species == 25 && m.Share == 60);
        Assert.Contains(mons, m => m.Species == 734 && m.Share == 40);
        // Applying it again finds nothing to change.
        Assert.Equal(0, WildEncounters7.Apply(files, edits));
    }

    [Fact]
    public void LevelsMove_WithinOneToAHundred()
    {
        var files = Archive(Area((731, 0, 100)));
        var edits = new EncounterEdits();
        edits.WildLevels[0] = 95;
        WildEncounters7.Apply(files, edits);
        var mon = Assert.Single(WildEncounters7.Summarize(files[WildEncounters7.FileOfArea(0)]));
        Assert.Equal((100, 100), (mon.MinLevel, mon.MaxLevel));
    }

    [Fact]
    public void AnAreaWithoutThatPokemon_IsLeftAsItIs()
    {
        byte[] area = Area((731, 0, 100));
        var files = Archive(area);
        var edits = new EncounterEdits();
        edits.Wild[0] = new() { [EncounterEdits.Key(10, 0)] = EncounterEdits.Key(25, 0) };
        Assert.Equal(0, WildEncounters7.Apply(files, edits));
        Assert.Same(area, files[WildEncounters7.FileOfArea(0)]);
    }

    [Fact]
    public void GiftsFixedAndTrades_GetTheirNewPokemon()
    {
        byte[][] files = [new byte[WildEncounters7.GiftSize * 3], new byte[WildEncounters7.StaticSize * 2], [], [], new byte[WildEncounters7.TradeSize]];
        var edits = new EncounterEdits();
        edits.Gifts[1] = EncounterEdits.Key(4, 0);
        edits.Statics[0] = EncounterEdits.Key(150, 0);
        edits.Trades[0] = EncounterEdits.Key(132, 0);
        Assert.Equal(3, WildEncounters7.ApplyStatics(files, edits));
        Assert.Equal(4, WildEncounters7.Gifts(files)[1].Species);
        Assert.Equal(150, WildEncounters7.Statics(files)[0].Species);
        Assert.Equal(132, WildEncounters7.Trades(files)[0].Species);
        Assert.Equal(0, WildEncounters7.ApplyStatics(files, edits));
    }

    [Fact]
    public void TheChangesAreInTheProjectsFingerprint()
    {
        var a = new Project { DumpDirectory = "x" };
        var b = new Project { DumpDirectory = "x" };
        b.Tweaks.Encounters.Gifts[0] = EncounterEdits.Key(4, 0);
        Assert.NotEqual(ProjectHistory.Fingerprint(a), ProjectHistory.Fingerprint(b));
        Assert.Equal(b.Tweaks.Encounters.Fingerprint(), b.Tweaks.Clone().Encounters.Fingerprint());
    }
}
