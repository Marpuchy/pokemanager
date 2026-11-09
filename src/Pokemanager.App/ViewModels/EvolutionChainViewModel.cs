using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.Input;
using Pokemanager.App.Resources;
using Pokemanager.App.Services;
using Pokemanager.Model.Data;
using Pokemanager.Model.Dump;

namespace Pokemanager.App.ViewModels;

/// <summary>One Pokémon of an evolution line: picture, name in its type colours, how it is reached, and a click to open it.</summary>
public sealed class EvolutionNodeViewModel(int species, int form, string name, string how, Bitmap? icon, IBrush background,
    IBrush foreground, bool isCurrent, Action<int, int>? open)
{
    public int Species { get; } = species;
    public int Form { get; } = form;
    public string Name { get; } = name;

    /// <summary>How it is reached from the stage before ("Level 16", "Use an item: Fire Stone"); empty for the first.</summary>
    public string How { get; } = how;
    public bool HasHow => How.Length > 0;
    public Bitmap? Icon { get; } = icon;
    public IBrush Background { get; } = background;
    public IBrush Foreground { get; } = foreground;
    public bool IsCurrent { get; } = isCurrent;
    public bool CanOpen => !IsCurrent && open is not null;

    public IRelayCommand OpenCommand { get; } = new RelayCommand(() => open?.Invoke(species, form));
}

/// <summary>A column of the line: the Pokémon of the same stage (one, or several when it branches like Eevee).</summary>
public sealed record EvolutionStageViewModel(IReadOnlyList<EvolutionNodeViewModel> Nodes, bool IsFirst);

/// <summary>
/// The whole evolution line of a Pokémon as the ROM has it (mods, randomization and edits included): from its first stage
/// to its last, every branch, with how each one is reached. Each Pokémon of the line opens its own page.
/// </summary>
public sealed class EvolutionChainViewModel
{
    private const int MostStages = 6;

    public IReadOnlyList<EvolutionStageViewModel> Stages { get; }

    /// <summary>Only the Pokémon itself: it neither evolves nor comes from anything.</summary>
    public bool IsAlone => Stages.Count <= 1;

    private EvolutionChainViewModel(IReadOnlyList<EvolutionStageViewModel> stages) => Stages = stages;

    /// <param name="open">What a click on another Pokémon of the line does (species, form); null for no links.</param>
    public static EvolutionChainViewModel Build(GameData data, GameNames names, int species, int form,
        Func<int, int, Bitmap?> icon, Action<int, int>? open)
    {
        bool gen7 = data.Title.Generation() == 7;
        var index = PersonalIndex.Of(data);
        var children = new Dictionary<(int, int), List<((int Species, int Form) To, string How)>>();
        var parent = new Dictionary<(int, int), (int, int)>();
        for (int row = 0; row < data.Evolutions.Length; row++)
        {
            if (index.Pokemon(row) is not { } from)
                continue;
            foreach (var (_, e) in data.Evolutions[row].InUse())
            {
                if (e.Species <= 0 || e.Species > data.LastSpecies)
                    continue;
                int targetForm = gen7 ? e.Form < 0 ? from.Form : e.Form : 0;
                var to = (e.Species, targetForm);
                if (!children.TryGetValue(from, out var list))
                    children[from] = list = [];
                if (list.All(c => c.To != to))
                    list.Add((to, EvolutionText.How(e, gen7, names)));
                parent.TryAdd(to, from);
            }
        }

        var self = (species, form);
        var root = self;
        var seen = new HashSet<(int, int)> { root };
        while (parent.TryGetValue(root, out var up) && seen.Add(up))
            root = up;

        EvolutionNodeViewModel Node((int Species, int Form) mon, string how)
        {
            int row = index.Row(mon.Species, mon.Form);
            if (row < 0)
                row = mon.Species;
            string name = row < names.PersonalEntries.Count ? names.PersonalEntries[row] : $"#{mon.Species}";
            int[] types = row < data.Personal.Length ? [data.Personal[row].Types[0], data.Personal[row].Types[1]] : [0, 0];
            return new EvolutionNodeViewModel(mon.Species, mon.Form, name, how, icon(mon.Species, mon.Form),
                TypeColors.Background(types[0], types[1]), TypeColors.Foreground(types), mon == self, open);
        }

        var stages = new List<EvolutionStageViewModel> { new([Node(root, "")], true) };
        var visited = new HashSet<(int, int)> { root };
        var current = new List<(int, int)> { root };
        while (stages.Count < MostStages)
        {
            var next = new List<EvolutionNodeViewModel>();
            var nextKeys = new List<(int, int)>();
            foreach (var mon in current)
                foreach (var (to, how) in children.GetValueOrDefault(mon) ?? [])
                    if (visited.Add(to))
                    {
                        next.Add(Node(to, how));
                        nextKeys.Add(to);
                    }
            if (next.Count == 0)
                break;
            stages.Add(new EvolutionStageViewModel(next, false));
            current = nextKeys;
        }

        // The mega evolutions of every Pokémon of the line, as one more column (2026-11-09, issue #2: "las megas deben
        // salir"). They come from the ROM's own mega table, so a mod's megas (Mega Staraptor) are there too.
        var megas = new List<EvolutionNodeViewModel>();
        foreach (var (key, stone) in data.MegaStoneByForm.OrderBy(p => p.Key.Species).ThenBy(p => p.Key.Form))
        {
            if (!visited.Contains((key.Species, 0)) || !visited.Add(key))
                continue;
            string how = stone > 0 && stone < names.Items.Count
                ? string.Format(Strings.Evo_MegaWith, names.Items[stone])
                : Strings.Evo_Mega;
            megas.Add(Node(key, how));
        }
        if (megas.Count > 0)
            stages.Add(new EvolutionStageViewModel(megas, false));
        return new EvolutionChainViewModel(stages);
    }
}
