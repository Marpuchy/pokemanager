using PKHeX.Core;
using Pokemanager.Save;
using Pokemanager.Tests.Editing;
using GameData = Pokemanager.Model.Data.GameData;

namespace Pokemanager.Tests.Save;

/// <summary>The save editor on every supported game's save format (blank saves made by PKHeX).</summary>
public sealed class SaveGamesTests : IDisposable
{
    private readonly SyntheticRomFs romfs = new();
    private readonly string dir = Directory.CreateTempSubdirectory("pokemanager-savegames-").FullName;

    public void Dispose()
    {
        romfs.Dispose();
        Directory.Delete(dir, true);
    }

    public static TheoryData<string> Games => ["XY", "AO", "SM", "USUM"];

    private static SaveFile Blank(string game) => game switch
    {
        "XY" => new SAV6XY(),
        "AO" => new SAV6AO(),
        "SM" => new SAV7SM(),
        _ => new SAV7USUM(),
    };

    private SaveDocument Open(string game)
    {
        var sav = Blank(game);
        sav.OT = "Test";
        sav.Language = (int)LanguageID.English;
        string path = Path.Combine(dir, game + "-main");
        File.WriteAllBytes(path, sav.Write().ToArray());
        var doc = SaveDocument.Open(path, GameData.Load(romfs.RomFs));
        doc.Set(new SaveSlot(null, 0), doc.Create(1, 20));
        return doc;
    }

    [Theory]
    [MemberData(nameof(Games))]
    public void Opens_CreatesPokemon_WritesAndRereads(string game)
    {
        var doc = Open(game);
        doc.Money = 12345;
        Assert.Equal(game is "XY" or "AO" ? 6 : 7, doc.Generation);

        doc.Write(Path.Combine(dir, "backups"));
        var reread = SaveDocument.Open(doc.SavePath, GameData.Load(romfs.RomFs));

        var pk = reread.Get(new SaveSlot(null, 0));
        Assert.Equal(1, pk.Species);
        Assert.Equal(game is "XY" or "AO" ? typeof(PK6) : typeof(PK7), pk.GetType());
        Assert.Equal(20, reread.Level(pk));
        Assert.Equal(12345u, reread.Money);
        Assert.Equal("Test", reread.TrainerName);
    }

    [Theory]
    [MemberData(nameof(Games))]
    public void Milestones_BadgesInGen6_StampsInGen7(string game)
    {
        var doc = Open(game);
        Assert.Equal(game is "XY" or "AO" ? 8 : 5, doc.MilestoneCount);

        doc.SetMilestone(0, true);
        doc.SetMilestone(doc.MilestoneCount - 1, true);
        doc.Write(Path.Combine(dir, "backups"));
        var reread = SaveDocument.Open(doc.SavePath, GameData.Load(romfs.RomFs));

        Assert.Equal([true, false, true], new[] { reread.GetMilestone(0), reread.GetMilestone(1), reread.GetMilestone(doc.MilestoneCount - 1) });
        if (Blank(game) is SAV7)
        {
            // Gen 7: stamp 1 is the Melemele trial, stamp 5 the completed island challenge (PKHeX Stamp7).
            byte[] bytes = File.ReadAllBytes(doc.SavePath);
            SAV7 sav = game == "SM" ? new SAV7SM(bytes) : new SAV7USUM(bytes);
            Assert.Equal((1u << (int)Stamp7.MelemeleTrialCompletion) | (1u << (int)Stamp7.IslandChallengeCompletion), sav.Misc.Stamps);
        }
    }

    [Theory]
    [MemberData(nameof(Games))]
    public void GiveItem_WorksInEveryBag(string game)
    {
        var doc = Open(game);

        Assert.Equal(3, doc.GiveItem(50, 3)); // Rare Candy
        Assert.Equal(1, doc.GiveItem(1, 1)); // Master Ball
    }

    [Theory]
    [MemberData(nameof(Games))]
    public void GameSpecificExtras_OnlyWhereTheGameHasThem(string game)
    {
        var doc = Open(game);

        Assert.Equal(game is "XY" or "AO", doc.HasGen6Extras);
        Assert.Equal(game == "XY", doc.HasXYExtras);
        Assert.Equal(game is "SM" or "USUM", doc.HasZMoves);
        Assert.NotEmpty(doc.RecordNames);

        // Calling what the game lacks does nothing instead of failing.
        doc.UnlockAllFriendSafari();
        doc.FillPokePuffs();
        doc.SetSaying(0, "Hi");
        doc.MegaEvolutionUnlocked = true;
        Assert.True(doc.MegaEvolutionUnlocked);
    }
}
