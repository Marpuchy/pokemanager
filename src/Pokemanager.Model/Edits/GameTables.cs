using System.Text.Json.Nodes;
using pk3DS.Core.Structures;
using pk3DS.Core.Structures.PersonalInfo;
using Pokemanager.Model.Data;
using Pokemanager.Model.Resources;

namespace Pokemanager.Model.Edits;

/// <summary>Maps (table, id, field) to reads and writes on <see cref="GameData"/>.</summary>
public interface ITable
{
    string Name { get; }
    IReadOnlyList<string> Fields { get; }
    int Count(GameData data);
    JsonNode Get(GameData data, int id, string field);
    void Set(GameData data, int id, string field, JsonNode value);
}

public static class GameTables
{
    public const string Personal = "personal";
    public const string Moves = "move";
    public const string Learnsets = "learnset";

    /// <summary>Move texts: <see cref="Description"/> (a string; English base, written to every language of the game).</summary>
    public const string MoveTexts = "movetext";

    /// <summary>
    /// Trainers: the record and its team. Team fields are prefixed with the slot, <c>p1.</c> … <c>p6.</c>; writing one of
    /// them on a slot the trainer does not have grows the team. Fields the generation does not have (the Generation 6
    /// <c>ivs</c> byte, the Generation 7 IVs, EVs, nature and shiny) read 0 and write nothing.
    /// </summary>
    public const string Trainers = "trainer";

    /// <summary>
    /// Items: <c>price</c> is what a shop charges for one (the game stores a tenth of it, so the value is rounded down
    /// to the nearest 10), and <c>sellPrice</c> is what it pays, half of that, shown for reference.
    /// </summary>
    public const string Items = "item";

    public const string Description = "description";

    /// <summary>Field of <see cref="Learnsets"/>: list of <c>[level, move]</c> pairs sorted by level.</summary>
    public const string LevelUp = "levelup";

    public static readonly ITable PersonalTable = new IntTable<PersonalInfoXY>(Personal, d => d.Personal,
    [
        new("hp", p => p.HP, (p, v) => p.HP = v),
        new("atk", p => p.ATK, (p, v) => p.ATK = v),
        new("def", p => p.DEF, (p, v) => p.DEF = v),
        new("spa", p => p.SPA, (p, v) => p.SPA = v),
        new("spd", p => p.SPD, (p, v) => p.SPD = v),
        new("spe", p => p.SPE, (p, v) => p.SPE = v),
        new("type1", p => p.Types[0], (p, v) => p.Types = [v, p.Types[1]]),
        new("type2", p => p.Types[1], (p, v) => p.Types = [p.Types[0], v]),
        new("ability1", p => p.Abilities[0], (p, v) => p.Abilities = [v, p.Abilities[1], p.Abilities[2]]),
        new("ability2", p => p.Abilities[1], (p, v) => p.Abilities = [p.Abilities[0], v, p.Abilities[2]]),
        new("abilityHidden", p => p.Abilities[2], (p, v) => p.Abilities = [p.Abilities[0], p.Abilities[1], v]),
        new("catchRate", p => p.CatchRate, (p, v) => p.CatchRate = v),
        new("baseExp", p => p.BaseEXP, (p, v) => p.BaseEXP = v),
        new("expGrowth", p => p.EXPGrowth, (p, v) => p.EXPGrowth = v),
        new("gender", p => p.Gender, (p, v) => p.Gender = v),
        new("hatchCycles", p => p.HatchCycles, (p, v) => p.HatchCycles = v),
        new("baseFriendship", p => p.BaseFriendship, (p, v) => p.BaseFriendship = v),
        new("eggGroup1", p => p.EggGroups[0], (p, v) => p.EggGroups = [v, p.EggGroups[1]]),
        new("eggGroup2", p => p.EggGroups[1], (p, v) => p.EggGroups = [p.EggGroups[0], v]),
        new("item1", p => p.Items[0], (p, v) => p.Items = [v, p.Items[1], p.Items[2]]),
        new("item2", p => p.Items[1], (p, v) => p.Items = [p.Items[0], v, p.Items[2]]),
        new("item3", p => p.Items[2], (p, v) => p.Items = [p.Items[0], p.Items[1], v]),
        new("evHp", p => p.EV_HP, (p, v) => p.EV_HP = v),
        new("evAtk", p => p.EV_ATK, (p, v) => p.EV_ATK = v),
        new("evDef", p => p.EV_DEF, (p, v) => p.EV_DEF = v),
        new("evSpa", p => p.EV_SPA, (p, v) => p.EV_SPA = v),
        new("evSpd", p => p.EV_SPD, (p, v) => p.EV_SPD = v),
        new("evSpe", p => p.EV_SPE, (p, v) => p.EV_SPE = v),
        new("height", p => p.Height, (p, v) => p.Height = v),
        new("weight", p => p.Weight, (p, v) => p.Weight = v),
        new("escapeRate", p => p.EscapeRate, (p, v) => p.EscapeRate = v),

        // Generation 7 keeps the exclusive Z-move of the species here: the crystal, the move it needs and the Z-move it
        // becomes (Decidueye + Decidium Z + Spirit Shackle = Sinister Arrow Raid). Reading gives 0 on Gen 6, writing does nothing.
        new(ZCrystal, p => Z(p)?.SpecialZ_Item ?? 0, (p, v) => { if (Z(p) is { } z) z.SpecialZ_Item = v; }),
        new(ZBaseMove, p => Z(p)?.SpecialZ_BaseMove ?? 0, (p, v) => { if (Z(p) is { } z) z.SpecialZ_BaseMove = v; }),
        new(ZMove, p => Z(p)?.SpecialZ_ZMove ?? 0, (p, v) => { if (Z(p) is { } z) z.SpecialZ_ZMove = v; }),
    ]);

