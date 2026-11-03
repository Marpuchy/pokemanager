using System.Buffers.Binary;
using System.Text;
using Pokemanager.Model.Difficulty;
using Pokemanager.Model.Dump;
using Pokemanager.Model.Mods;
using Pokemanager.Model.Projects;

namespace Pokemanager.Tests.Editing;

/// <summary>
/// The <c>.rnqs</c> that carries everything: UPR ZX's preset as UPR ZX writes it, then the application's options.
/// A plain preset from UPR ZX must keep importing unchanged.
/// </summary>
public sealed class FullPresetTests
{
    /// <summary>What UPR ZX writes: a version, a length and that many bytes of settings.</summary>
    private static byte[] UprPreset(string settings = "AQIDBAUGBwgJ", int version = 321)
    {
        byte[] text = Encoding.UTF8.GetBytes(settings);
        var file = new byte[8 + text.Length];
        BinaryPrimitives.WriteInt32BigEndian(file, version);
        BinaryPrimitives.WriteInt32BigEndian(file.AsSpan(4), text.Length);
        text.CopyTo(file, 8);
        return file;
    }

    private static Project ProjectWithOptions()
    {
        var project = new Project { DumpDirectory = "game", Game = GameTitle.UltraSun };
        project.Tweaks.TrainerLevelPercent = 30;
        project.Tweaks.WildLevelPercent = -10;
        project.Tweaks.StarterLevelPercent = 50;
        project.Tweaks.ShinyOdds = 512;
        project.Tweaks.LevelCap = 20;
        project.Tweaks.LevelCapByMilestones = true;
        project.Tweaks.LevelCapOverrides[2] = 33;
        project.Tweaks.OriginalStarters = true;
        project.Tweaks.Difficulty = DifficultyLevel.Challenge;
        project.Tweaks.InstalledLevelCap = 18;
        project.Shops.FreeRareCandies = true;
        project.Shops.MegaStonesOnSale = true;
        project.Randomization.AllowAllItems = true;
        project.Randomization.MegaStonesInPool = true;
        project.Mods.Set(ModCatalog.UsumExpansionId, true);
        project.Mods.Set(ModCatalog.FollowerId, true);
        return project;
    }

    [Fact]
    public void APlainUprPreset_IsReadAsItIs_WithNoOptionsOfOurs()
    {
        byte[] upr = UprPreset();

        var (part, options) = FullPreset.Read(upr);

        Assert.Equal(upr, part);
        Assert.Null(options);
    }

    [Fact]
    public void TheExportedFile_StartsWithTheUprPresetByteForByte()
    {
        byte[] upr = UprPreset();

        byte[] file = FullPreset.Write(upr, ProjectWithOptions());

        // UPR ZX reads the version, the length and that many bytes, and never looks past them.
        Assert.True(file.Length > upr.Length);
        Assert.Equal(upr, file[..upr.Length]);
        Assert.Equal(upr, FullPreset.UprPart(file));
    }

    [Fact]
    public void EveryOptionOfOurs_RoundTrips()
    {
        var source = ProjectWithOptions();
        byte[] file = FullPreset.Write(UprPreset(), source);

        var (upr, options) = FullPreset.Read(file);
        var target = new Project { DumpDirectory = "game", Game = GameTitle.UltraSun };
        target.Tweaks.InstalledLevelCap = 40;
        Assert.NotNull(options);
        options.ApplyTo(target);

        Assert.Equal(UprPreset(), upr);
        Assert.Equal(30, target.Tweaks.TrainerLevelPercent);
        Assert.Equal(-10, target.Tweaks.WildLevelPercent);
        Assert.Equal(50, target.Tweaks.StarterLevelPercent);
        Assert.Equal(512, target.Tweaks.ShinyOdds);
        Assert.Equal(20, target.Tweaks.LevelCap);
        Assert.True(target.Tweaks.LevelCapByMilestones);
        Assert.Equal(33, target.Tweaks.LevelCapOverrides[2]);
        Assert.True(target.Tweaks.OriginalStarters);
        Assert.Equal(DifficultyLevel.Challenge, target.Tweaks.Difficulty);
        Assert.True(target.Shops.FreeRareCandies);
        Assert.True(target.Shops.MegaStonesOnSale);
        Assert.True(target.Randomization.AllowAllItems);
        Assert.True(target.Randomization.MegaStonesInPool);
        // What was built is the target's, not the file's.
        Assert.Equal(40, target.Tweaks.InstalledLevelCap);

        // Only the ROM mods travel: an emulator plugin is installed on a computer, not part of the run.
        var (known, unknown) = options.ModsFor(GameTitle.UltraSun);
        Assert.Equal([ModCatalog.UsumExpansionId], known.Select(m => m.Id));
        Assert.Empty(unknown);
        Assert.Empty(options.ModsFor(GameTitle.X).Known);
    }

    [Fact]
    public void ExportingAnImportedFile_DoesNotStackTwoBlocks()
    {
        var project = ProjectWithOptions();
        byte[] once = FullPreset.Write(UprPreset(), project);

        byte[] twice = FullPreset.Write(once, project);

        Assert.Equal(once, twice);
    }

    [Fact]
    public void AModThisVersionDoesNotKnow_IsReportedAndLeftOff()
    {
        var options = new FullPreset { Mods = ["someone-elses-mod", ModCatalog.UsumExpansionId] };

        var (known, unknown) = options.ModsFor(GameTitle.UltraMoon);

        Assert.Equal([ModCatalog.UsumExpansionId], known.Select(m => m.Id));
        Assert.Equal(["someone-elses-mod"], unknown);
    }

    [Fact]
    public void ABrokenBlock_IsAnErrorAndNotAPlainPreset()
    {
        byte[] file = FullPreset.Write(UprPreset(), ProjectWithOptions());
        file[^3] = (byte)'{'; // the JSON no longer parses

        Assert.Throws<InvalidDataException>(() => FullPreset.Read(file));
        Assert.Throws<InvalidDataException>(() => FullPreset.Read(file[..^5])); // shorter than its length says
    }

    [Fact]
    public void TrailingBytesThatAreNotOurs_AreIgnored()
    {
        byte[] upr = UprPreset();
        byte[] file = [.. upr, 0, 1, 2, 3];

        var (part, options) = FullPreset.Read(file);

        Assert.Equal(upr, part);
        Assert.Null(options);
    }
}
