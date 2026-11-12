using pk3DS.Core;
using pk3DS.Core.CTR;
using Pokemanager.Model.Projects;

namespace Pokemanager.Model.Data;

/// <summary>A Pokémon of an area, as the encounters tab lists it.</summary>
/// <param name="Share">The biggest share it has in the normal encounters of one of the area's tables (0-100); 0 when
/// it only comes through SOS calls.</param>
public sealed record WildPokemon(int Species, int Form, int Share, bool Day, bool Night, bool OnlySos, int MinLevel, int MaxLevel);

/// <summary>
/// The wild encounter tables of Generation 7 (2026-11-12, user: "a tab with every route and every Pokémon that can
/// appear, editable"; chose: changing a Pokémon of a route changes it everywhere in that route). Measured on Ultra Sun:
/// the encounter archive keeps an area every eleven files, its tables in file <c>9 + 11·area</c> — LZ11 around an
/// <c>EA</c> pack —, 74 areas with tables. A table: 4 bytes, then a day half and a night half of 0x164 bytes: minimum
/// and maximum level, ten rates, eight rows of ten slots (row 0 the normal encounters, 1-7 the SOS calls) and six more
/// SOS slots at 0x14C; a slot is <c>species | form &lt;&lt; 11</c>.
/// </summary>
public static class WildEncounters7
{
    public const int FirstArea = 9;
    public const int AreaStride = 11;
    private const int Half = 0x164;
    private const int TableSize = 4 + 2 * Half;
    private const int Slots = 8 * 10 + 6;

    public static int AreaOfFile(int file) => (file - FirstArea) / AreaStride;
    public static int FileOfArea(int area) => FirstArea + AreaStride * area;

    /// <summary>The tables of an area file, or null when it has none.</summary>
    public static List<byte[]>? Tables(byte[] file)
    {
        if (file.Length == 0 || file[0] != 0x11)
            return null;
        try
        {
            if (Mini.UnpackMini(LZSS.Decompress(file), "EA") is not { } tables)
                return null;
            var real = tables.Where(t => t.Length >= TableSize).ToList();
            return real.Count == 0 ? null : [.. tables];
        }
        catch (Exception ex) when (ex is InvalidDataException or IndexOutOfRangeException or ArgumentException or EndOfStreamException)
        {
            return null;
        }
    }

    private static IEnumerable<int> HalfOffsets => [4, 4 + Half];

    private static (int Species, int Form) Slot(byte[] table, int half, int slot)
    {
        uint raw = BitConverter.ToUInt32(table, half + 0xC + 4 * slot);
        return ((int)(raw & 0x7FF), (int)(raw >> 11));
    }

    /// <summary>Every Pokémon of an area file, with its biggest share in the normal encounters and when it appears.</summary>
    public static List<WildPokemon> Summarize(byte[] file)
    {
        var found = new Dictionary<(int, int), (int Share, bool Day, bool Night, bool Normal, int Min, int Max)>();
        foreach (byte[] table in Tables(file) ?? [])
        {
            if (table.Length < TableSize)
                continue;
            for (int h = 0; h < 2; h++)
            {
                int half = 4 + h * Half;
                int min = table[half], max = table[half + 1];
                var share = new Dictionary<(int, int), int>();
                for (int slot = 0; slot < Slots; slot++)
                {
                    var key = Slot(table, half, slot);
                    if (key.Species == 0)
                        continue;
                    int rate = slot < 10 ? table[half + 2 + slot] : 0;
                    share[key] = share.GetValueOrDefault(key) + rate;
                }
                foreach (var (key, rate) in share)
                {
                    var was = found.TryGetValue(key, out var seen) ? seen : (Share: 0, Day: false, Night: false, Normal: false, Min: 100, Max: 1);
                    found[key] = (Math.Max(was.Share, rate), was.Day || h == 0, was.Night || h == 1, was.Normal || rate > 0,
                        Math.Min(was.Min, min), Math.Max(was.Max, max));
                }
            }
        }
        return [.. found.OrderByDescending(p => p.Value.Share).ThenBy(p => p.Key.Item1)
            .Select(p => new WildPokemon(p.Key.Item1, p.Key.Item2, p.Value.Share, p.Value.Day, p.Value.Night, !p.Value.Normal,
                p.Value.Min, p.Value.Max))];
    }

