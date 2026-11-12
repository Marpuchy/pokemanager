using Pokemanager.Model.Data;

namespace Pokemanager.Tests.Editing;

/// <summary>
/// The shiny branch on hand-made data: what it takes to be found, and what it refuses. The real games are covered by
/// <see cref="Dump.RealDumpGameTests"/>.
/// </summary>
public class GameCodeTests
{
    private static readonly byte[] Signature = [0x21, 0xE2, 0x03, 0x20, 0x92, 0xE1, 0x1C];

    private static byte[] Code(int at, byte branch = 0x0A, int length = 0x400, int copies = 1)
    {
        byte[] code = new byte[length];
        for (int copy = 0; copy < copies; copy++)
        {
            int start = at + (copy * 0x100);
            Signature.CopyTo(code, start);
            code[start + 9] = branch;
        }
        return code;
    }

    [Fact]
    public void TheBranchLosesItsCondition()
    {
        byte[] code = Code(0x120);

        Assert.True(GameCode.HasShinyBranch(code));
        Assert.False(GameCode.IsEveryPokemonShiny(code));
        Assert.True(GameCode.MakeEveryPokemonShiny(code));

        Assert.Equal(0xEA, code[0x120 + 9]);
        Assert.True(GameCode.IsEveryPokemonShiny(code));
        // Nothing else moved, and asking twice changes nothing.
        Assert.False(GameCode.MakeEveryPokemonShiny(code));
        Assert.Equal(Signature, code.Skip(0x120).Take(Signature.Length));
        Assert.All(code.Take(0x120), b => Assert.Equal(0, b));
    }

    [Fact]
    public void ASignatureThatIsNotThere_ChangesNothing()
    {
        byte[] code = new byte[0x400];

        Assert.False(GameCode.HasShinyBranch(code));
        Assert.False(GameCode.MakeEveryPokemonShiny(code));
        Assert.All(code, b => Assert.Equal(0, b));
    }

    [Fact]
    public void ASignatureThatAppearsTwice_IsNotTakenForIt()
    {
        byte[] code = Code(0x120, copies: 2);

        Assert.False(GameCode.HasShinyBranch(code));
        Assert.False(GameCode.MakeEveryPokemonShiny(code));
    }

    [Fact]
    public void SomethingElseWhereTheBranchShouldBe_IsRefused()
    {
        byte[] code = Code(0x120, branch: 0x1A); // BNE: not the branch this knows

        Assert.False(GameCode.HasShinyBranch(code));
        Assert.False(GameCode.MakeEveryPokemonShiny(code));
        Assert.Equal(0x1A, code[0x120 + 9]);
    }

    /// <summary>
    /// Odds between "all" and the game's: the two reads of the roll count become a constant, measured in Ultra Sun and
    /// the USUM Expansion at 0x1E and 0xA2 after the signature.
    /// </summary>
    [Fact]
    public void TheRollCountBecomesAConstant()
    {
        byte[] code = Code(0x120);
        BitConverter.TryWriteBytes(code.AsSpan(0x120 + 0x1E, 4), 0xE5D40023u);
        BitConverter.TryWriteBytes(code.AsSpan(0x120 + 0xA2, 4), 0xE5D40023u);

        Assert.True(GameCode.CanSetShinyRolls(code));
        Assert.True(GameCode.SetShinyRolls(code, 16));
        Assert.Equal(0xE3A00010u, BitConverter.ToUInt32(code, 0x120 + 0x1E));
        Assert.Equal(0xE3A00010u, BitConverter.ToUInt32(code, 0x120 + 0xA2));
        Assert.Equal(0x0A, code[0x120 + 9]); // the branch is the game's: this is odds, not "all"
        Assert.True(GameCode.CanSetShinyRolls(code)); // and it can be changed again
        Assert.True(GameCode.SetShinyRolls(code, 4));
        Assert.Equal(0xE3A00004u, BitConverter.ToUInt32(code, 0x120 + 0x1E));
    }

    [Fact]
    public void WithoutTheReads_NothingIsWritten()
    {
        byte[] code = Code(0x120);
        Assert.False(GameCode.CanSetShinyRolls(code));
        Assert.False(GameCode.SetShinyRolls(code, 16));
    }

    [Theory]
    [InlineData(4096, 1)]
    [InlineData(1024, 4)]
    [InlineData(256, 16)]
    [InlineData(2, 2832)]
    [InlineData(16, 264)]
    public void TheOddsBecomeRolls(int odds, int rolls) => Assert.Equal(rolls, GameCode.RollsFor(odds));

    [Fact]
    public void OneInTwo_IsAboutHalf_AndFitsOneInstruction()
    {
        int rolls = GameCode.RollsFor(2);
        Assert.Equal(2, GameCode.OddsFor(rolls));
        byte[] code = Code(0x120);
        BitConverter.TryWriteBytes(code.AsSpan(0x120 + 0x1E, 4), 0xE5D40023u);
        BitConverter.TryWriteBytes(code.AsSpan(0x120 + 0xA2, 4), 0xE5D40023u);
        Assert.True(GameCode.SetShinyRolls(code, rolls));
        // MOV R0, #0xB10 = 0xB1 rotated right by 28.
        Assert.Equal(0xE3A00EB1u, BitConverter.ToUInt32(code, 0x120 + 0x1E));
        Assert.Equal(0xE3A00EB1u, BitConverter.ToUInt32(code, 0x120 + 0xA2));
        Assert.True(GameCode.CanSetShinyRolls(code)); // and it can be changed again
        Assert.True(GameCode.SetShinyRolls(code, 16));
        Assert.Equal(0xE3A00010u, BitConverter.ToUInt32(code, 0x120 + 0x1E));
    }
}
