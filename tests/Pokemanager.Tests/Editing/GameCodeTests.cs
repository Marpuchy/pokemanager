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
}