    /// <summary>
    /// Applies the project's changes to every area of the archive, in place: each Pokémon swapped where the area has
    /// it, and the levels moved. Returns how many values changed.
    /// </summary>
    public static int Apply(byte[][] files, EncounterEdits edits)
    {
        int changed = 0;
        foreach (int area in edits.Wild.Keys.Concat(edits.WildLevels.Keys).Distinct())
        {
            int index = FileOfArea(area);
            if (index >= files.Length || Tables(files[index]) is not { } tables)
                continue;
            var swaps = edits.Wild.GetValueOrDefault(area)?.ToDictionary(p => EncounterEdits.Parse(p.Key), p => EncounterEdits.Parse(p.Value))
                        ?? [];
            int shift = edits.WildLevels.GetValueOrDefault(area);
            int before = changed;
            foreach (byte[] table in tables.Where(t => t.Length >= TableSize))
            {
                foreach (int half in HalfOffsets)
                {
                    for (int slot = 0; slot < Slots; slot++)
                    {
                        var key = Slot(table, half, slot);
                        if (key.Species == 0 || !swaps.TryGetValue(key, out var to) || to == key)
                            continue;
                        BitConverter.TryWriteBytes(table.AsSpan(half + 0xC + 4 * slot, 4), (uint)(to.Species | to.Form << 11));
                        changed++;
                    }
                    if (shift == 0)
                        continue;
                    for (int l = 0; l < 2; l++)
                    {
                        if (table[half + l] == 0)
                            continue;
                        table[half + l] = (byte)Math.Clamp(table[half + l] + shift, 1, 100);
                        changed++;
                    }
                }
            }
            if (changed != before)
                files[index] = LZSS.Compress(Mini.PackMini([.. tables], "EA"));
        }
        return changed;
    }

    /// <summary>
    /// The name of each area, from the game's zones (measured on Ultra Sun): zone data = 0x54 bytes per zone (LZ11),
    /// its location name at 0x1C; a second file gives each zone's world; each world (LZ11 around a <c>WD</c> pack) lists
    /// in its first part, third section, the (zone, area) pairs. An area with several zones of different names gets
    /// them all.
    /// </summary>
    public static Dictionary<int, string> AreaNames(byte[] zones, byte[] zoneWorlds, Func<int, byte[]> world, IReadOnlyList<string> locations) =>
        AreaPlaces(zones, zoneWorlds, world, locations).ToDictionary(p => p.Key, p => p.Value.Name);

    /// <summary>
    /// The name of each area and where it comes in the story (2026-11-12, user: "order the routes as they come in the
    /// story"): the game's location list runs island by island in story order (Route 1, Hau'oli, … Poni), so an area
    /// goes where its first-listed location is.
    /// </summary>
    public static Dictionary<int, (string Name, int Order)> AreaPlaces(byte[] zones, byte[] zoneWorlds, Func<int, byte[]> world, IReadOnlyList<string> locations)
    {
        var names = new Dictionary<int, List<string>>();
        var order = new Dictionary<int, int>();
        byte[] data = zones.Length > 0 && zones[0] == 0x11 ? LZSS.Decompress(zones) : zones;
        int count = Math.Min(data.Length / 0x54, zoneWorlds.Length / 2);
        var zoneArea = new Dictionary<int, int>();
        foreach (int w in Enumerable.Range(0, count).Select(z => (int)BitConverter.ToUInt16(zoneWorlds, 2 * z)).Distinct())
        {
            byte[] raw = world(w);
            if (raw.Length == 0)
                continue;
            byte[] unpacked = raw[0] == 0x11 ? LZSS.Decompress(raw) : raw;
            if (Mini.UnpackMini(unpacked, "WD") is not { Length: > 0 } parts || parts[0].Length < 16)
                continue;
            byte[] part = parts[0];
            int start = BitConverter.ToInt32(part, 8), end = BitConverter.ToInt32(part, 12);
            for (int at = start; at + 4 <= end && at + 4 <= part.Length; at += 4)
                zoneArea[BitConverter.ToUInt16(part, at)] = BitConverter.ToUInt16(part, at + 2);
        }
        for (int zone = 0; zone < count; zone++)
        {
            if (!zoneArea.TryGetValue(zone, out int area))
                continue;
            int location = BitConverter.ToUInt16(data, zone * 0x54 + 0x1C);
            if (location >= locations.Count || string.IsNullOrWhiteSpace(locations[location]))
                continue;
            if (!names.TryGetValue(area, out var list))
                names[area] = list = [];
            if (!list.Contains(locations[location]))
                list.Add(locations[location]);
            order[area] = Math.Min(order.GetValueOrDefault(area, int.MaxValue), location);
        }
        return names.ToDictionary(p => p.Key, p => (string.Join(" · ", p.Value), order[p.Key]));
    }

