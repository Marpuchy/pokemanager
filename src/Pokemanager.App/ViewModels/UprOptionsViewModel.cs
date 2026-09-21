using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using Pokemanager.App.Resources;
using Pokemanager.Randomizer;

namespace Pokemanager.App.ViewModels;

/// <summary>Editor of the UPR ZX options. The source of truth is the project's preset (.rnqs).</summary>
public partial class UprOptionsViewModel : ObservableObject
{
    private readonly Action markDirty;
    private readonly Dictionary<string, UprOptionViewModel> byName = [];

    public ObservableCollection<UprOptionGroupViewModel> Groups { get; } = [];

    [ObservableProperty]
    public partial UprOptionGroupViewModel? SelectedGroup { get; set; }

    [ObservableProperty]
    public partial bool IsLoaded { get; set; }

    [ObservableProperty]
    public partial string LoadStatus { get; set; } = Strings.Rnd_LoadingOptions;

    /// <summary>There are option changes not yet written to the preset.</summary>
    public bool HasChanges { get; private set; }

    private readonly ShopExtrasViewModel? shops;
    private readonly Func<GameTweaksViewModel?>? game;

    /// <param name="shops">
    /// Our own shop settings, shown on the item options page. Null in the tests, which only exercise the UPR options.
    /// </param>
    /// <param name="game">
    /// Our own game options, put on the group each one is about. A function because they are built after this page.
    /// </param>
    public UprOptionsViewModel(Action markDirty, ShopExtrasViewModel? shops = null, Func<GameTweaksViewModel?>? game = null)
    {
        this.markDirty = markDirty;
        this.shops = shops;
        this.game = game;
    }

    public void Load(UprSettingsDescription description, IReadOnlySet<string> availableTweaks, int generation)
    {
        Groups.Clear();
        byName.Clear();
        var groups = UprOptionCatalog.Groups.ToDictionary(g => g, g => new UprOptionGroupViewModel(g));
        groups[UprOptionCatalog.Items].Shops = shops;
        if (game?.Invoke() is { } tweaks)
        {
            foreach (var group in groups.Values)
                group.Game = tweaks;
        }

        foreach (var option in description.Options)
        {
            var info = UprOptionCatalog.Describe(option.Name);
            var vm = new UprOptionViewModel(this, option, info);
            byName[option.Name] = vm;
            // A hidden option keeps its value in the preset (and is written back) but has no control of its own.
            if (!UprOptionCatalog.IsHidden(option.Name, generation))
                groups[info.Group].Items.Add(vm);
        }

        foreach (var tweak in description.Tweaks.Where(t => availableTweaks.Contains(t.Name)))
        {
            var vm = UprOptionViewModel.ForTweak(this, tweak);
            byName[vm.Name] = vm;
            groups[UprOptionCatalog.Misc].Items.Add(vm);
        }

        // Catalog order within each group (uncatalogued options last).
        var order = UprOptionCatalog.Options.Keys.Select((k, i) => (k, i)).ToDictionary(p => p.k, p => p.i);
        foreach (var g in groups.Values)
        {
            var sorted = g.Items.OrderBy(i => order.GetValueOrDefault(i.Name, int.MaxValue)).ToList();
            g.Items.Clear();
            foreach (var item in sorted)
                g.Items.Add(item);
            if (g.Items.Count > 0)
                Groups.Add(g);
        }

        HasChanges = false;
        RefreshEnabled();
        SelectedGroup = Groups.FirstOrDefault();
        IsLoaded = true;
        LoadStatus = "";
    }

    public void Fail(string message)
    {
        IsLoaded = false;
        LoadStatus = message;
    }

    internal void OnChanged()
    {
        HasChanges = true;
        RefreshEnabled();
        markDirty();
    }

    private void RefreshEnabled()
    {
        foreach (var vm in byName.Values)
            vm.IsEnabled = vm.Info.DependsOn is not { } parent || !byName.TryGetValue(parent, out var p) || p.IsActive;
    }

    /// <summary>Every option as name=value lines for <c>write-settings</c>.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Assignments() =>
        byName.Values.Select(v => new KeyValuePair<string, string>(v.Name, v.SerializedValue)).ToList();

    public void MarkWritten() => HasChanges = false;

    /// <summary>
    /// What the preset asks the randomizer to add to a kind of level, when this application hides that option because it
    /// has its own. 0 when the preset does not touch it.
    /// </summary>
    public int PresetLevelModifier(string toggle, string percent) =>
        byName.TryGetValue(toggle, out var on) && on.BoolValue && byName.TryGetValue(percent, out var value)
            ? (int)(value.IntValue ?? 0)
            : 0;

    /// <summary>Puts that modifier back to nothing, so only this application's percentage acts.</summary>
    public void ClearLevelModifier(string toggle, string percent)
    {
        if (byName.TryGetValue(toggle, out var on))
            on.BoolValue = false;
        if (byName.TryGetValue(percent, out var value))
            value.IntValue = 0;
    }
}