    /// <summary>The entry as a Generation 7 one (which has the Z-move fields), or null in Generation 6.</summary>
    private static PersonalInfoSM? Z(PersonalInfoXY entry) => entry as PersonalInfoSM;

    /// <summary>Field of <see cref="Personal"/>: the Z-crystal that gives this species its exclusive Z-move (Gen 7).</summary>
    public const string ZCrystal = "zCrystal";

    /// <summary>Field of <see cref="Personal"/>: the move the Z-crystal needs (Gen 7).</summary>
    public const string ZBaseMove = "zBaseMove";

    /// <summary>Field of <see cref="Personal"/>: the Z-move that comes out (Gen 7).</summary>
    public const string ZMove = "zMove";

    public static readonly ITable MoveTable = new IntTable<Move>(Moves, d => d.Moves,
    [
        new("type", m => m.Type, (m, v) => m.Type = v),
        new("category", m => m.Category, (m, v) => m.Category = v),
        new("power", m => m.Power, (m, v) => m.Power = v),
        new("accuracy", m => m.Accuracy, (m, v) => m.Accuracy = v),
        new("pp", m => m.PP, (m, v) => m.PP = v),
        new("priority", m => (sbyte)m.Priority, (m, v) => m.Priority = (byte)(sbyte)v),
        new("critStage", m => m.CritStage, (m, v) => m.CritStage = v),
        new("flinch", m => m.Flinch, (m, v) => m.Flinch = v),
        new("hitMin", m => m.HitMin, (m, v) => m.HitMin = v),
        new("hitMax", m => m.HitMax, (m, v) => m.HitMax = v),
        new("recoil", m => (sbyte)m.Recoil, (m, v) => m.Recoil = (byte)(sbyte)v),
        new("inflictPercent", m => m.InflictPercent, (m, v) => m.InflictPercent = v),
    ]);

    public static readonly ITable LearnsetTable = new LearnsetTableImpl();

    public static readonly ITable MoveTextTable = new MoveTextTableImpl();

    public static readonly ITable TrainerTable = new TrainerTableImpl();

    public static readonly ITable ItemTable = new ItemTableImpl();

    public static IReadOnlyList<ITable> All { get; } = [PersonalTable, MoveTable, LearnsetTable, MoveTextTable, TrainerTable, ItemTable];

    public static ITable Get(string name) =>
        All.FirstOrDefault(t => t.Name == name) ?? throw new ArgumentException(string.Format(Strings.Tables_UnknownTable, name), nameof(name));

    internal sealed record IntField<T>(string Name, Func<T, int> Get, Action<T, int> Set);

    private sealed class IntTable<T>(string name, Func<GameData, T[]> entries, IReadOnlyList<IntField<T>> fields) : ITable
    {
        private readonly Dictionary<string, IntField<T>> byName = fields.ToDictionary(f => f.Name);

        public string Name => name;
        public IReadOnlyList<string> Fields { get; } = fields.Select(f => f.Name).ToArray();
        public int Count(GameData data) => entries(data).Length;

        public JsonNode Get(GameData data, int id, string field) => JsonValue.Create(Field(field).Get(Entry(data, id)));

        public void Set(GameData data, int id, string field, JsonNode value) =>
            Field(field).Set(Entry(data, id), value.GetValue<int>());

        private T Entry(GameData data, int id)
        {
            var all = entries(data);
            if ((uint)id >= all.Length)
                throw new ArgumentOutOfRangeException(nameof(id), id, string.Format(Strings.Tables_OutOfRange, name, all.Length));
            return all[id];
        }

        private IntField<T> Field(string field) =>
            byName.TryGetValue(field, out var f) ? f : throw new ArgumentException(string.Format(Strings.Tables_UnknownField, name, field), nameof(field));
    }