    // ------------------------------------------------------------------ static archive: gifts, fixed, trades

    /// <summary>Gifts: file 0 of the static archive, 0x14 bytes each, species at 0, form at 2, level at 3; the starters first.</summary>
    public const int GiftFile = 0, GiftSize = 0x14;

    /// <summary>Fixed encounters (legendaries, totems, allies): file 1, 0x38 bytes each, same layout.</summary>
    public const int StaticFile = 1, StaticSize = 0x38;

    /// <summary>In-game trades: file 4, 0x34 bytes each, the given species at 0, form at 4, level at 5, the requested one at 0x2C.</summary>
    public const int TradeFile = 4, TradeSize = 0x34;

    public sealed record StaticEntry(int Index, int Species, int Form, int Level, int Requested = 0);

    public static List<StaticEntry> Gifts(byte[][] files) => Entries(files, GiftFile, GiftSize, 2, 3);
    public static List<StaticEntry> Statics(byte[][] files) => Entries(files, StaticFile, StaticSize, 2, 3);
    public static List<StaticEntry> Trades(byte[][] files) => Entries(files, TradeFile, TradeSize, 4, 5, 0x2C);

    private static List<StaticEntry> Entries(byte[][] files, int file, int size, int formAt, int levelAt, int requestedAt = -1)
    {
        if (file >= files.Length)
            return [];
        byte[] data = files[file];
        return [.. Enumerable.Range(0, data.Length / size).Select(i => new StaticEntry(i, BitConverter.ToUInt16(data, i * size),
            data[i * size + formAt], data[i * size + levelAt], requestedAt < 0 ? 0 : BitConverter.ToUInt16(data, i * size + requestedAt)))];
    }

    /// <summary>The project's gifts, fixed encounters and trades written into the static archive, in place. Returns how many changed.</summary>
    public static int ApplyStatics(byte[][] files, EncounterEdits edits) =>
        Write(files, GiftFile, GiftSize, 2, edits.Gifts) + Write(files, StaticFile, StaticSize, 2, edits.Statics)
        + Write(files, TradeFile, TradeSize, 4, edits.Trades);

    private static int Write(byte[][] files, int file, int size, int formAt, Dictionary<int, string> values)
    {
        if (values.Count == 0 || file >= files.Length)
            return 0;
        int changed = 0;
        byte[] data = files[file];
        foreach (var (index, value) in values)
        {
            if (index < 0 || (index + 1) * size > data.Length)
                continue;
            var (species, form) = EncounterEdits.Parse(value);
            if (BitConverter.ToUInt16(data, index * size) == species && data[index * size + formAt] == form)
                continue;
            BitConverter.TryWriteBytes(data.AsSpan(index * size, 2), (ushort)species);
            data[index * size + formAt] = (byte)form;
            changed++;
        }
        return changed;
    }
}
