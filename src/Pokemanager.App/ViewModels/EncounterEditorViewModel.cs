using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using pk3DS.Core.CTR;
using Pokemanager.App.Resources;
using Pokemanager.App.Services;
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

    internal int[] TypesOf(int species, int form)
    {
        var types = Data.Personal[Row(species, form)].Types;
        return types.Length >= 2 ? [types[0], types[1]] : [0, 0];
    }

    internal TypeChip TypeChipOf(int type) => new(type >= 0 && type < editor.Names.Types.Count ? editor.Names.Types[type] : "?",
        TypeColors.Background(type), TypeColors.Foreground(type));

    /// <summary>
    /// A Pokémon of the tab as the shared sheet: base stats with the values it has at its level (the IVs and EVs the
    /// entry fixes, "?" for those drawn at random, which count as 31), the moves it comes with, and its data. What each
    /// entry fixes was read on the user's Ultra Sun: IVs -1 random and -4 "three at 31"; a fixed encounter's nature 0 is
    /// random and otherwise the nature + 1; abilities by slot.
    /// </summary>
    internal MonSheet SheetOf(EncounterRowViewModel card, EncounterPlaceKind kind, int species, int form, RowData row)
    {
        var data = Data;
        var names = editor.Names;
        var p = data.Personal[Row(species, form)];
        int level = row.MaxLevel > 0 ? row.MaxLevel : 1;
        var raw = row.Raw;
        bool changed = species != row.Species || form != row.Form; // what the entry fixes was meant for the original

        // IVs and EVs: HP, Atk, Def, Spe, SpA, SpD as the game stores them.
        int[] ivs = [-1, -1, -1, -1, -1, -1];
        int[] evs = new int[6];
        int nature = -1, abilitySlot = -1, perfect = 0;
        int[] relearn = [];
        if (raw is not null)
        {
            switch (kind)
            {
                case EncounterPlaceKind.Statics:
                    ivs = [.. raw.AsSpan(0x15, 6).ToArray().Select(b => (int)(sbyte)b)];
                    evs = [.. raw.AsSpan(0x1B, 6).ToArray().Select(b => (int)b)];
                    nature = raw[0x14] - 1;
                    abilitySlot = ((raw[6] >> 4) & 7) switch { 1 => 0, 2 => 1, 3 or 4 => 2, _ => -1 };
                    relearn = [.. Enumerable.Range(0, 4).Select(i => (int)BitConverter.ToUInt16(raw, 0xC + 2 * i)).Where(m => m > 0)];
                    break;
                case EncounterPlaceKind.Trades:
                    ivs = [.. raw.AsSpan(0x6, 6).ToArray().Select(b => (int)(sbyte)b)];
                    nature = raw[0xD] < 25 ? raw[0xD] : -1;
                    abilitySlot = raw[0xC] <= 2 ? raw[0xC] : -1;
                    break;
                default:
                    nature = (sbyte)raw[0x7];
                    abilitySlot = (sbyte)raw[0x6] is >= 0 and <= 2 and var a ? a : -1;
                    if ((sbyte)raw[0xE] == -4)
                        perfect = 3;
                    break;
            }
        }
        if (ivs[0] == -4)
            (perfect, ivs[0]) = (3, -1);

        int Stat(int baseStat, int iv, int ev, bool hp, int natureEffect)
        {
            int value = (2 * baseStat + (iv < 0 ? 31 : iv) + ev / 4) * level / 100;
            if (hp)
                return value + level + 10;
            value += 5;
            return natureEffect > 0 ? value * 110 / 100 : natureEffect < 0 ? value * 90 / 100 : value;
        }
        string? IvText(int iv) => iv < 0 ? "?" : null;
        // Display order HP, Atk, Def, SpA, SpD, Spe; stored order HP, Atk, Def, Spe, SpA, SpD.
        (string Label, int Base, int Stored)[] order =
        [
            (Strings.Stat_HP, p.HP, 0), (Strings.Stat_Atk, p.ATK, 1), (Strings.Stat_Def, p.DEF, 2),
            (Strings.Stat_SpA, p.SPA, 4), (Strings.Stat_SpD, p.SPD, 5), (Strings.Stat_Spe, p.SPE, 3),
        ];
        var stats = order.Select((o, i) =>
        {
            int effect = nature >= 0 ? StatBars.NatureEffect(nature, i) : 0;
            int iv = ivs[o.Stored];
            return new SheetStat(o.Label, Stat(o.Base, iv, evs[o.Stored], i == 0, effect), o.Base, iv < 0 ? null : iv, evs[o.Stored],
                Nature: effect, IvShown: IvText(iv));
        }).ToList();

        // Moves: what a fixed encounter brings, else the last four it learned by its level (as the game gives them).
        int learnRow = Row(species, form);
        int[] moves = !changed && relearn.Length > 0 ? relearn
            : learnRow < data.Learnsets.Length ? [.. data.Learnsets[learnRow].GetEncounterMoves(level).Where(m => m > 0)] : [];
        var moveLines = Enumerable.Range(0, 4).Select(i => i < moves.Length ? MoveOf(moves[i]) : ViewModels.MoveLine.Empty).ToList();

        var a3 = p.Abilities;
        string ability = abilitySlot >= 0 && abilitySlot < a3.Length && !changed
            ? Named(names.Abilities, a3[abilitySlot]) + (abilitySlot == 2 ? $" ({Strings.Preview_Hidden})" : "")
            : AbilitiesOf(species, form);
        string natureText = nature >= 0 && nature < names.Natures.Count && !changed ? names.Natures[nature] : Strings.Enc_Random;
        int item = card.HasItem ? card.FixedItemId : 0;
        string itemText = kind == EncounterPlaceKind.Area ? ItemsOf(species, form)
            : item > 0 ? Named(names.Items, item) : Strings.Preview_NoItem;
        var info = new List<RoomInfoLine>
        {
            new(Strings.Pkm_Species, SpeciesName(species, form)),
            new(Strings.Enc_LevelsLabel, card.LevelsText),
            new(Strings.Pkm_AbilityLabel, ability),
            new(Strings.Pkm_Nature, natureText),
            new(Strings.Pkm_HeldItem, itemText),
        };
        if (kind == EncounterPlaceKind.Trades && row.Requested > 0)
            info.Add(new(Strings.Enc_RequestsLabel, SpeciesName(row.Requested, 0)));
        if (kind == EncounterPlaceKind.Statics && raw is not null && (raw[6] & 2) != 0)
            info.Add(new(Strings.Enc_ShinyLabel, Strings.Enc_ShinyLocked));
        if (changed)
            info.Add(new(Strings.Enc_InsteadLabel, SpeciesName(row.Species, row.Form)));

        string note = string.Join(" ", new[]
        {
            string.Format(Strings.Enc_StatsNote, level),
            perfect > 0 ? string.Format(Strings.Enc_PerfectIvs, perfect) : null,
            kind == EncounterPlaceKind.Area ? Strings.Enc_WildNote : null,
        }.Where(t => t is not null));
        string subtitle = $"{card.LevelsText} · {(nature >= 0 && !changed ? natureText : "")}".TrimEnd(' ', '·');
        return new MonSheet(card, subtitle, null, stats, moveLines, info, note);
    }

    private MoveLine MoveOf(int move)
    {
        var data = Data;
        var m = move < data.Moves.Length ? data.Moves[move] : null;
        int type = m?.Type ?? -1;
        var names = editor.Names;
        string typeName = type >= 0 && type < names.Types.Count ? names.Types[type] : "";
        return ViewModels.MoveLine.Of(Named(names.Moves, move), m is null ? "" : string.Format(Strings.Enc_PP, m.PP), type, typeName, m?.Category ?? -1);
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
        int OriginalSpecies = 0, int OriginalForm = 0, int MinLevel = 0, int MaxLevel = 0, byte[]? Raw = null);
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
                    MinLevel: e.Level, MaxLevel: e.Level, Raw: e.Raw);
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
public sealed partial class EncounterRowViewModel : ObservableObject, IMonCard
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

    // ------------------------------------------------------------------ as a card, like every Pokémon of the app

    public Bitmap? Icon => Shown ? owner.Icon(Current.Species, Current.Form) : null;
    public string Name => Shown ? owner.SpeciesName(Current.Species, Current.Form) : Strings.Enc_Hidden;
    public string GenderText => "";
    public string LevelText => LevelsText;
    public bool IsShiny => false;
    public bool IsEgg => false;
    public bool IsEmpty => false;
    public bool IsSelected => IsExpanded;
    public bool HasProblem => false;
    public string Tooltip => Name;

    /// <summary>The item it is given with (gifts, fixed encounters, trades); a wild one's chances are in the details.</summary>
    private int FixedItem => data.Raw is not { } raw ? 0 : place.Kind switch
    {
        EncounterPlaceKind.Trades => Math.Max(0, (int)BitConverter.ToInt16(raw, 0x14)),
        EncounterPlaceKind.Statics => Math.Max(0, (int)BitConverter.ToInt16(raw, 0x4)),
        _ => BitConverter.ToUInt16(raw, 0x8) is var i && i != 0xFFFF ? i : 0,
    };

    public bool HasItem => Shown && FixedItem > 0;
    internal int FixedItemId => FixedItem;
    public Bitmap? ItemIcon => HasItem ? PkhexImages.Item(FixedItem) : null;

    private int[] TypeIds => owner.TypesOf(Current.Species, Current.Form);
    public Avalonia.Media.IBrush TypeBackground => Shown ? TypeColors.Background(TypeIds[0], TypeIds[1]) : Services.Tokens.Panel2;
    public Avalonia.Media.IBrush TypeForeground => Shown ? TypeColors.Foreground(TypeIds) : Services.Tokens.Text;
    public IReadOnlyList<TypeChip> TypeChips => Shown ? [.. TypeIds.Distinct().Select(owner.TypeChipOf)] : [];

    /// <summary>A click on the card opens or closes its details.</summary>
    [RelayCommand]
    private void Select() => IsExpanded = !IsExpanded;

    public string Detail => data.Detail
        + (Shown && Choice > 0 ? " · " + string.Format(Strings.Enc_Replaces, owner.SpeciesName(data.Species, data.Form)) : "")
        + (place.Kind == EncounterPlaceKind.Trades && Shown && data.Requested > 0 ? " · " + string.Format(Strings.Enc_Requests, owner.SpeciesName(data.Requested, 0)) : "")
        // What it replaces, from the game before the randomization (user: "say which totem they replace").
        + (Shown && Choice == 0 && data.OriginalSpecies > 0 && (data.OriginalSpecies, data.OriginalForm) != (data.Species, data.Form)
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
        Refresh();
        place.Changed();
    }

    /// <summary>The Pokémon this row has now: the chosen one, or what the ROM has there.</summary>
    private (int Species, int Form) Current => Choice > 0 ? (Choice, 0) : (data.Species, data.Form);

    /// <summary>Opened to show its abilities, held items and moves.</summary>
    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    partial void OnIsExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsSelected));
        RefreshDetails();
    }

    /// <summary>The Pokémon laid out as everywhere else in the app (the shared <c>MonSheet</c>), built when it is opened.</summary>
    public MonSheet? Sheet => IsExpanded && Shown ? owner.SheetOf(this, place.Kind, Current.Species, Current.Form, data) : null;

    public bool ShowsHiddenNote => IsExpanded && !Shown;

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
        OnPropertyChanged(nameof(Sheet));
        OnPropertyChanged(nameof(ShowsHiddenNote));
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
        Refresh();
    }

    internal void Refresh()
    {
        OnPropertyChanged(nameof(Icon));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Detail));
        OnPropertyChanged(nameof(Tooltip));
        OnPropertyChanged(nameof(TypeBackground));
        OnPropertyChanged(nameof(TypeForeground));
        OnPropertyChanged(nameof(TypeChips));
        OnPropertyChanged(nameof(HasItem));
        OnPropertyChanged(nameof(ItemIcon));
        RefreshDetails();
    }
}
