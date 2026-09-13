using System.Collections;
using System.Globalization;
using System.Resources;

namespace Pokemanager.Tests;

public class LocalizationTests
{
    public static TheoryData<string> Resources() =>
    [
        "Pokemanager.App",
        "Pokemanager.Model",
        "Pokemanager.Randomizer",
        "Pokemanager.Save",
    ];

    private static ResourceManager Manager(string name) =>
        new(name + ".Resources.Strings", System.Reflection.Assembly.Load(name));

    [Theory]
    [MemberData(nameof(Resources))]
    public void Spanish_TranslatesEveryEnglishKey_KeepingPlaceholders(string name)
    {
        var manager = Manager(name);
        var english = manager.GetResourceSet(CultureInfo.InvariantCulture, true, false)!.Cast<DictionaryEntry>()
            .ToDictionary(e => (string)e.Key, e => (string)e.Value!);
        var spanish = manager.GetResourceSet(new CultureInfo("es"), true, false)!.Cast<DictionaryEntry>()
            .ToDictionary(e => (string)e.Key, e => (string)e.Value!);

        Assert.Empty(english.Keys.Except(spanish.Keys));
        foreach (var (key, value) in english)
        {
            var placeholders = System.Text.RegularExpressions.Regex.Matches(value, @"\{\d+[^}]*\}").Select(m => m.Value).Order();
            var translated = System.Text.RegularExpressions.Regex.Matches(spanish[key], @"\{\d+[^}]*\}").Select(m => m.Value).Order();
            Assert.True(placeholders.SequenceEqual(translated), $"{name}.{key}: placeholders differ");
        }
    }
}
