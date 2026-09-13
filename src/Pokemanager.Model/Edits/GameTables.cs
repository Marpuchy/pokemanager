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
    ]);

    public static readonly ITable MoveTable = new IntTable<Move6>(Moves, d => d.Moves,
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

    public static IReadOnlyList<ITable> All { get; } = [PersonalTable, MoveTable, LearnsetTable];

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
}