    private sealed class LearnsetTableImpl : ITable
    {
        public string Name => Learnsets;
        public IReadOnlyList<string> Fields { get; } = [LevelUp];
        public int Count(GameData data) => data.Learnsets.Length;

        public JsonNode Get(GameData data, int id, string field)
        {
            Check(field);
            var l = data.Learnsets[id];
            return new JsonArray(Enumerable.Range(0, l.Moves.Length)
                .Select(i => (JsonNode)new JsonArray(l.Levels[i], l.Moves[i]))
                .ToArray());
        }

        public void Set(GameData data, int id, string field, JsonNode value)
        {
            Check(field);
            var pairs = value.AsArray()
                .Select(p => (Level: p![0]!.GetValue<int>(), Move: p[1]!.GetValue<int>()))
                .OrderBy(p => p.Level) // stable: keeps the order of moves learned at the same level
                .ToArray();
            var l = data.Learnsets[id];
            l.Levels = pairs.Select(p => p.Level).ToArray();
            l.Moves = pairs.Select(p => p.Move).ToArray();
            l.Count = pairs.Length;
        }

        private static void Check(string field)
        {
            if (field != LevelUp)
                throw new ArgumentException(string.Format(Strings.Tables_UnknownField, Learnsets, field), nameof(field));
        }
    }

    /// <summary>
    /// The trainer record and its team. Everything is an int, booleans included (0/1), so a trainer file is the same
    /// kind of document as a <c>.pkdata</c>.
    /// </summary>
    private sealed class TrainerTableImpl : ITable
    {
        private static readonly string[] RecordFields =
            ["ai", "class", "battleType", "money", "flag", "customMoves", "heldItems", "count", "item1", "item2", "item3", "item4"];

        private static readonly string[] MemberFields =
        [
            "species", "form", "level", "ability", "gender", "item", "move1", "move2", "move3", "move4",
            "ivs", "nature", "shiny",
            "ivHp", "ivAtk", "ivDef", "ivSpa", "ivSpd", "ivSpe",
            "evHp", "evAtk", "evDef", "evSpa", "evSpd", "evSpe",
        ];

        /// <summary>Stat order of the game's records: HP, Atk, Def, SpA, SpD, Spe.</summary>
        private static readonly string[] Stats = ["Hp", "Atk", "Def", "Spa", "Spd", "Spe"];

        public string Name => Trainers;

        public IReadOnlyList<string> Fields { get; } =
        [
            .. RecordFields,
            .. Enumerable.Range(1, 6).SelectMany(slot => MemberFields.Select(f => $"p{slot}.{f}")),
        ];

        public int Count(GameData data) => data.Trainers.Length;

        public JsonNode Get(GameData data, int id, string field)
        {
            var trainer = Entry(data, id);
            if (Slot(field, out int slot, out string member))
                return JsonValue.Create(trainer.Member(slot) is { } mon ? ReadMember(mon, member) : 0);
            return JsonValue.Create(ReadRecord(trainer, field));
        }

        public void Set(GameData data, int id, string field, JsonNode value)
        {
            var trainer = Entry(data, id);
            int number = value.GetValue<int>();
            if (Slot(field, out int slot, out string member))
            {
                if (trainer.Grow(slot) is { } mon)
                    WriteMember(mon, member, number);
                return;
            }
            WriteRecord(trainer, field, number);
        }

        /// <summary>"p3.level" → slot 2, "level". Anything else is a field of the record itself.</summary>
        private static bool Slot(string field, out int slot, out string member)
        {
            slot = -1;
            member = field;
            if (field.Length < 4 || field[0] != 'p' || field[2] != '.' || field[1] is < '1' or > '6')
                return false;
            slot = field[1] - '1';
            member = field[3..];
            return true;
        }

        private static int ReadRecord(Trainer t, string field) => field switch
        {
            "ai" => t.Ai,
            "class" => t.TrainerClass,
            "battleType" => t.BattleType,
            "money" => t.Money,
            "flag" => t.Flag ? 1 : 0,
            "customMoves" => t.CustomMoves ? 1 : 0,
            "heldItems" => t.HeldItems ? 1 : 0,
            "count" => t.Count,
            "item1" or "item2" or "item3" or "item4" => t.GetItem(field[^1] - '1'),
            _ => throw Unknown(field),
        };

        private static void WriteRecord(Trainer t, string field, int value)
        {
            switch (field)
            {
                case "ai": t.Ai = value; break;
                case "class": t.TrainerClass = value; break;
                case "battleType": t.BattleType = value; break;
                case "money": t.Money = value; break;
                case "flag": t.Flag = value != 0; break;
                case "customMoves": t.CustomMoves = value != 0; break;
                case "heldItems": t.HeldItems = value != 0; break;
                case "count": t.Count = value; break;
                case "item1" or "item2" or "item3" or "item4": t.SetItem(field[^1] - '1', value); break;
                default: throw Unknown(field);
            }
        }

