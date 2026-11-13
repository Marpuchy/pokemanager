namespace Pokemanager.Model.Data;

/// <summary>Which list of the static archive an entry is in.</summary>
public enum FixedKind { Gift, Static, Trade }

/// <summary>
/// The fields of a gift (0x14), a fixed encounter (0x38) or an in-game trade (0x34) of Generation 7, read and written
/// with one meaning each (2026-11-13, user: "every option editable, as in Partida"). Measured on the user's Ultra Sun:
/// <list type="bullet">
/// <item><c>item</c>: 0 none (stored −1 in encounters and trades).</item>
/// <item><c>nature</c>: −1 at random, else 0-24 (an encounter stores 0 for random and nature + 1; a gift a signed byte).</item>
/// <item><c>ability</c>: −1 first or second at random, 0/1 that slot, 2 the hidden one (an encounter keeps it in bits 4-6
/// of byte 6 as 0 random, 1, 2, 3 hidden).</item>
/// <item><c>iv0</c>-<c>iv5</c>: −1 at random (HP, Atk, Def, Spe, SpA, SpD as stored); <c>ev0</c>-<c>ev5</c> and
/// <c>move1</c>-<c>move4</c> only in fixed encounters (a move 0 means the moves of its level); <c>requested</c> only in trades.</item>
/// </list>
/// </summary>
public static class FixedEncounters7
{
    public static readonly string[] Ivs = ["iv0", "iv1", "iv2", "iv3", "iv4", "iv5"];
    public static readonly string[] Evs = ["ev0", "ev1", "ev2", "ev3", "ev4", "ev5"];
    public static readonly string[] MoveFields = ["move1", "move2", "move3", "move4"];

    public static string Key(FixedKind kind, int index) => kind switch { FixedKind.Gift => "g", FixedKind.Static => "s", _ => "t" } + index;

    public static bool Has(FixedKind kind, string field) => field switch
    {
        "species" or "form" or "level" or "item" or "nature" or "ability" => true,
        _ when Ivs.Contains(field) => kind != FixedKind.Gift,
        _ when Evs.Contains(field) || MoveFields.Contains(field) => kind == FixedKind.Static,
        "requested" => kind == FixedKind.Trade,
        _ => false,
    };

    public static int Get(FixedKind kind, byte[] e, string field)
    {
        int formAt = kind == FixedKind.Trade ? 4 : 2, levelAt = kind == FixedKind.Trade ? 5 : 3;
        switch (field)
        {
            case "species": return BitConverter.ToUInt16(e, 0);
            case "form": return e[formAt];
            case "level": return e[levelAt];
            case "item":
                return kind switch
                {
                    FixedKind.Static => Math.Max(0, (int)BitConverter.ToInt16(e, 4)),
                    FixedKind.Trade => Math.Max(0, (int)BitConverter.ToInt16(e, 0x14)),
                    _ => BitConverter.ToUInt16(e, 8) is var g && g != 0xFFFF ? g : 0,
                };
            case "nature":
                return kind switch
                {
                    FixedKind.Static => e[0x14] - 1,
                    FixedKind.Trade => e[0xD] < 25 ? e[0xD] : -1,
                    _ => Math.Max(-1, (int)(sbyte)e[7]),
                };
            case "ability":
                return kind switch
                {
                    FixedKind.Static => ((e[6] >> 4) & 7) switch { 1 => 0, 2 => 1, 3 or 4 => 2, _ => -1 },
                    FixedKind.Trade => e[0xC] <= 2 ? e[0xC] : -1,
                    _ => (sbyte)e[6] is >= 0 and <= 2 and var a ? a : -1,
                };
            case "requested": return BitConverter.ToUInt16(e, 0x2C);
        }
        int iv = Array.IndexOf(Ivs, field);
        if (iv >= 0)
            return Math.Max(-1, (int)(sbyte)e[(kind == FixedKind.Static ? 0x15 : 0x6) + iv]);
        int ev = Array.IndexOf(Evs, field);
        if (ev >= 0)
            return e[0x1B + ev];
        int move = Array.IndexOf(MoveFields, field);
        if (move >= 0)
            return BitConverter.ToUInt16(e, 0xC + 2 * move);
        return 0;
    }

