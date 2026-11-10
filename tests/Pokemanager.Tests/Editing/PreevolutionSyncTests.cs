using Pokemanager.Model.Dump;
using Pokemanager.Model.Editing;
using Pokemanager.Model.Edits;
using Pokemanager.Model.Projects;

namespace Pokemanager.Tests.Editing;

/// <summary>Earlier stages following an edited Pokémon (2026-11-10, user).</summary>
public sealed class PreevolutionSyncTests
{
    private const string P = GameTables.Personal;

    [Fact]
    public void Fit_ARaiseGoesMostlyToTheStrongestStats_ACutComesOffEvenly()
    {
        int[] gengar = [60, 65, 60, 130, 75, 110];
        int[] up = StatSpread.Fit(gengar, 600);
        Assert.Equal(600, up.Sum());
        Assert.True(up[3] - 130 > up[0] - 60, "Sp. Atk, the strongest, gains more than HP");
        Assert.True(up[4] - 75 > up[2] - 60);

        int[] down = StatSpread.Fit([105, 110, 125, 55, 100, 105], 530);
        Assert.Equal(530, down.Sum());
        Assert.All(down, v => Assert.InRange(v, 1, 255));

        int[] capped = StatSpread.Fit([250, 10, 10, 10, 10, 10], 400);
        Assert.Equal(400, capped.Sum());
        Assert.Equal(255, capped[0]);
    }

    [Fact]
    public void Fit_KeepsAMegasHp()
    {
        int[] mega = StatSpread.Fit([1, 150, 100, 100, 100, 100], 700, fixedHp: 90);
        Assert.Equal(90, mega[0]);
        Assert.Equal(700, mega.Sum());
    }

    private static EditorSession Real()
    {
        string? dump = Environment.GetEnvironmentVariable("POKEMANAGER_USUM_DUMP");
        Assert.SkipWhen(string.IsNullOrEmpty(dump), "POKEMANAGER_USUM_DUMP is not set.");
        return EditorSession.Open(new Project { DumpDirectory = dump!, Game = GameTitle.UltraSun }, null);
    }

    private static int Total(EditorSession s, int row) => s.Current.Personal[row].BST;

    [Fact]
    public void RealUltraSun_EditingCharizard_MovesCharmanderAndCharmeleon_Idempotently()
    {
        var session = Real();
        int charmander = Total(session, 4), charmeleon = Total(session, 5), charizard = Total(session, 6);
        int charmeleonCatch = session.Current.Personal[5].CatchRate;

        // Charizard 534 → +10% and Fire/Dragon
        foreach (var (field, value) in new[] { ("atk", 100), ("spa", 140), ("spe", 120) })
            session.SetInt(P, 6, field, value);
        session.SetInt(P, 6, "type2", 15);
        double factor = (double)Total(session, 6) / charizard;

        var sync = new PreevolutionSync(session);
        Assert.Equal([5, 4], sync.EarlierStages(6));
        Assert.Equal(2, sync.SyncFrom(6));
        Assert.Equal((int)Math.Round(charmander * factor), Total(session, 4));
        Assert.Equal((int)Math.Round(charmeleon * factor), Total(session, 5));
        Assert.True(session.Current.Personal[5].CatchRate <= charmeleonCatch);
        // Charmeleon never had Charizard's types (Fire against Fire/Flying): it keeps its own.
        Assert.Equal(session.Original.Personal[5].Types[1], session.Current.Personal[5].Types[1]);

        Assert.Equal(0, new PreevolutionSync(session).SyncFrom(6));
        Assert.Equal(0, new PreevolutionSync(session).SyncAll());

        // Back to the ROM's: the earlier stages come back too.
        session.Revert(P, 6);
        new PreevolutionSync(session).SyncFrom(6);
        Assert.False(session.IsModified(P, 4));
        Assert.False(session.IsModified(P, 5));

        // Blastoise Water/Steel: Wartortle (Water, like Blastoise was) takes it, Squirtle — the first stage — does not.
        session.SetInt(P, 9, "type2", 8);
        new PreevolutionSync(session).SyncFrom(9);
        Assert.Equal(8, session.Current.Personal[8].Types[1]);
        Assert.Equal(session.Original.Personal[7].Types[1], session.Current.Personal[7].Types[1]);
    }

    [Fact]
    public void RealUltraSun_SyncAll_LeavesHandEditedEarlierStagesAlone()
    {
        var session = Real();
        session.SetInt(P, 9, "hp", 120);           // Blastoise
        session.SetInt(P, 8, "hp", 99);            // Wartortle, by hand
        int wartortle = Total(session, 8);
        Assert.Equal(1, new PreevolutionSync(session).SyncAll());   // Squirtle only
        Assert.Equal(wartortle, Total(session, 8));
        Assert.True(Total(session, 7) > session.Original.Personal[7].BST);
    }
}
