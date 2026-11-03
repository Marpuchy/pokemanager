using System.Buffers.Binary;
using pk3DS.Core.CTR;
using pk3DS.Core.Structures.PersonalInfo;
using Pokemanager.Model.Data;
using Pokemanager.Model.Dump;
using Pokemanager.Model.Editing;
using Pokemanager.Model.Mods;
using Pokemanager.Model.Projects;
using Pokemanager.Tests.Editing;

namespace Pokemanager.Tests.Mods;

/// <summary>
/// The first of the two places a mod has to enter: <b>reading</b>. The application reads the game through
/// <see cref="RomFsLayers"/>, and a mod's files are one more layer over the dump — without it the application would go
/// on showing the species the game shipped with however carefully the mod was installed.
/// </summary>
/// <remarks>
/// Synthetic data throughout: a dump with five species (<see cref="SyntheticRomFs"/>) and a mod library holding an
/// expanded personal table. No ROM, no mod files, no Java.
/// </remarks>
public sealed class ModLayerTests : IDisposable
{
    private const string Id = "test-expansion";

    /// <summary>What the USUM Expansion Mod reaches, used here as the number to grow to.</summary>
    private const int Expanded = 1025;

    private readonly SyntheticRomFs romfs = new();
    private readonly string library = Directory.CreateTempSubdirectory("pokemanager-mods-layer-").FullName;

    public void Dispose()
    {
        romfs.Dispose();
        try { Directory.Delete(library, recursive: true); } catch (IOException) { }
    }

    private ModLibrary Mods => new(library);

    private Project Plain() => new() { DumpDirectory = romfs.DumpDirectory };

    private Project WithMod()
    {
        var project = Plain();
        project.Mods.Set(Id, true);
        return project;
    }

    /// <summary>
    /// A personal table with <paramref name="lastSpecies"/> species and two alternate form rows after them, which is
    /// what tells the reader where the species end (<see cref="PersonalIndex.DeriveLastSpecies"/>: a table with no
    /// forms says nothing and falls back to the constant). Packed as the game packs it, with the concatenation of
    /// every entry as the last file.
    /// </summary>
    private static byte[] PersonalGarc(int lastSpecies)
    {
        int rows = lastSpecies + 1 + 2;
        var entries = Enumerable.Range(0, rows).Select(_ => new byte[PersonalInfoXY.SIZE]).ToArray();
        int firstForm = lastSpecies + 1;
        foreach (int row in new[] { 3, firstForm, firstForm + 1 })
        {
            entries[row][0x20] = 3; // FormeCount, the species itself included
            BinaryPrimitives.WriteUInt16LittleEndian(entries[row].AsSpan(0x1C), (ushort)firstForm);
        }
        // Something recognisable in the last species' stats, to prove the mod's table is the one being read.
        entries[lastSpecies][0] = 123;
        byte[][] files = [.. entries, entries.SelectMany(e => e).ToArray()];
        return GARC.PackGARC(files, GARC.VER_4, 4).Data;
    }

    /// <summary>Puts a file in the mod library under its served path, as the user dropping a release in would.</summary>
    private void Provide(string servedPath, byte[] data)
    {
        string path = Path.Combine(library, Id, servedPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, data);
    }

    // ------------------------------------------------------------------ the layer

    /// <summary>
    /// With the mod on, an expanded personal table in the library is what the editing session reads: the species count
    /// is the mod's and the ROM is marked expanded. This is the test that would fail if the mod were not a layer.
    /// </summary>
    [Fact]
    public void AnExpandedTableInTheLibraryIsReadAsExpanded()
    {
        Provide("romfs/" + GameData.PersonalGarc, PersonalGarc(Expanded));

        var session = EditorSession.Open(WithMod(), null, Mods);

        Assert.Equal(Expanded, session.Current.LastSpecies);
        Assert.Equal(Expanded + 1, session.Current.SpeciesCount);
        Assert.True(session.Current.IsExpanded);
        Assert.Equal(Expanded + 1 - GameTitle.X.Layout().SpeciesCount, session.Current.ExtraSpecies);
        Assert.Equal(123, session.Current.Personal[Expanded].HP);
    }

