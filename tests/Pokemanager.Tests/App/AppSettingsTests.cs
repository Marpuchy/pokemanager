using Pokemanager.App.Services;

namespace Pokemanager.Tests.App;

public sealed class AppSettingsTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("pokemanager-settings-").FullName;

    public void Dispose() => Directory.Delete(dir, true);

    [Fact]
    public void SettingsNotLoadedFromDisk_NeverWriteTheUserFile()
    {
        DateTime? before = File.Exists(AppSettings.DefaultFilePath) ? File.GetLastWriteTimeUtc(AppSettings.DefaultFilePath) : null;

        var settings = new AppSettings { EmulatorUserDirectory = dir };
        settings.TouchProject(Path.Combine(dir, "p.json"));
        settings.Save();

        DateTime? after = File.Exists(AppSettings.DefaultFilePath) ? File.GetLastWriteTimeUtc(AppSettings.DefaultFilePath) : null;
        Assert.Equal(before, after);
        Assert.Null(settings.FilePath);
    }

    [Fact]
    public void RecentProjects_MostRecentFirst_NoDuplicates_SurvivesReload()
    {
        string file = Path.Combine(dir, "settings.json");
        string a = Path.Combine(dir, "a.json"), b = Path.Combine(dir, "b.json");
        File.WriteAllText(a, "{}");
        File.WriteAllText(b, "{}");

        var settings = AppSettings.Load(file);
        settings.TouchProject(a);
        settings.TouchProject(b);
        settings.TouchProject(a);

        var reloaded = AppSettings.Load(file);
        Assert.Equal([a, b], reloaded.ExistingRecentProjects().Select(r => r.Path));
        Assert.Equal(a, reloaded.LastProject);
    }

    [Fact]
    public void RecentProjects_HideDeletedFiles_AndForget()
    {
        string file = Path.Combine(dir, "settings.json");
        string a = Path.Combine(dir, "a.json"), gone = Path.Combine(dir, "deleted.json");
        File.WriteAllText(a, "{}");
        File.WriteAllText(gone, "{}");
        var settings = AppSettings.Load(file);
        settings.TouchProject(gone);
        settings.TouchProject(a);
        File.Delete(gone);

        Assert.Equal([a], settings.ExistingRecentProjects().Select(r => r.Path));

        settings.ForgetProject(a);
        Assert.Empty(AppSettings.Load(file).ExistingRecentProjects());
    }

    [Fact]
    public void ComputedProperties_AreNotSerialized()
    {
        string file = Path.Combine(dir, "settings.json");
        var settings = AppSettings.Load(file);
        settings.TouchProject(Path.Combine(dir, "x.json"));

        string json = File.ReadAllText(file);
        Assert.DoesNotContain("EffectiveEmulator", json);
        Assert.DoesNotContain("FilePath", json);
    }
}