    public static void Set(FixedKind kind, byte[] e, string field, int value)
    {
        if (!Has(kind, field))
            return;
        int formAt = kind == FixedKind.Trade ? 4 : 2, levelAt = kind == FixedKind.Trade ? 5 : 3;
        void U16(int at, int v) => BitConverter.TryWriteBytes(e.AsSpan(at, 2), (ushort)v);
        switch (field)
        {
            case "species": U16(0, value); return;
            case "form": e[formAt] = (byte)Math.Clamp(value, 0, 31); return;
            case "level": e[levelAt] = (byte)Math.Clamp(value, 1, 100); return;
            case "item":
                if (kind == FixedKind.Static) BitConverter.TryWriteBytes(e.AsSpan(4, 2), (short)(value <= 0 ? -1 : value));
                else if (kind == FixedKind.Trade) BitConverter.TryWriteBytes(e.AsSpan(0x14, 2), (short)(value <= 0 ? -1 : value));
                else U16(8, Math.Max(0, value));
                return;
            case "nature":
                if (kind == FixedKind.Static) e[0x14] = (byte)(value < 0 ? 0 : value + 1);
                else if (kind == FixedKind.Trade) e[0xD] = (byte)Math.Max(0, value);
                else e[7] = (byte)(sbyte)Math.Max(-1, value);
                return;
            case "ability":
                if (kind == FixedKind.Static) e[6] = (byte)((e[6] & ~0x70) | ((value < 0 ? 0 : value + 1) << 4));
                else if (kind == FixedKind.Trade) e[0xC] = (byte)Math.Max(0, value);
                else e[6] = (byte)(sbyte)Math.Max(-1, value);
                return;
            case "requested": U16(0x2C, value); return;
        }
        int iv = Array.IndexOf(Ivs, field);
        if (iv >= 0)
        {
            e[(kind == FixedKind.Static ? 0x15 : 0x6) + iv] = (byte)(sbyte)Math.Clamp(value, -1, 31);
            return;
        }
        int ev = Array.IndexOf(Evs, field);
        if (ev >= 0)
        {
            e[0x1B + ev] = (byte)Math.Clamp(value, 0, 252);
            return;
        }
        int move = Array.IndexOf(MoveFields, field);
        if (move >= 0)
            U16(0xC + 2 * move, Math.Max(0, value));
    }

    /// <summary>The project's field edits (key → field → value) written into the static archive, in place.</summary>
    public static int Apply(byte[][] files, IReadOnlyDictionary<string, Dictionary<string, int>> fields)
    {
        int changed = 0;
        foreach (var (key, values) in fields)
        {
            if (key.Length < 2 || !int.TryParse(key.AsSpan(1), out int index))
                continue;
            var (kind, file, size) = key[0] switch
            {
                'g' => (FixedKind.Gift, WildEncounters7.GiftFile, WildEncounters7.GiftSize),
                's' => (FixedKind.Static, WildEncounters7.StaticFile, WildEncounters7.StaticSize),
                't' => (FixedKind.Trade, WildEncounters7.TradeFile, WildEncounters7.TradeSize),
                _ => (FixedKind.Gift, -1, 0),
            };
            if (file < 0 || file >= files.Length || (index + 1) * size > files[file].Length)
                continue;
            byte[] entry = files[file].AsSpan(index * size, size).ToArray();
            byte[] before = (byte[])entry.Clone();
            foreach (var (field, value) in values)
                Set(kind, entry, field, value);
            if (entry.AsSpan().SequenceEqual(before))
                continue;
            entry.CopyTo(files[file], index * size);
            changed++;
        }
        return changed;
    }
}