        private static int ReadMember(TrainerMon m, string field) => field switch
        {
            "species" => m.Species,
            "form" => m.Form,
            "level" => m.Level,
            "ability" => m.Ability,
            "gender" => m.Gender,
            "item" => m.Item,
            "move1" or "move2" or "move3" or "move4" => m.GetMove(field[^1] - '1'),
            "ivs" => m.IvByte,
            "nature" => m.Nature,
            "shiny" => m.Shiny ? 1 : 0,
            _ when Stat(field, "iv") is { } iv => m.GetIv(iv),
            _ when Stat(field, "ev") is { } ev => m.GetEv(ev),
            _ => throw Unknown(field),
        };

        private static void WriteMember(TrainerMon m, string field, int value)
        {
            switch (field)
            {
                case "species": m.Species = value; return;
                case "form": m.Form = value; return;
                case "level": m.Level = value; return;
                case "ability": m.Ability = value; return;
                case "gender": m.Gender = value; return;
                case "item": m.Item = value; return;
                case "move1" or "move2" or "move3" or "move4": m.SetMove(field[^1] - '1', value); return;
                case "ivs": m.IvByte = value; return;
                case "nature": m.Nature = value; return;
                case "shiny": m.Shiny = value != 0; return;
            }
            if (Stat(field, "iv") is { } iv)
                m.SetIv(iv, value);
            else if (Stat(field, "ev") is { } ev)
                m.SetEv(ev, value);
            else
                throw Unknown(field);
        }

        /// <summary>"ivSpa" with prefix "iv" → 3; null when it is not one of the six.</summary>
        private static int? Stat(string field, string prefix)
        {
            if (!field.StartsWith(prefix, StringComparison.Ordinal))
                return null;
            int index = Array.IndexOf(Stats, field[prefix.Length..]);
            return index < 0 ? null : index;
        }

        private static Trainer Entry(GameData data, int id)
        {
            if ((uint)id >= data.Trainers.Length)
                throw new ArgumentOutOfRangeException(nameof(id), id, string.Format(Strings.Tables_OutOfRange, Trainers, data.Trainers.Length));
            return data.Trainers[id];
        }

        private static ArgumentException Unknown(string field) =>
            new(string.Format(Strings.Tables_UnknownField, Trainers, field), nameof(field));
    }

    /// <summary>The item table; only the price for now, which is what the shops charge.</summary>
    private sealed class ItemTableImpl : ITable
    {
        public string Name => Items;
        public IReadOnlyList<string> Fields { get; } = ["price"];
        public int Count(GameData data) => data.Items.Length;

        public JsonNode Get(GameData data, int id, string field)
        {
            Check(data, id, field);
            return JsonValue.Create(data.Items[id].BuyPrice);
        }

        public void Set(GameData data, int id, string field, JsonNode value)
        {
            Check(data, id, field);
            // The game stores a tenth of the price in a ushort, so anything not a multiple of 10 rounds down.
            var item = data.Items[id];
            item.BuyPrice = Math.Clamp(value.GetValue<int>(), 0, ushort.MaxValue * 10);
            data.Items[id] = item;
        }

        private static void Check(GameData data, int id, string field)
        {
            if (field != "price")
                throw new ArgumentException(string.Format(Strings.Tables_UnknownField, Items, field), nameof(field));
            if ((uint)id >= data.Items.Length)
                throw new ArgumentOutOfRangeException(nameof(id), id, string.Format(Strings.Tables_OutOfRange, Items, data.Items.Length));
        }
    }

    private sealed class MoveTextTableImpl : ITable
    {
        public string Name => MoveTexts;
        public IReadOnlyList<string> Fields { get; } = [Description];
        public int Count(GameData data) => data.MoveDescriptions.Length;

        public JsonNode Get(GameData data, int id, string field) => JsonValue.Create(data.MoveDescriptions[Index(data, id, field)]);

        /// <summary>Line breaks normalized to "\n" and trailing spaces or breaks removed, so equal texts compare equal.</summary>
        public void Set(GameData data, int id, string field, JsonNode value) =>
            data.MoveDescriptions[Index(data, id, field)] = value.GetValue<string>().Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd();

        private static int Index(GameData data, int id, string field)
        {
            if (field != Description)
                throw new ArgumentException(string.Format(Strings.Tables_UnknownField, MoveTexts, field), nameof(field));
            if ((uint)id >= data.MoveDescriptions.Length)
                throw new ArgumentOutOfRangeException(nameof(id), id, string.Format(Strings.Tables_OutOfRange, MoveTexts, data.MoveDescriptions.Length));
            return id;
        }
    }
}
