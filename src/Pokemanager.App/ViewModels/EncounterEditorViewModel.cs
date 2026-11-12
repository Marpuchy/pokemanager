using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using pk3DS.Core.CTR;
using Pokemanager.App.Resources;
using Pokemanager.Model.Data;
using Pokemanager.Model.Dump;
using Pokemanager.Model.Editing;
using Pokemanager.Model.Projects;

namespace Pokemanager.App.ViewModels;

/// <summary>What a place of the encounters tab is.</summary>
public enum EncounterPlaceKind { Area, Starters, Gifts, Statics, Trades }

/// <summary>
/// Encuentros → Todos los encuentros (2026-11-12, user: "every route with every Pokémon that can appear, the totems, the
/// starters and the trades, editable"; chose: hidden until asked, a change of a route's Pokémon changes every slot of
/// that route, and its levels). Generation 7: the routes come from the encounter archive of the ROM as the
/// randomization left it (or the base ROM), the rest from the static archive. What is changed goes to
/// <see cref="GameSettings.Encounters"/> and is written when the ROM is built.
/// </summary>
public sealed partial class EncounterEditorViewModel : ObservableObject
{
    private readonly EditorViewModel editor;
    private EditorSession session;
    private bool loaded;

    public EncounterEditorViewModel(EditorViewModel editor, EditorSession session)
    {
        this.editor = editor;
        this.session = session;
    }

    public bool Supported => session.Current.Title.Generation() == 7 && session.Current.Layout.Wild.Length > 0;

    public bool NotSupported => !Supported;

    /// <summary>Off on every open, like the trainers' teams: the list is the run's spoiler.</summary>
    [ObservableProperty]
    public partial bool ShowPokemon { get; set; }

    partial void OnShowPokemonChanged(bool value)
    {
        foreach (var place in Places)
            foreach (var row in place.Rows)
                row.Refresh();
    }

    [ObservableProperty]
    public partial string Status { get; set; } = "";

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    public ObservableCollection<EncounterPlaceViewModel> Places { get; } = [];

    [ObservableProperty]
    public partial EncounterPlaceViewModel? SelectedPlace { get; set; }

    /// <summary>"Keep it" first, then every species of the ROM — built once (an ItemsSource next to a two-way SelectedIndex).</summary>
    public IReadOnlyList<string> SpeciesChoices { get; private set; } = [];

    internal EncounterEdits Edits => session.Project.Tweaks.Encounters ??= new EncounterEdits();
    internal EditorViewModel Editor => editor;

    /// <summary>The session was reopened (a new randomization, a mod): read everything again next time it is shown.</summary>
    public void Reset(EditorSession reopened)
    {
        session = reopened;
        loaded = false;
        Places.Clear();
        SelectedPlace = null;
        OnPropertyChanged(nameof(Supported));
        OnPropertyChanged(nameof(NotSupported));
    }

