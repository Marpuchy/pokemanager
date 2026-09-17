using pk3DS.Core.CTR;
using Pokemanager.Model.Build;
using Pokemanager.Model.Data;
using Pokemanager.Model.Dump;
using Pokemanager.Model.Editing;
using Pokemanager.Model.Edits;
using Pokemanager.Model.Projects;

namespace Pokemanager.Tests.Dump;

/// <summary>
/// Trainers read from the real Pokémon X dump (skipped without <c>POKEMANAGER_DUMP</c>). The numbers come from the dump
/// itself, measured while researching the AI: see <c>docs/game-ai.md</c>.
/// </summary>
public class RealDumpTrainerTests
{
    private static string RequireDump()
    {
        string? dumpDir = Environment.GetEnvironmentVariable("POKEMANAGER_DUMP");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(dumpDir), "POKEMANAGER_DUMP is not set.");
        Assert.SkipUnless(Directory.Exists(Path.Combine(dumpDir!, "romfs")), $"{dumpDir} does not contain romfs.");
        return dumpDir!;
    }

    [Fact]
    public void Trainers_AreReadWithTheirAiAndTeam()
    {
        var data = GameData.Load(Path.Combine(RequireDump(), "romfs"));

        Assert.Equal(785, data.Trainers.Length);
        // Gym leader Korrina: AI Basic+Strong+Expert, two Hyper Potions in the bag, three Pokémon.
        var korrina = data.Trainers[21];
        Assert.Equal(0x07, korrina.Ai);
        Assert.Equal(25, korrina.GetItem(0));
        Assert.Equal(25, korrina.GetItem(1));
        Assert.Equal(3, korrina.Count);
        Assert.True(korrina.CustomMoves);
        var mienfoo = korrina.Member(0)!;
        Assert.Equal(619, mienfoo.Species);
        Assert.Equal(29, mienfoo.Level);
        Assert.Equal(150, mienfoo.IvByte); // the leaders' IVs byte

        // The first Youngsters have no AI flags at all, and the game fills their moves from the learnset.
        Assert.Equal(0x00, data.Trainers[1].Ai);
        Assert.False(data.Trainers[1].CustomMoves);

        // Every gym leader, the Elite Four and the Champion use the game's own ceiling.
        Assert.Equal(0x07, data.Trainers[276].Ai); // Diantha
        Assert.Equal(6, data.Trainers[276].Count);
        Assert.Equal(200, data.Trainers[276].Member(0)!.IvByte);
    }

    [Fact]
    public void EveryTrainer_RoundTripsByteForByte()
    {
        string romfs = Path.Combine(RequireDump(), "romfs");
        var layout = GameTitle.X.Layout();
        byte[][] records = new GARC.MemGARC(File.ReadAllBytes(Path.Combine(romfs, layout.TrainerData.Replace('/', Path.DirectorySeparatorChar)))).Files;
        byte[][] teams = new GARC.MemGARC(File.ReadAllBytes(Path.Combine(romfs, layout.TrainerPokemon.Replace('/', Path.DirectorySeparatorChar)))).Files;

        var trainers = GameData.Load(romfs).Trainers;

        for (int i = 0; i < trainers.Length; i++)
        {
            Assert.Equal(records[i], trainers[i].WriteData());
            Assert.Equal(teams[i], trainers[i].WriteTeam());
        }
    }

    [Fact]
    public void AiEdit_ChangesOneByteInTheTrainerArchive()
    {
        string dumpDir = RequireDump();
        var session = EditorSession.Open(new Project { DumpDirectory = dumpDir });
        session.SetInt(GameTables.Trainers, 21, "ai", 0x00);

        var built = ModBuilder.BuildEdits(session);

        var layout = GameTitle.X.Layout();
        Assert.True(built.ContainsKey("romfs/" + layout.TrainerData));
        Assert.True(built.ContainsKey("romfs/" + layout.TrainerPokemon));

        byte[] originalGarc = File.ReadAllBytes(Path.Combine(dumpDir, "romfs", layout.TrainerData.Replace('/', Path.DirectorySeparatorChar)));
        byte[] builtGarc = built["romfs/" + layout.TrainerData];
        Assert.Equal(originalGarc.Length, builtGarc.Length);
        Assert.Equal(1, originalGarc.Zip(builtGarc).Count(p => p.First != p.Second));
        Assert.Equal(0, new GARC.MemGARC(builtGarc).GetFile(21)[0x0C]);

        // The teams were not touched, so that archive comes out identical.
        string teamPath = Path.Combine(dumpDir, "romfs", layout.TrainerPokemon.Replace('/', Path.DirectorySeparatorChar));
        Assert.Equal(File.ReadAllBytes(teamPath), built["romfs/" + layout.TrainerPokemon]);
    }

    [Fact]
    public void TeamEdit_ChangesTheTeamArchive()
    {
        string dumpDir = RequireDump();
        var session = EditorSession.Open(new Project { DumpDirectory = dumpDir });
        session.SetInt(GameTables.Trainers, 21, "p1.level", 60);
        session.SetInt(GameTables.Trainers, 21, "p1.ivs", 255);

        var built = ModBuilder.BuildEdits(session);
        var layout = GameTitle.X.Layout();
        byte[][] teams = new GARC.MemGARC(built["romfs/" + layout.TrainerPokemon]).Files;
        var mienfoo = Trainer.Read(GameTitle.X, new GARC.MemGARC(built["romfs/" + layout.TrainerData]).GetFile(21), teams[21]).Member(0)!;

        Assert.Equal(60, mienfoo.Level);
        Assert.Equal(255, mienfoo.IvByte);
        Assert.Equal(619, mienfoo.Species); // nothing else moved
    }
}
