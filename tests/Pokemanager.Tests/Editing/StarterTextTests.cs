using Pokemanager.Model.Data;
using Pokemanager.Model.Dump;

namespace Pokemanager.Tests.Editing;

/// <summary>
/// The starter scene put right, on the lines the real games use. Taken from the user's Ultra Moon, where a
/// randomization had rewritten the English text and left every other language naming Rowlet, Litten and Popplio.
/// </summary>
public class StarterTextTests
{
    private static readonly GameStarters.StarterSwap[] Swaps =
    [
        new("Rowlet", "Tepig", "Planta", "Fuego"),
        new("Litten", "Tynamo", "Fuego", "Eléctrico"),
        new("Popplio", "Igglybuff", "Agua", "Normal"),
    ];

    [Fact]
    public void TheSceneNamesWhatTheGameGives()
    {
        string[] lines =
        [
            "¿Con cuál de estos Pokémon te quedas?",
            "¡Rowlet es capaz de volar sin hacer ni un ruido!",
            "Litten usa movimientos de tipo Fuego, ¡pero mantiene la sangre fría!",
            "¡Popplio es un Pokémon muy diligente!",
        ];

        int changed = GameStarters.Rewrite(lines, Swaps);

        Assert.Equal(3, changed);
        Assert.Equal("¿Con cuál de estos Pokémon te quedas?", lines[0]);
        Assert.Equal("¡Tepig es capaz de volar sin hacer ni un ruido!", lines[1]);
        // The type the sentence names belongs to the Pokémon that was there, so it moves with the name.
        Assert.Equal("Tynamo usa movimientos de tipo Eléctrico, ¡pero mantiene la sangre fría!", lines[2]);
        Assert.Equal("¡Igglybuff es un Pokémon muy diligente!", lines[3]);
    }

    [Fact]
    public void ALineTheGameFillsIn_StillGetsItsType()
    {
        // Spanish asks with a variable for the name and the type written out, one line per slot.
        string[] lines =
        [
            "¿Te decantas por [VAR 0101(0001)], el Pokémon de tipo Planta?",
            "¿Te decantas por [VAR 0101(0002)], el Pokémon de tipo Fuego?",
            "¿Te decantas por [VAR 0101(0003)], el Pokémon de tipo Agua?",
        ];

        Assert.Equal(3, GameStarters.Rewrite(lines, Swaps));

        Assert.Equal("¿Te decantas por [VAR 0101(0001)], el Pokémon de tipo Fuego?", lines[0]);
        Assert.Equal("¿Te decantas por [VAR 0101(0002)], el Pokémon de tipo Eléctrico?", lines[1]);
        Assert.Equal("¿Te decantas por [VAR 0101(0003)], el Pokémon de tipo Normal?", lines[2]);
    }

    [Fact]
    public void ANameThatBecomesAnotherStarter_IsNotSwappedTwice()
    {
        // The randomizer handed slot 1 the Pokémon that used to be slot 2: one pass, so Litten does not become Popplio.
        GameStarters.StarterSwap[] swaps =
        [
            new("Rowlet", "Litten"),
            new("Litten", "Popplio"),
            new("Popplio", "Rowlet"),
        ];
        string[] lines = ["Rowlet, Litten y Popplio te esperan."];

        Assert.Equal(1, GameStarters.Rewrite(lines, swaps));

        Assert.Equal("Litten, Popplio y Rowlet te esperan.", lines[0]);
    }

    [Fact]
    public void AGameThatGivesTheSameThree_ChangesNothing()
    {
        string[] lines = ["¡Rowlet es capaz de volar!", "Litten usa movimientos de tipo Fuego."];
        GameStarters.StarterSwap[] same = [new("Rowlet", "Rowlet", "Planta", "Planta"), new("Litten", "Litten", "Fuego", "Fuego")];

        Assert.Equal(0, GameStarters.Rewrite(lines, same));

        Assert.Equal("¡Rowlet es capaz de volar!", lines[0]);
    }

    [Fact]
    public void EveryGameSaysWhichThreeItShipsWith()
    {
        Assert.Equal([650, 653, 656], GameStarters.Vanilla(GameTitle.X));
        Assert.Equal([252, 255, 258], GameStarters.Vanilla(GameTitle.AlphaSapphire));
        Assert.Equal([722, 725, 728], GameStarters.Vanilla(GameTitle.UltraMoon));

        // Generation 7 knows where they are; Generation 6 keeps them in a CRO module and is not offered.
        Assert.NotEqual("", GameTitle.UltraSun.Layout().Statics);
        Assert.Equal("", GameTitle.X.Layout().Statics);
        Assert.Equal(39, GameTitle.UltraMoon.Layout().StarterTextFile);
        Assert.Equal(-1, GameTitle.OmegaRuby.Layout().StarterTextFile);
    }
}