    /// <summary>The project's changes were replaced (undo, history): the rows show what is stored now.</summary>
    public void Refresh()
    {
        foreach (var place in Places)
        {
            place.RefreshLevel();
            foreach (var row in place.Rows)
                row.Reload();
        }
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (loaded || !Supported || IsLoading)
            return;
        IsLoading = true;
        Status = Strings.Enc_Loading;
        var names = editor.Names;
        var s = session;
        try
        {
            var data = await Task.Run(() => Read(s, names.Locations));
            SpeciesChoices = [Strings.Enc_Keep, .. Enumerable.Range(1, Math.Max(0, names.Species.Count - 1)).Select(i => $"#{i:000} {names.Species[i]}")];
            OnPropertyChanged(nameof(SpeciesChoices));
            Places.Clear();
            foreach (var place in data.Places)
                Places.Add(new EncounterPlaceViewModel(this, place));
            SelectedPlace = Places.FirstOrDefault();
            Status = string.Format(data.FromRandomization ? Strings.Enc_FromRandomization : Strings.Enc_FromBase, data.Places.Count(p => p.Kind == EncounterPlaceKind.Area));
            loaded = true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException
                                       or IndexOutOfRangeException or RomReadException or EndOfStreamException)
        {
            Status = string.Format(Strings.Enc_Failed, ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    internal string SpeciesName(int species, int form)
    {
        var list = editor.Names.Species;
        string name = species > 0 && species < list.Count ? list[species] : $"#{species}";
        return form > 0 ? $"{name} ({form})" : name;
    }

    internal Bitmap? Icon(int species, int form) => editor.Sprites.For(species, form);

    // ------------------------------------------------------------------ what a Pokémon brings (2026-11-12, user)

    private GameData Data => editor.Session.Current;

    /// <summary>The personal row of a species and form (a form past the ones the species has is its first row).</summary>
    internal int Row(int species, int form)
    {
        var personal = Data.Personal;
        if (species <= 0 || species >= personal.Length)
            return 0;
        var p = personal[species];
        return form > 0 && form < p.FormeCount && p.FormStatsIndex > 0 && p.FormStatsIndex + form - 1 < personal.Length
            ? p.FormStatsIndex + form - 1 : species;
    }

    private string Named(IReadOnlyList<string> list, int id) => id > 0 && id < list.Count ? list[id] : $"#{id}";

    internal string AbilitiesOf(int species, int form)
    {
        var a = Data.Personal[Row(species, form)].Abilities;
        string normal = a[0] == a[1] ? Named(editor.Names.Abilities, a[0]) : $"{Named(editor.Names.Abilities, a[0])} / {Named(editor.Names.Abilities, a[1])}";
        return a.Length > 2 && a[2] > 0 && a[2] != a[0] && a[2] != a[1]
            ? string.Format(Strings.Enc_AbilitiesHidden, normal, Named(editor.Names.Abilities, a[2]))
            : normal;
    }

    /// <summary>What a wild one can hold: the first item half the time, the second 5 %, the third 1 % (all three the same: always).</summary>
    internal string ItemsOf(int species, int form)
    {
        var items = Data.Personal[Row(species, form)].Items;
        if (items.All(i => i <= 0))
            return Strings.Enc_NoItems;
        if (items.Distinct().Count() == 1)
            return string.Format(Strings.Enc_ItemAlways, Named(editor.Names.Items, items[0]));
        int[] chance = [50, 5, 1];
        return string.Join(", ", items.Select((item, i) => (item, i)).Where(x => x.item > 0)
            .Select(x => $"{Named(editor.Names.Items, x.item)} ({chance[Math.Min(x.i, 2)]} %)"));
    }

    /// <summary>The moves a wild one has at that level: the last four it learned, as the game gives them.</summary>
    internal string MovesOf(int species, int form, int level)
    {
        int row = Row(species, form);
        var learnsets = Data.Learnsets;
        if (row >= learnsets.Length || level <= 0)
            return "";
        var moves = learnsets[row].GetEncounterMoves(level).Where(m => m > 0).Select(m => Named(editor.Names.Moves, m)).ToList();
        return moves.Count == 0 ? "—" : string.Join(", ", moves);
    }

    private UprOptionViewModel? Option(string name) => editor.Randomizer.Options.Option(name);

    /// <summary>
    /// A Pokémon picked at random for a row, by the rules of the randomizer's wild options (2026-11-12, user: "any one,
    /// respecting the application's options"): no legendaries when they are blocked, of similar strength when that is
    /// the restriction, and none already in that place.
    /// </summary>
    internal int RandomSpecies(int species, int form, IEnumerable<int> taken)
    {
        var data = Data;
        int last = Math.Min(data.LastSpecies, editor.Names.Species.Count - 1);
        bool noLegendaries = Option("BlockWildLegendaries")?.BoolValue ?? false;
        bool similar = Option("WildPokemonRestrictionMod") is { IsChoice: true } r && r.SerializedValue == "SIMILAR_STRENGTH";
        var skip = taken.ToHashSet();
        var pool = Enumerable.Range(1, Math.Max(0, last)).Where(s => s != species && !skip.Contains(s)
            && !(noLegendaries && Legendaries.Contains(s)) && data.Personal[s].BST > 0).ToList();
        if (similar && species > 0)
        {
            int bst = data.Personal[Row(species, form)].BST;
            for (double margin = 0.1; margin < 1; margin += 0.05)
            {
                var near = pool.Where(s => Math.Abs(data.Personal[s].BST - bst) <= bst * margin).ToList();
                if (near.Count >= 3)
                {
                    pool = near;
                    break;
                }
            }
        }
        return pool.Count == 0 ? 0 : pool[Random.Shared.Next(pool.Count)];
    }

    // ------------------------------------------------------------------ reading (off the UI thread)

    internal sealed record PlaceData(EncounterPlaceKind Kind, int Index, string Name, List<RowData> Rows, int Order = 0);
    internal sealed record RowData(int Index, int Species, int Form, string Detail, int Requested = 0, bool Sos = false,
        int OriginalSpecies = 0, int OriginalForm = 0, int MinLevel = 0, int MaxLevel = 0);
    private sealed record ReadResult(List<PlaceData> Places, bool FromRandomization);

    private static ReadResult Read(EditorSession session, IReadOnlyList<string> locations)
    {
        var layout = session.Current.Layout;
        var places = new List<PlaceData>();

        // The static archive: imported with the game, replaced by the randomization's when there is one.
        if (layout.Statics.Length > 0 && session.Layers.TryResolve(layout.Statics) is { } staticsPath)
        {
            var files = new GARC.MemGARC(File.ReadAllBytes(staticsPath)).Files;
            // The game as imported, before the randomization: what each entry was, and so which of the fixed encounters
            // are the totems (they come in a totem form; the randomization keeps the entry but not the form). Measured
            // on the user's Ultra Sun: the other entries with an aura are the Ultra Beasts and Necrozma.
            string own = Path.Combine(session.Project.DumpDirectory, "romfs", layout.Statics.Replace('/', Path.DirectorySeparatorChar));
            var original = File.Exists(own) ? new GARC.MemGARC(File.ReadAllBytes(own)).Files : files;
            var originalGifts = WildEncounters7.Gifts(original).ToDictionary(g => g.Index);
            var originalStatics = WildEncounters7.Statics(original).ToDictionary(g => g.Index);
            var originalTrades = WildEncounters7.Trades(original).ToDictionary(g => g.Index);
            RowData Row(WildEncounters7.StaticEntry e, Dictionary<int, WildEncounters7.StaticEntry> was, int requested = 0) =>
                new(e.Index, e.Species, e.Form, string.Format(Strings.Enc_Level, e.Level), requested,
                    OriginalSpecies: was.TryGetValue(e.Index, out var o) ? o.Species : 0, OriginalForm: o?.Form ?? 0,
                    MinLevel: e.Level, MaxLevel: e.Level);
            bool Totem(WildEncounters7.StaticEntry e) =>
                originalStatics.TryGetValue(e.Index, out var o) && BattleSafety.IsTotem(o.Species, o.Form);
            var gifts = WildEncounters7.Gifts(files);
            places.Add(new(EncounterPlaceKind.Starters, -1, Strings.Enc_Starters, [.. gifts.Take(3).Select(g => Row(g, originalGifts))]));
            // These carry no place, but their level rises with the story (2026-11-12, user: "in story order, the totems
            // and everything"): the gifts, fixed encounters and trades go by level, the game's order between equals.
            places.Add(new(EncounterPlaceKind.Gifts, -1, Strings.Enc_Gifts,
                [.. gifts.Skip(3).Where(g => g.Species > 0).OrderBy(g => g.Level).ThenBy(g => g.Index).Select(g => Row(g, originalGifts))]));
            var statics = WildEncounters7.Statics(files).Where(g => g.Species > 0).OrderBy(g => g.Level).ThenBy(g => g.Index).ToList();
            places.Add(new(EncounterPlaceKind.Statics, -1, Strings.Enc_Totems,
                [.. statics.Where(Totem).Select(g => Row(g, originalStatics))]));
            places.Add(new(EncounterPlaceKind.Statics, -1, Strings.Enc_Statics,
                [.. statics.Where(g => !Totem(g)).Select(g => Row(g, originalStatics))]));
            places.Add(new(EncounterPlaceKind.Trades, -1, Strings.Enc_Trades,
                [.. WildEncounters7.Trades(files).OrderBy(t => t.Level).ThenBy(t => t.Index)
                    .Select(t => Row(t, originalTrades, t.Requested))]));
        }

        // The routes: the randomization's copy of the encounter archive when the session has one, else the base ROM.
        bool fromRandomization = session.Layers.TryResolve(layout.Wild) is not null;
        using var wild = OpenWild(session, layout.Wild);
        if (wild is null)
            return new(places, false);
        var names = AreaPlaces(session, locations);
        for (int file = WildEncounters7.FirstArea; file < wild.Count; file += WildEncounters7.AreaStride)
        {
            byte[] data = wild.File(file);
            var mons = WildEncounters7.Summarize(data);
            if (mons.Count == 0)
                continue;
            int area = WildEncounters7.AreaOfFile(file);
            var known = names.TryGetValue(area, out var p) && p.Name.Length > 0;
            string name = known ? p.Name : string.Format(Strings.Enc_Area, area);
            // An area without a name goes after the named ones, in the archive's order.
            places.Add(new(EncounterPlaceKind.Area, area, name, [.. mons.Select(m => new RowData(0, m.Species, m.Form, Detail(m), Sos: m.OnlySos, MinLevel: m.MinLevel, MaxLevel: m.MaxLevel))],
                known ? p.Order : int.MaxValue));
        }
        // Starters, gifts, totems, fixed and trades first, then the routes in story order (the game's location list
        // runs island by island); several areas with the same name are numbered in that order.
        var fixedPlaces = places.Where(p => p.Kind != EncounterPlaceKind.Area);
        var areas = places.Where(p => p.Kind == EncounterPlaceKind.Area).OrderBy(p => p.Order).ThenBy(p => p.Index).ToList();
        var counts = areas.GroupBy(p => p.Name).ToDictionary(g => g.Key, g => g.Count());
        var seen = new Dictionary<string, int>();
        areas = [.. areas.Select(p => counts[p.Name] == 1 ? p : p with { Name = $"{p.Name} ({seen[p.Name] = seen.GetValueOrDefault(p.Name) + 1})" })];
        return new([.. fixedPlaces, .. areas], fromRandomization);
    }

    private static string Detail(WildPokemon m)
    {
        var parts = new List<string>();
        parts.Add(m.OnlySos ? Strings.Enc_OnlySos : string.Format(Strings.Enc_Share, m.Share));
        if (m.Day != m.Night)
            parts.Add(m.Day ? Strings.Enc_Day : Strings.Enc_Night);
        parts.Add(m.MinLevel == m.MaxLevel ? string.Format(Strings.Enc_Level, m.MinLevel) : string.Format(Strings.Enc_Levels, m.MinLevel, m.MaxLevel));
        return string.Join(" · ", parts);
    }

    private static GarcStream? OpenWild(EditorSession session, string path)
    {
        if (session.Layers.TryResolve(path) is { } local)
            return new GarcStream(File.OpenRead(local));
        if (session.Project.ResolveRomFile() is not { } rom || !File.Exists(rom))
            return null;
        using var reader = RomReader.Open(rom);
        return reader.OpenRomFs(path) is { } stream ? new GarcStream(stream) : null;
    }

    /// <summary>Zone and world archives (measured on Ultra Sun; Sun/Moon assumed the same, a name missing is only a number).</summary>
    private const string ZoneData = "a/0/7/7", WorldData = "a/0/9/1";

    private static Dictionary<int, (string Name, int Order)> AreaPlaces(EditorSession session, IReadOnlyList<string> locations)
    {
        try
        {
            byte[]? Read(string path)
            {
                if (session.Layers.TryResolve(path) is { } local)
                    return File.ReadAllBytes(local);
                if (session.Project.ResolveRomFile() is not { } rom || !File.Exists(rom))
                    return null;
                using var reader = RomReader.Open(rom);
                return reader.ReadRomFs(path);
            }
            if (locations.Count == 0 || Read(ZoneData) is not { } zones || Read(WorldData) is not { } worlds)
                return [];
            var zoneFiles = new GARC.MemGARC(zones).Files;
            var worldFiles = new GARC.MemGARC(worlds).Files;
            return zoneFiles.Length < 2 ? [] : WildEncounters7.AreaPlaces(zoneFiles[0], zoneFiles[1],
                w => w < worldFiles.Length ? worldFiles[w] : [], locations);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or IndexOutOfRangeException
                                       or RomReadException or EndOfStreamException)
        {
            return [];
        }
    }
}

/// <summary>A route (area) or one of the lists of fixed Pokémon.</summary>
public sealed partial class EncounterPlaceViewModel : ObservableObject
{
    private readonly EncounterEditorViewModel owner;
    private bool loadingLevel;

    internal EncounterPlaceViewModel(EncounterEditorViewModel owner, EncounterEditorViewModel.PlaceData data)
    {
        this.owner = owner;
        Kind = data.Kind;
        Index = data.Index;
        Name = data.Name;
        Rows = [.. data.Rows.Select(r => new EncounterRowViewModel(owner, this, r))];
        RefreshLevel();
    }

    public EncounterPlaceKind Kind { get; }
    public int Index { get; }
    public string Name { get; }
    public IReadOnlyList<EncounterRowViewModel> Rows { get; }
    public bool IsArea => Kind == EncounterPlaceKind.Area;
    /// <summary>The Pokémon of the normal encounters, and apart those that only answer SOS calls (2026-11-12, user: "54 in a route is too many").</summary>
    public string CountText => Rows.Count(r => r.Sos) is var sos and > 0
        ? string.Format(Strings.Enc_CountSos, Rows.Count - sos, sos)
        : string.Format(Strings.Enc_Count, Rows.Count);

    public bool IsModified => Rows.Any(r => r.IsModified) || LevelShift != 0;

    /// <summary>Levels added to every table of the route.</summary>
    [ObservableProperty]
    public partial decimal LevelShift { get; set; }

    partial void OnLevelShiftChanged(decimal value)
    {
        if (loadingLevel || !IsArea)
            return;
        int shift = (int)value;
        if (shift == 0)
            owner.Edits.WildLevels.Remove(Index);
        else
            owner.Edits.WildLevels[Index] = shift;
        owner.Editor.MarkDirty(string.Format(Strings.Enc_UndoLevels, Name));
        OnPropertyChanged(nameof(IsModified));
    }

    internal void RefreshLevel()
    {
        loadingLevel = true;
        LevelShift = IsArea ? owner.Edits.WildLevels.GetValueOrDefault(Index) : 0;
        loadingLevel = false;
        OnPropertyChanged(nameof(IsModified));
    }

    internal void Changed() => OnPropertyChanged(nameof(IsModified));
}

/// <summary>A Pokémon of a place: what the ROM has there and what it becomes.</summary>
public sealed partial class EncounterRowViewModel : ObservableObject
{
    private readonly EncounterEditorViewModel owner;
    private readonly EncounterPlaceViewModel place;
    private readonly EncounterEditorViewModel.RowData data;
    private bool loading;

    internal EncounterRowViewModel(EncounterEditorViewModel owner, EncounterPlaceViewModel place, EncounterEditorViewModel.RowData data)
    {
        this.owner = owner;
        this.place = place;
        this.data = data;
        Reload();
    }

    private bool Shown => owner.ShowPokemon;

    public Bitmap? Icon => Shown ? owner.Icon(data.Species, data.Form) : null;
    public string Name => Shown ? owner.SpeciesName(data.Species, data.Form) : Strings.Enc_Hidden;
    public string Detail => data.Detail
        + (place.Kind == EncounterPlaceKind.Trades && Shown && data.Requested > 0 ? " · " + string.Format(Strings.Enc_Requests, owner.SpeciesName(data.Requested, 0)) : "")
        // What it replaces, from the game before the randomization (user: "say which totem they replace").
        + (Shown && data.OriginalSpecies > 0 && (data.OriginalSpecies, data.OriginalForm) != (data.Species, data.Form)
            ? " · " + string.Format(Strings.Enc_Replaces, owner.SpeciesName(data.OriginalSpecies, data.OriginalForm)) : "");

    public bool Sos => data.Sos;

    public IReadOnlyList<string> Choices => owner.SpeciesChoices;

    /// <summary>0 keeps the Pokémon; otherwise the species it becomes (form 0).</summary>
    [ObservableProperty]
    public partial int Choice { get; set; }

    public bool IsModified => Choice > 0;

    partial void OnChoiceChanged(int value)
    {
        if (loading)
            return;
        var edits = owner.Edits;
        string from = EncounterEdits.Key(data.Species, data.Form);
        string? to = value > 0 ? EncounterEdits.Key(value, 0) : null;
        switch (place.Kind)
        {
            case EncounterPlaceKind.Area:
                if (!edits.Wild.TryGetValue(place.Index, out var swaps))
                    edits.Wild[place.Index] = swaps = [];
                if (to is null)
                    swaps.Remove(from);
                else
                    swaps[from] = to;
                if (swaps.Count == 0)
                    edits.Wild.Remove(place.Index);
                break;
            default:
                var map = place.Kind switch
                {
                    EncounterPlaceKind.Statics => edits.Statics,
                    EncounterPlaceKind.Trades => edits.Trades,
                    _ => edits.Gifts,
                };
                if (to is null)
                    map.Remove(data.Index);
                else
                    map[data.Index] = to;
                break;
        }
        owner.Editor.MarkDirty(string.Format(Strings.Enc_UndoSwap, place.Name));
        OnPropertyChanged(nameof(IsModified));
        RefreshDetails();
        place.Changed();
    }

    /// <summary>The Pokémon this row has now: the chosen one, or what the ROM has there.</summary>
    private (int Species, int Form) Current => Choice > 0 ? (Choice, 0) : (data.Species, data.Form);

    /// <summary>Opened to show its abilities, held items and moves.</summary>
    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    partial void OnIsExpandedChanged(bool value) => RefreshDetails();

    public string LevelsText => data.MinLevel == data.MaxLevel
        ? string.Format(Strings.Enc_Level, data.MinLevel) : string.Format(Strings.Enc_Levels, data.MinLevel, data.MaxLevel);
    public string AbilitiesText => IsExpanded && Shown ? owner.AbilitiesOf(Current.Species, Current.Form) : Strings.Enc_Hidden;
    public string ItemsText => IsExpanded && Shown ? owner.ItemsOf(Current.Species, Current.Form) : Strings.Enc_Hidden;

    /// <summary>The moves at the lowest and at the highest level it can appear at.</summary>
    public IReadOnlyList<string> MovesText => !IsExpanded || !Shown
        ? [Strings.Enc_Hidden]
        : data.MinLevel == data.MaxLevel
            ? [string.Format(Strings.Enc_MovesAt, data.MinLevel, owner.MovesOf(Current.Species, Current.Form, data.MinLevel))]
            : [string.Format(Strings.Enc_MovesAt, data.MinLevel, owner.MovesOf(Current.Species, Current.Form, data.MinLevel)),
               string.Format(Strings.Enc_MovesAt, data.MaxLevel, owner.MovesOf(Current.Species, Current.Form, data.MaxLevel))];

    private void RefreshDetails()
    {
        OnPropertyChanged(nameof(AbilitiesText));
        OnPropertyChanged(nameof(ItemsText));
        OnPropertyChanged(nameof(MovesText));
    }

    /// <summary>Another Pokémon at random for this row, with what the ROM gives it (its moves, abilities and items).</summary>
    [RelayCommand]
    private void Reroll()
    {
        var taken = place.Rows.Where(r => r != this).Select(r => r.Current.Species);
        int pick = owner.RandomSpecies(data.Species, data.Form, taken);
        if (pick > 0)
            Choice = pick;
    }

    /// <summary>The choice stored in the project for this row.</summary>
    internal void Reload()
    {
        var edits = owner.Edits;
        string? to = place.Kind switch
        {
            EncounterPlaceKind.Area => edits.Wild.GetValueOrDefault(place.Index)?.GetValueOrDefault(EncounterEdits.Key(data.Species, data.Form)),
            EncounterPlaceKind.Statics => edits.Statics.GetValueOrDefault(data.Index),
            EncounterPlaceKind.Trades => edits.Trades.GetValueOrDefault(data.Index),
            _ => edits.Gifts.GetValueOrDefault(data.Index),
        };
        loading = true;
        Choice = to is null ? 0 : EncounterEdits.Parse(to).Species;
        loading = false;
        OnPropertyChanged(nameof(IsModified));
    }

    internal void Refresh()
    {
        OnPropertyChanged(nameof(Icon));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Detail));
        RefreshDetails();
    }
}
