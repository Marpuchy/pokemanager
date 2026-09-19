using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Pokemanager.App.Resources;
using Pokemanager.App.Services;
using Pokemanager.Save;

namespace Pokemanager.App.ViewModels;

/// <summary>A base stat in the Pokédex page.</summary>
public sealed record DexStatLine(string Label, int Value, double BarWidth, IBrush BarBrush);

/// <summary>A label and a value of the Pokédex page.</summary>
public sealed record DexInfoLine(string Label, string Value);

/// <summary>
/// A species as the game's Pokédex shows it — number, name, classification, this version's entry, height and weight —
/// plus the data of the ROM being played (types, abilities, egg groups, base stats), which may be randomized.
/// </summary>
public sealed class DexDetailViewModel : ObservableObject
{
    private readonly DexEntryViewModel entry;

    public DexDetailViewModel(SaveEditorViewModel owner, SaveDocument doc, SaveNames names, DexEntryViewModel entry)
    {
        this.entry = entry;
        ushort species = entry.Species;
        var game = owner.GameNames;
        var p = doc.Personal(species, 0);
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
    }

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
