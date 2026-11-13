using Pokemanager.Model.Data;
using Xunit;

namespace Pokemanager.Tests.Data;

public class FixedEncountersTests
{
    [Theory]
    [InlineData(FixedKind.Static, 0x38)]
    [InlineData(FixedKind.Trade, 0x34)]
    [InlineData(FixedKind.Gift, 0x14)]
    public void EveryField_ReadsBackWhatWasWritten(FixedKind kind, int size)
    {
        var e = new byte[size];
        (string Field, int Value)[] values =
        [
            ("species", 701), ("form", 1), ("level", 42), ("item", 222), ("nature", 3), ("ability", 2), ("nature", -1),
            ("ability", -1), ("iv0", 31), ("iv5", -1), ("ev2", 252), ("move3", 89), ("requested", 25), ("item", 0),
        ];
        foreach (var (field, value) in values)
        {
            if (!FixedEncounters7.Has(kind, field) || (kind == FixedKind.Trade && (field, value) is ("nature", -1) or ("ability", -1)))
                continue;
            FixedEncounters7.Set(kind, e, field, value);
            Assert.Equal(value, FixedEncounters7.Get(kind, e, field));
        }
    }

    [Fact]
    public void Apply_WritesOnlyTheEditedEntry()
    {
        byte[][] files = [new byte[3 * 0x14], new byte[2 * 0x38], [], [], new byte[2 * 0x34]];
        int changed = FixedEncounters7.Apply(files, new Dictionary<string, Dictionary<string, int>>
        {
            ["s1"] = new() { ["level"] = 50, ["iv0"] = 31 },
            ["t0"] = new() { ["requested"] = 132 },
        });
        Assert.Equal(2, changed);
        Assert.Equal(50, files[1][0x38 + 3]);
        Assert.Equal(0, files[1][3]);
        Assert.Equal(132, BitConverter.ToUInt16(files[4], 0x2C));
    }
}
