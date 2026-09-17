using Pokemanager.App.Services;

namespace Pokemanager.Tests.App;

public sealed class ItemImageTests
{
    [Theory]
    [InlineData(807, 776)]   // Normalium Z: the bead the bag keeps → the held piece
    [InlineData(825, 794)]   // Pikanium Z, last of the type range
    [InlineData(826, 798)]   // Decidium Z, first of the Pokémon-exclusive range
    [InlineData(834, 806)]   // Mewnium Z
    [InlineData(836, 835)]   // Pikashunium Z
    [InlineData(776, 776)]   // a held piece is itself
    [InlineData(1, 1)]       // and so is any other item
    [InlineData(806, 806)]
    public void CrystalBeads_UseTheIconOfTheirHeldPiece(int item, int piece) =>
        Assert.Equal(piece, PkhexImages.Piece(item));
}
