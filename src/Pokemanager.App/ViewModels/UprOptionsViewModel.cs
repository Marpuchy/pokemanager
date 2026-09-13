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

    public UprOptionsViewModel(Action markDirty) => this.markDirty = markDirty;

    public void Load(UprSettingsDescription description, IReadOnlySet<string> availableTweaks)
    {
        Groups.Clear();
        byName.Clear();
        var groups = UprOptionCatalog.Groups.ToDictionary(g => g, g => new UprOptionGroupViewModel(g));

        foreach (var option in description.Options.Where(o => !UprOptionCatalog.Hidden.Contains(o.Name)))
        {
            var info = UprOptionCatalog.Describe(option.Name);
            var vm = new UprOptionViewModel(this, option, info);
            byName[option.Name] = vm;
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
}

public sealed class UprOptionGroupViewModel(string id)
{
    public string Id { get; } = id;
    public string Name { get; } = UprOptionCatalog.GroupLabel(id);
    public ObservableCollection<UprOptionViewModel> Items { get; } = [];
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
