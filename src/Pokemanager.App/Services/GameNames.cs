using pk3DS.Core;
using Pokemanager.Model.Data;
using Pokemanager.Model.Dump;

namespace Pokemanager.App.Services;

/// <summary>Nombres para mostrar, sacados de los textos del juego en el idioma del proyecto.</summary>
public sealed class GameNames
{
    public IReadOnlyList<string> Species { get; }
    public IReadOnlyList<string> PersonalEntries { get; }
    public IReadOnlyList<string> Types { get; }
    public IReadOnlyList<string> Abilities { get; }
    public IReadOnlyList<string> Items { get; }
    public IReadOnlyList<string> Moves { get; }

    public static IReadOnlyList<string> ExpGrowth { get; } =
        ["Medio", "Errático", "Fluctuante", "Parabólico", "Rápido", "Lento"];

    public static IReadOnlyList<string> EggGroups { get; } =
    [
        "—", "Monstruo", "Agua 1", "Bicho", "Volador", "Campo", "Hada", "Planta", "Humanoide",
        "Agua 3", "Mineral", "Amorfo", "Agua 2", "Ditto", "Dragón", "Desconocido",
    ];

    public static IReadOnlyList<string> MoveCategories { get; } = ["Estado", "Físico", "Especial"];

    public GameNames(GameDump dump, GameData original)
    {
        var config = dump.Config;
        Species = config.GetText(TextName.SpeciesNames);
        Types = config.GetText(TextName.Types);
        Abilities = config.GetText(TextName.AbilityNames);
        Items = config.GetText(TextName.ItemNames);
        Moves = config.GetText(TextName.MoveNames);
        PersonalEntries = BuildPersonalEntryNames(original, Species, config.MaxSpeciesID);
    }

    /// <summary>
    /// Las entradas por encima del número de especies son formas alternativas: la especie base indica
    /// con <c>FormStatsIndex</c> dónde empiezan y con <c>FormeCount</c> cuántas formas tiene.
    /// </summary>
    private static string[] BuildPersonalEntryNames(GameData data, IReadOnlyList<string> species, int maxSpecies)
    {
        var names = new string[data.Personal.Length];
        for (int i = 0; i < names.Length; i++)
            names[i] = i < species.Count ? species[i] : $"Entrada {i}";

        for (int s = 1; s <= maxSpecies && s < data.Personal.Length; s++)
        {
            var p = data.Personal[s];
            if (p.FormStatsIndex <= 0)
                continue;
            for (int form = 1; form < p.FormeCount; form++)
            {
                int index = p.FormStatsIndex + form - 1;
                if (index < names.Length)
                    names[index] = $"{species[s]} (forma {form})";
            }
        }

        return names;
    }
}
