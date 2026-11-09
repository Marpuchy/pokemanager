using Pokemanager.Model.Data;

namespace Pokemanager.Tests.Editing;

/// <summary>The Ultra Space evolutions UPR ZX drops, put back (values measured on the user's Ultra Moon + Expansion ROM).</summary>
public sealed class EvolutionRepairTests
{
    private static byte[] Record(params (int Method, int Arg, int Species, int Form, int Level)[] entries)
    {
        var record = new byte[64];
        for (int i = 0; i < entries.Length; i++)
        {
            var (m, a, s, f, l) = entries[i];
            BitConverter.TryWriteBytes(record.AsSpan(i * 8), (ushort)m);
            BitConverter.TryWriteBytes(record.AsSpan(i * 8 + 2), (ushort)a);
            BitConverter.TryWriteBytes(record.AsSpan(i * 8 + 4), (ushort)s);
            record[i * 8 + 6] = (byte)(sbyte)f;
            record[i * 8 + 7] = (byte)l;
        }
        return record;
    }

    private static (int, int, int, int, int) At(byte[] r, int i) => (BitConverter.ToUInt16(r, i * 8), BitConverter.ToUInt16(r, i * 8 + 2),
        BitConverter.ToUInt16(r, i * 8 + 4), (sbyte)r[i * 8 + 6], r[i * 8 + 7]);

    [Fact]
    public void Pikachu_GetsTheRaichuTheRandomizerDropped()
    {
        byte[][] original = [Record((8, 83, 26, 1, 0), (42, 83, 26, 0, 0))];
        byte[][] randomized = [Record((8, 83, 26, 1, 0))];

        Assert.Equal(1, EvolutionRepair.Repair(original, randomized, _ => 0));
        Assert.Equal((8, 83, 26, 1, 0), At(randomized[0], 0));   // the stone still gives Alolan Raichu
        Assert.Equal((19, 83, 26, 0, 0), At(randomized[0], 1));  // holding it and levelling up gives Raichu
        Assert.Equal((20, 83, 26, 0, 0), At(randomized[0], 2));
        Assert.Equal(0, EvolutionRepair.Repair(original, randomized, _ => 0)); // a second pass changes nothing
    }

    [Fact]
    public void Cubone_SplitsTheTwoMarowaksByTime()
    {
        byte[][] original = [Record((41, 0, 105, 0, 28), (4, 0, 105, 1, 28))];
        byte[][] randomized = [Record((4, 0, 105, 1, 28))];

        Assert.Equal(1, EvolutionRepair.Repair(original, randomized, _ => 0));
        Assert.Equal((32, 0, 105, 1, 28), At(randomized[0], 0)); // by day, the one the randomizer kept
        Assert.Equal((33, 0, 105, 0, 28), At(randomized[0], 1)); // by night, the one it dropped
    }

    [Fact]
    public void RandomizedEvolutions_AreLeftAlone()
    {
        byte[][] original = [Record((8, 83, 26, 1, 0), (42, 83, 26, 0, 0))];
        byte[] other = Record((8, 83, 400, 0, 0));
        byte[][] randomized = [(byte[])other.Clone()];

        Assert.Equal(0, EvolutionRepair.Repair(original, randomized, _ => 0));
        Assert.Equal(other, randomized[0]);
    }
}
