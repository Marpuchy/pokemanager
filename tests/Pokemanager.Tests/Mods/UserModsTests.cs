using System.IO.Compression;
using Pokemanager.Model.Dump;
using Pokemanager.Model.Mods;
using Pokemanager.Model.Projects;

namespace Pokemanager.Tests.Mods;

/// <summary>
/// The user's own mods (2026-11-11, user): a folder of Documents where LayeredFS folders, archives and 3GX plugins are
/// dropped in, edited and turned on like the catalogue's.
/// </summary>
[Collection(nameof(UserModsTests))]
public sealed class UserModsTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("pokemanager-usermods-").FullName;
    private readonly string previous = UserMods.Root;

    public UserModsTests() => UserMods.Root = root;

    public void Dispose()
    {
        UserMods.Root = root + "-gone";
        UserMods.Scan();
        UserMods.Root = previous;
        Directory.Delete(root, true);
        if (Directory.Exists(root + "-gone"))
            Directory.Delete(root + "-gone", true);
    }

    private static void Write(string path, string text = "x")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    [Fact]
    public void AFolderLaidOutDeeper_IsFound_AndItsTitleIdSaysTheGame()
    {
        Write(Path.Combine(root, "Mi parche", "luma", "titles", "00040000001B5100", "romfs", "a", "0", "1", "7"));
        Write(Path.Combine(root, "_notamod", "romfs", "x"));
        Write(Path.Combine(root, "vacia", "readme.txt"));

        UserMods.Scan();
        var mod = Assert.Single(UserMods.Current);
        Assert.Equal("user:Mi parche", mod.Id);
        Assert.Equal("Mi parche", mod.Name);
        Assert.Equal([GameTitle.UltraMoon], mod.Games);
        Assert.False(mod.GamesUnknown);
        Assert.True(mod.ChangesTheRom);
        Assert.Same(mod, ModCatalog.Find("user:Mi parche"));
        Assert.Contains(mod, ModCatalog.For(GameTitle.UltraMoon));
        Assert.DoesNotContain(mod, ModCatalog.For(GameTitle.X));

        // The library reads its files where they are, and the build packs them.
        var library = new ModLibrary(Path.Combine(root, "_library"));
        Assert.EndsWith(Path.Combine("00040000001B5100"), library.DirectoryFor(mod.Id));
        var found = library.Inspect(mod);
        Assert.True(found.CanBuild);
        Assert.Contains(found.Contents, c => c.TitlePath == "romfs/a/0/1/7");
    }

    [Fact]
    public void AZipIsUnpacked_ALoose3gxGetsAFolder_AndTheInfoIsEditable()
    {
        string staging = Path.Combine(root, "_staging");
        Write(Path.Combine(staging, "romfs", "a", "0", "9", "4"));
        ZipFile.CreateFromDirectory(staging, Path.Combine(root, "Modelos.zip"));
        Write(Path.Combine(root, "Seguidor.3gx"), "3gx");

        var report = UserMods.Scan();
        Assert.Equal(2, report.Added.Count);
        Assert.True(File.Exists(Path.Combine(root, UserMods.ArchivesFolder, "Modelos.zip")));   // kept, not unpacked twice
        Assert.False(File.Exists(Path.Combine(root, "Seguidor.3gx")));

        var zip = UserMods.Current.Single(m => m.Id == "user:Modelos");
        Assert.True(zip.GamesUnknown);                       // says nothing: listed everywhere, and says so
        Assert.Contains(zip, ModCatalog.For(GameTitle.X));
        var plugin = UserMods.Current.Single(m => m.Id == "user:Seguidor");
        Assert.True(plugin.IsPlugin);
        Assert.Equal("Seguidor.3gx", plugin.PluginFile!.Path);
        Assert.False(plugin.ChangesTheRom);

        UserMods.SaveInfo(zip.Id, new UserModInfo { Name = "Modelos nuevos", Description = "Modelos 3D\nmás", Games = [GameTitle.UltraSun] });
        var edited = ModCatalog.Find(zip.Id)!;
        Assert.Equal("Modelos nuevos", edited.Name);
        Assert.Equal("Modelos 3D", edited.Summary);
        Assert.Equal([GameTitle.UltraSun], edited.Games);
        UserMods.Scan();                                     // read again from its pokemanager-mod.json
        Assert.Equal("Modelos nuevos", ModCatalog.Find(zip.Id)!.Name);
    }

    [Fact]
    public void TheEnabledOrder_MovesUpAndDown()
    {
        var mods = new ModSettings();
        mods.Set("a", true);
        mods.Set("b", true);
        mods.Set("c", true);                                 // c, b, a: the last one turned on is on top
        Assert.Equal(["c", "b", "a"], mods.Enabled);
        Assert.True(mods.Move("a", -1));
        Assert.Equal(["c", "a", "b"], mods.Enabled);
        Assert.False(mods.Move("c", -1));                    // already first
        Assert.True(mods.Move("c", +2));
        Assert.Equal(["a", "b", "c"], mods.Enabled);
    }
}
