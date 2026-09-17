using System.Globalization;
using System.Resources;
using Pokemanager.Model.Edits;
using Pokemanager.Randomizer;

namespace Pokemanager.Tests.App;

/// <summary>
/// Every option the user can change says what it does when the pointer rests on it. These guard the two families that
/// are filled from resources by name, where a new option would otherwise get no text and nobody would notice.
/// </summary>
public class TooltipTests
{
    private static ResourceManager App() =>
        new("Pokemanager.App.Resources.Strings", System.Reflection.Assembly.Load("Pokemanager.App"));

    [Fact]
    public void EveryRandomizerOption_SaysWhatItDoes()
    {
        var missing = UprOptionCatalog.Options.Keys.Where(name => string.IsNullOrWhiteSpace(UprOptionCatalog.Description(name))).ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void EveryFieldOfTheGameTables_SaysWhatItIs()
    {
        var app = App();
        var missing = new List<string>();
        foreach (var table in GameTables.All)
        {
            foreach (string field in table.Fields)
            {
                // The six team slots of a trainer share one text, and free text fields are not numbers with a meaning.
                string name = field[(field.IndexOf('.') + 1)..];
                if (table.Name is GameTables.MoveTexts or GameTables.Learnsets)
                    continue;
                if (string.IsNullOrWhiteSpace(app.GetString($"Tip_Field_{table.Name}_{name}", CultureInfo.InvariantCulture)))
                    missing.Add($"{table.Name}.{name}");
            }
        }

        Assert.Empty(missing.Distinct());
    }
}