    /// <summary>
    /// The same project with the mod off reads the dump: five species, not expanded. So the expansion above comes from
    /// the layer and from nothing else.
    /// </summary>
    [Fact]
    public void WithTheModOffTheDumpIsWhatIsRead()
    {
        Provide("romfs/" + GameData.PersonalGarc, PersonalGarc(Expanded));

        var session = EditorSession.Open(Plain(), null, Mods);

        Assert.Equal(SyntheticRomFs.Species, session.Current.SpeciesCount);
        Assert.False(session.Current.IsExpanded);
    }

    /// <summary>
    /// A file the mod does not replace still comes from the dump: the layer is a stack, not a swap. The mod here
    /// carries only the personal table, so the moves are the dump's.
    /// </summary>
    [Fact]
    public void AFileTheModDoesNotReplaceStillComesFromTheDump()
    {
        Provide("romfs/" + GameData.PersonalGarc, PersonalGarc(Expanded));

        var session = EditorSession.Open(WithMod(), null, Mods);

        Assert.Equal(SyntheticRomFs.MoveCount, session.Current.Moves.Length);
        Assert.Equal(Path.Combine(romfs.RomFs, GameData.MoveGarc.Replace('/', Path.DirectorySeparatorChar)),
            session.Layers.Resolve(GameData.MoveGarc));
    }

    /// <summary>
    /// The order of the stack: the randomization on top, the mod under it, the dump at the bottom. The randomizer ran
    /// on a base ROM that already had the mod, so its output is the newer word on anything it wrote; a file the mod
    /// replaced and the randomizer did not touch has to come from the mod and not from the dump.
    /// </summary>
    [Fact]
    public void TheRandomizationIsOverTheModAndTheModOverTheDump()
    {
        Provide("romfs/" + GameData.PersonalGarc, PersonalGarc(Expanded));
        string randomized = Path.Combine(library, "randomized");
        Directory.CreateDirectory(randomized);

        var layers = WithMod().ReadLayers(randomized, Mods);

        Assert.Equal(new[] { randomized, Path.Combine(library, Id, "romfs"), romfs.RomFs }, layers.Roots.ToArray());
    }

    /// <summary>The mod's own executable is what is read, because it is the code that reads its expanded tables.</summary>
    [Fact]
    public void TheModsExecutableIsTheOneRead()
    {
        var project = WithMod();
        Assert.Equal(Path.Combine(romfs.DumpDirectory, "exefs", "code.bin"), project.CodePath(Mods));

        Provide("exefs/code.bin", [1, 2, 3]);

        Assert.Equal(Path.Combine(library, Id, "exefs", "code.bin"), project.CodePath(Mods));
    }

    // ------------------------------------------------------------------ a project with no mods

    /// <summary>
    /// <b>A project with no mods behaves exactly as it did before mods existed</b>: one layer, the dump's own romfs,
    /// and the dump's own executable. Nothing else of the read path can have moved.
    /// </summary>
    [Fact]
    public void AProjectWithoutModsReadsExactlyWhatItAlwaysRead()
    {
        var project = Plain();

        Assert.True(project.Mods.IsEmpty);
        Assert.Equal(new[] { romfs.RomFs }, project.ReadLayers(null, Mods).Roots.ToArray());
        Assert.Equal(new[] { romfs.RomFs }, project.ReadLayers().Roots.ToArray()); // not even the default library is read
        Assert.Equal(Path.Combine(romfs.DumpDirectory, "exefs", "code.bin"), project.CodePath(Mods));
        Assert.Null(EditorSession.Open(project, null, Mods).RandomizedRomFs);
    }

    /// <summary>
    /// A mod turned on whose files are not there adds no layer, so the application reads the dump instead of failing:
    /// what refuses in that case is the build, which says what is missing.
    /// </summary>
    [Fact]
    public void AModWithoutFilesAddsNoLayer()
    {
        var project = WithMod();

        Assert.Equal(new[] { romfs.RomFs }, project.ReadLayers(null, Mods).Roots.ToArray());
        Assert.Equal(SyntheticRomFs.Species, EditorSession.Open(project, null, Mods).Current.SpeciesCount);
    }

    /// <summary>Turning a mod on and off again leaves the project as it was, and never repeats an id.</summary>
    [Fact]
    public void TurningAModOnTwiceDoesNotRepeatIt()
    {
        var mods = new ModSettings();
        mods.Set(Id, true);
        mods.Set(Id, true);
        Assert.Equal(new[] { Id }, mods.Enabled.ToArray());
        Assert.True(mods.Has(Id));

        mods.Set(Id, false);
        Assert.True(mods.IsEmpty);
        Assert.False(mods.Has(Id));
    }
}
