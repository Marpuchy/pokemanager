using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Pokemanager.App.Resources;
using Pokemanager.App.Services;
using Pokemanager.Model.Dump;
using Pokemanager.Save;

namespace Pokemanager.App.ViewModels;

/// <summary>A base stat in the Pokédex page.</summary>
public sealed record DexStatLine(string Label, int Value, double BarWidth, IBrush BarBrush);

/// <summary>A label and a value of the Pokédex page.</summary>
public sealed record DexInfoLine(string Label, string Value);

/// <summary>A move the species learns by level, with its type as the ROM has it.</summary>
public sealed record DexMoveLine(string Level, string Move, TypeChip Type);

/// <summary>
/// One level of the learnset timeline: the level, the moves learnt there, and how far it sits from the previous stop.
/// </summary>
public sealed record DexMoveStop(string Level, IReadOnlyList<DexMoveLine> Moves, Avalonia.Thickness Gap);

/// <summary>
/// What a Pokémon learns by level as a horizontal timeline from its first level to its last (user, 2026-11-04: "como
/// una línea temporal"). Each level is a stop placed in proportion to the level, pushed right only as much as it needs
/// so two close levels do not overlap; the moves of one level are stacked under it.
/// </summary>
public static class DexMoveTimeline
{
    public const double StopWidth = 108, PixelsPerLevel = 12, MinimumGap = 6;

    public static IReadOnlyList<DexMoveStop> Build(IReadOnlyList<(int Level, DexMoveLine Line)> moves)
    {
        var stops = new List<DexMoveStop>();
        var groups = moves.GroupBy(m => m.Level).OrderBy(g => g.Key).ToList();
        if (groups.Count == 0)
            return stops;
        int first = groups[0].Key;
        double end = double.NegativeInfinity; // where the previous stop ends
        foreach (var g in groups)
        {
            double wanted = (g.Key - first) * PixelsPerLevel;
            double x = Math.Max(wanted, end + MinimumGap);
            double gap = stops.Count == 0 ? 0 : x - end;
            stops.Add(new DexMoveStop(g.First().Line.Level, [.. g.Select(m => m.Line)], new Avalonia.Thickness(gap, 0, 0, 0)));
            end = x + StopWidth;
        }
        return stops;
    }
}

/// <summary>
/// A species as the game's Pokédex shows it — number, name, classification, this version's entry, height and weight —
/// plus the data of the ROM being played (types, abilities, egg groups, base stats), which may be randomized.
/// </summary>
public sealed class DexDetailViewModel : ObservableObject
{
    private readonly DexEntryViewModel entry;

