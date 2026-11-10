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

/// <summary>
/// A Pokémon of the line and what it becomes: drawn as its card, then its branches stacked one under the other, each
/// behind an arrow (2026-11-09, user chose vertical branches: the Raichu megas fell into a second row with no arrow).
/// </summary>
public sealed class EvolutionBranchViewModel(EvolutionNodeViewModel node, IReadOnlyList<EvolutionBranchViewModel> children)
{
    /// <summary>Above this many branches they go in two or more columns (Eevee's eight: two columns of four).</summary>
    public const int MostInAColumn = 4;

    public EvolutionNodeViewModel Node { get; } = node;
    public IReadOnlyList<EvolutionBranchViewModel> Children { get; } = children;
    public bool HasChildren => Children.Count > 0;
    public bool IsWide => Children.Count > MostInAColumn;
    public bool IsNarrow => HasChildren && !IsWide;
    public int Columns => (Children.Count + MostInAColumn - 1) / MostInAColumn;
}

/// <summary>
/// The whole evolution line of a Pokémon as the ROM has it (mods, randomization and edits included): a tree from its
/// first stage, every branch with how it is reached, and each mega evolution next to the Pokémon it comes from. Each
/// Pokémon of the line opens its own page.
/// </summary>
public sealed class EvolutionChainViewModel
{
    private const int MostDepth = 8;

    public EvolutionBranchViewModel Root { get; }

    /// <summary>Only the Pokémon itself: it neither evolves nor comes from anything.</summary>
    public bool IsAlone => !Root.HasChildren;

    private EvolutionChainViewModel(EvolutionBranchViewModel root) => Root = root;

    /// <param name="open">What a click on another Pokémon of the line does (species, form); null for no links.</param>
    public static EvolutionChainViewModel Build(GameData data, GameNames names, int species, int form,
        Func<int, int, Bitmap?> icon, Action<int, int>? open)
    {
        bool gen7 = data.Title.Generation() == 7;
        var index = PersonalIndex.Of(data);
        int FormCount(int s) => s > 0 && s < data.Personal.Length && data.Personal[s].FormeCount is > 0 and var n ? n : 1;
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
                // "Same form" (-1) only when the target has that form: Galarian Meowth becomes Perrserker, which has
                // one form, so the line pointed at a Perrserker that does not exist and Perrserker stood alone.
                int targetForm = EvolutionSlots.TargetForm(e, from.Form, FormCount(e.Species), gen7);
                var to = (e.Species, targetForm);
                if (!children.TryGetValue(from, out var list))
                    children[from] = list = [];
                if (list.All(c => c.To != to))
                    list.Add((to, EvolutionText.How(e, gen7, names)));
                parent.TryAdd(to, from);
            }
        }

        // The mega evolutions hang from the Pokémon they come from (issue #2: "las megas deben salir"). They come from
        // the ROM's own mega table, so a mod's megas (Mega Raichu, Mega Staraptor) are there too.
        foreach (var (key, stone) in data.MegaStoneByForm.OrderBy(p => p.Key.Species).ThenBy(p => p.Key.Form))
        {
            var from = (key.Species, MegaParentForm(key.Species, key.Form));
            if (!children.TryGetValue(from, out var list))
                children[from] = list = [];
            if (list.Any(c => c.To == key))
                continue;
            string how = stone > 0 && stone < names.Items.Count
                ? string.Format(Strings.Evo_MegaWith, names.Items[stone])
                : Strings.Evo_Mega;
            list.Add((key, how));
            parent.TryAdd(key, from);
        }

        var self = (species, form);

        // A female-only mega (Meowstic's, in the expansion mod) comes from the female form — form 1 when that is not a
        // mega itself —; every other mega from form 0.
        int MegaParentForm(int s, int megaForm)
        {
            int row = index.Row(s, megaForm);
            return row > 0 && row < data.Personal.Length && data.Personal[row].Gender == 254 && FormCount(s) > 1
                   && !data.MegaForms.Contains((s, 1)) && index.Row(s, 1) is var female && female > 0
                   && data.Personal[female].Gender != 0
                ? 1 : 0;
        }

        // A form nothing evolves into and that evolves into nothing (Bloodmoon Ursaluna, Alcremie's flavours): shown in
        // its species' line, next to the form that line reaches, instead of on its own (issue #2).
        if (form > 0 && !parent.ContainsKey(self) && !children.ContainsKey(self)
            && parent.TryGetValue((species, 0), out var baseParent))
        {
            children[baseParent].Add((self, Strings.Evo_OtherForm));
            parent[self] = baseParent;
        }

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

        var visited = new HashSet<(int, int)> { root };
        EvolutionBranchViewModel Branch((int Species, int Form) mon, string how, int depth)
        {
            var below = new List<EvolutionBranchViewModel>();
            if (depth < MostDepth)
                foreach (var (to, toHow) in children.GetValueOrDefault(mon) ?? [])
                    if (visited.Add(to))
                        below.Add(Branch(to, toHow, depth + 1));
            return new EvolutionBranchViewModel(Node(mon, how), below);
        }

        return new EvolutionChainViewModel(Branch(root, "", 0));
    }
}