/// <summary>
/// One group of options: UPR ZX's own and **this application's for the same subject**, on the same page. They are not
/// the same kind of thing — the randomizer's are rolled when the ROM is randomized, ours are written when it is built —
/// so each card says so; but a player looking for "trainer levels" finds everything about it in one place.
/// </summary>
public sealed class UprOptionGroupViewModel(string id)
{
    public string Id { get; } = id;
    public string Name { get; } = UprOptionCatalog.GroupLabel(id);
    public ObservableCollection<UprOptionViewModel> Items { get; } = [];

    /// <summary>Our own item and shop settings, on the group that holds the item options.</summary>
    public ShopExtrasViewModel? Shops { get; set; }

    public bool HasShops => Shops is not null;

    /// <summary>Our own game options; each group shows the ones about its subject.</summary>
    public GameTweaksViewModel? Game { get; set; }

    public bool HasGame => Game is not null;

    /// <summary>Base stats and the rest of the personal table: the sweeps over every Pokémon of the game.</summary>
    public bool IsPokemon => HasGame && Id == UprOptionCatalog.Traits;

    public bool IsTrainers => HasGame && Id == UprOptionCatalog.Trainers;

    public bool IsWild => HasGame && Id == UprOptionCatalog.Wild && Game!.WildAndStaticSupported;

    /// <summary>Fixed Pokémon, gifts and totems travel together in the game's static archive.</summary>
    public bool IsStatic => HasGame && Id == UprOptionCatalog.Starters && Game!.WildAndStaticSupported;

    /// <summary>Everything else about the whole game: shiny and the level cap.</summary>
    public bool IsMisc => HasGame && Id == UprOptionCatalog.Misc;
}

public partial class UprOptionViewModel : ObservableObject
{
    private readonly UprOptionsViewModel owner;
    private bool boolValue;
    private int intValue;
    private int choiceIndex;

    public string Name { get; }
    public UprOptionInfo Info { get; }
    public string Label { get; }
    public bool IsBool { get; }
    public bool IsInt { get; }
    public bool IsChoice { get; }
    public IReadOnlyList<string> Choices { get; } = [];
    public IReadOnlyList<string> ChoiceLabels { get; } = [];
    public string? Hint { get; }

    /// <summary>What the option does, on hover: UPR ZX's own text for a misc tweak, ours for everything else.</summary>
    public string? Tip => Hint ?? UprOptionCatalog.Description(Name);

    [ObservableProperty]
    public partial bool IsEnabled { get; set; } = true;

    internal UprOptionViewModel(UprOptionsViewModel owner, UprOptionValue option, UprOptionInfo info)
    {
        this.owner = owner;
        Name = option.Name;
        Info = info;
        Label = UprOptionCatalog.Label(option.Name);
        switch (option.Type)
        {
            case "bool":
                IsBool = true;
                boolValue = option.Value.ValueKind == JsonValueKind.True;
                break;
            case "int":
                IsInt = true;
                intValue = option.Value.GetInt32();
                break;
            default:
                IsChoice = true;
                Choices = option.Choices ?? [];
                ChoiceLabels = Choices.Select(c => UprOptionCatalog.ChoiceLabel(option.Name, c)).ToList();
                choiceIndex = option.Value.ValueKind == JsonValueKind.String ? Math.Max(0, Choices.ToList().IndexOf(option.Value.GetString()!)) : 0;
                break;
        }
    }

    private UprOptionViewModel(UprOptionsViewModel owner, string name, string label, string? hint, bool value)
    {
        this.owner = owner;
        Name = name;
        Info = new UprOptionInfo(UprOptionCatalog.Misc);
        Label = label;
        Hint = hint;
        IsBool = true;
        boolValue = value;
    }

    internal static UprOptionViewModel ForTweak(UprOptionsViewModel owner, UprTweakValue tweak) =>
        new(owner, "tweak:" + tweak.Name, UprOptionCatalog.TweakLabel(tweak.Name, tweak.Label), tweak.Tooltip, tweak.Value);

    public bool BoolValue
    {
        get => boolValue;
        set { if (SetProperty(ref boolValue, value)) owner.OnChanged(); }
    }

    public decimal? IntValue
    {
        get => intValue;
        set
        {
            if (value is null)
                return;
            int v = Math.Clamp((int)value.Value, Info.Min, Info.Max);
            if (SetProperty(ref intValue, v))
                owner.OnChanged();
        }
    }

    public int ChoiceIndex
    {
        get => choiceIndex;
        set
        {
            if (value < 0 || value >= Choices.Count)
                return;
            if (SetProperty(ref choiceIndex, value))
                owner.OnChanged();
        }
    }

    public int Min => Info.Min;
    public int Max => Info.Max;

    /// <summary>Active for dependencies: bool checked, or enum other than its first value ("unchanged").</summary>
    public bool IsActive => IsBool ? boolValue : IsChoice ? choiceIndex > 0 : intValue != 0;

    public string SerializedValue => IsBool ? (boolValue ? "true" : "false") : IsInt ? intValue.ToString() : Choices[choiceIndex];
}