    public DexDetailViewModel(SaveEditorViewModel owner, SaveDocument doc, SaveNames names, DexEntryViewModel entry, Action<int, int>? open = null)
    {
        this.entry = entry;
        ushort species = entry.Species;
        var game = owner.GameNames;
        byte form = entry.Form;
        var p = doc.Personal(species, form);
        int[] types = p is null ? [0] : [.. p.Types.Distinct()];

        Number = $"#{species:000}";
        Name = entry.Name;
        Icon = entry.Icon;
        Classification = species < game.Classifications.Count ? game.Classifications[species] : "";
        string text = species < game.PokedexEntries.Count ? game.PokedexEntries[species] : "";
        Entry = text.Length > 0 ? text : Strings.Dex_NoEntry;
        HasEntry = text.Length > 0;
        TypeBackground = TypeColors.Background(types[0], types[^1]);
        TypeForeground = TypeColors.Foreground(types);
        TypeChips = [.. types.Select(owner.TypeChipOf)];
        GameName = Pokemanager.Model.Dump.GameTitleExtensions.DisplayName(owner.Game);

        if (p is null)
            return;
        Height = string.Format(Strings.Dex_Height, p.Height / 100.0);
        Weight = string.Format(Strings.Dex_Weight, p.Weight / 10.0);

        int[] abilities = [.. p.Abilities];
        Abilities =
        [
            new(Strings.Field_Ability1, names.AbilityName(abilities[0])),
            new(Strings.Field_Ability2, names.AbilityName(abilities[1])),
            new(Strings.Field_AbilityHidden, names.AbilityName(abilities[2])),
        ];
        var info = new List<DexInfoLine>
        {
            new(Strings.Dex_EggGroups, string.Join(" · ", new[] { p.EggGroups[0], p.EggGroups[1] }.Distinct()
                .Select(g => g >= 0 && g < GameNames.EggGroups.Count ? GameNames.EggGroups[g] : "?"))),
            new(Strings.Dex_GenderRatio, p.Gender switch
            {
                255 => Strings.Pkm_Genderless,
                254 => Strings.Dex_FemaleOnly,
                0 => Strings.Dex_MaleOnly,
                var g => string.Format(Strings.Dex_Ratio, 100 - (g * 100 / 254), g * 100 / 254),
            }),
            new(Strings.Field_CatchRate, p.CatchRate.ToString()),
            new(Strings.Field_ExpGrowth, p.EXPGrowth < GameNames.ExpGrowth.Count ? GameNames.ExpGrowth[p.EXPGrowth] : "?"),
            new(Strings.Field_HatchCycles, p.HatchCycles.ToString()),
        };
        Info = info;

        (string Label, int Value)[] stats =
        [
            (Strings.Stat_HP, p.HP), (Strings.Stat_Atk, p.ATK), (Strings.Stat_Def, p.DEF),
            (Strings.Stat_SpA, p.SPA), (Strings.Stat_SpD, p.SPD), (Strings.Stat_Spe, p.SPE),
        ];
        Stats = [.. stats.Select(s => new DexStatLine(s.Label, s.Value, Math.Max(2, Math.Min(s.Value, 200) / 200.0 * 220), BarBrush(s.Value)))];
        Total = stats.Sum(s => s.Value);

        // The ROM's own data (randomization, mods and the project's edits included): how it evolves and what it learns.
        var rom = doc.Rom;
        Chain = EvolutionChainViewModel.Build(rom, game, species, form, (s, f) => owner.Sprites.For(s, f), open);

        if (entry.Row < rom.Learnsets.Length)
        {
            var learnset = rom.Learnsets[entry.Row];
            var lines = Enumerable.Range(0, learnset.Moves.Length).Select(i => ((int)learnset.Levels[i], new DexMoveLine(
                learnset.Levels[i] == 0 ? Strings.Dex_MoveOnEvolution : string.Format(Strings.Dex_MoveLevel, learnset.Levels[i]),
                learnset.Moves[i] < game.Moves.Count ? game.Moves[learnset.Moves[i]] : $"#{learnset.Moves[i]}",
                owner.TypeChipOf(learnset.Moves[i] < rom.Moves.Length ? rom.Moves[learnset.Moves[i]].Type : -1)))).ToList();
            Moves = [.. lines.Select(l => l.Item2)];
            Timeline = DexMoveTimeline.Build(lines);
        }
    }

    /// <summary>Its whole evolution line, with how each stage is reached; a click opens that Pokémon's entry.</summary>
    public EvolutionChainViewModel? Chain { get; }
    public bool HasNoEvolution => Chain is null || Chain.IsAlone;

    /// <summary>What it learns by level, hidden until asked like the abilities.</summary>
    public IReadOnlyList<DexMoveLine> Moves { get; } = [];

    /// <summary>The same, by level along a horizontal line.</summary>
    public IReadOnlyList<DexMoveStop> Timeline { get; } = [];

    private static IBrush BarBrush(int value) => new SolidColorBrush(Color.Parse(value switch
    {
        < 30 => "#F34444", < 60 => "#FF7F0F", < 90 => "#FFDD57", < 120 => "#A0E515", < 150 => "#23CD5E", _ => "#00C2B8",
    }));

    public string Number { get; }
    public string Name { get; }
    public Avalonia.Media.Imaging.Bitmap? Icon { get; }
    public string Classification { get; }
    public string Entry { get; }
    public bool HasEntry { get; }
    public string GameName { get; }
    public string Height { get; } = "—";
    public string Weight { get; } = "—";
    public IBrush TypeBackground { get; }
    public IBrush TypeForeground { get; }
    public IReadOnlyList<TypeChip> TypeChips { get; }
    public IReadOnlyList<DexInfoLine> Info { get; } = [];

    /// <summary>
    /// The three abilities, apart from the rest: a player checking what a randomization did to a species may not want
    /// to be told what it can have, so the page keeps them hidden until asked (like Advanced: Pokémon).
    /// </summary>
    public IReadOnlyList<DexInfoLine> Abilities { get; } = [];
    public IReadOnlyList<DexStatLine> Stats { get; } = [];
    public int Total { get; }

    /// <summary>Registered in the save's Pokédex, as the list's check boxes (two-way).</summary>
    /// <summary>The save's Pokédex has flags for it (a species of the game's own Pokédex, not a form or a mod's Pokémon).</summary>
    public bool InDex => entry.InDex;

    public bool Seen
    {
        get => entry.Seen;
        set => entry.Seen = value;
    }

    public bool Caught
    {
        get => entry.Caught;
        set => entry.Caught = value;
    }

    public string StatusText => Caught ? Strings.Dex_StatusCaught : Seen ? Strings.Dex_StatusSeen : Strings.Dex_StatusUnknown;

    public void Refresh() => OnPropertyChanged(string.Empty);
}
