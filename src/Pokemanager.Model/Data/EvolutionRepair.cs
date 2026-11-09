using pk3DS.Core.CTR;
using Pokemanager.Model.Dump;

namespace Pokemanager.Model.Data;

/// <summary>
/// Puts back the evolutions UPR ZX drops (2026-11-09, user: "Pikachu does not evolve into Raichu any more").
/// </summary>
/// <remarks>
/// <para>
/// Measured on the user's Ultra Moon + Expansion ROM: the randomizer's "change impossible evolutions" removes every
/// evolution that only happens in Ultra Space — method 41 (level up there) and 42 (use an item there) — and keeps the
/// other evolution into the same species. Twelve were lost that way: Pikachu → Raichu, Exeggcute → Exeggutor and
/// Petilil → Lilligant (42, the other form by the stone), Cubone, Koffing, Cyndaquil, Mime Jr., Oshawott, Rufflet,
/// Sandygast, Goomy, Bergmite and Rowlet (41). With them gone a form of the species can no longer be had by evolving.
/// </para>
/// <para>
/// They come back with methods that work anywhere, so both forms can be had: a stone one (42) stays a stone for the
/// form the randomizer kept and becomes "level up holding that stone" (day and night) for the other; a level one (41)
/// is split by time — the form the randomizer kept by day, the other by night, at the same level.
/// </para>
/// <para>
/// Only that case is touched: an Ultra Space evolution of the base whose species the randomized record still evolves
/// into in another form. A run with randomized evolutions points elsewhere and is left alone. Running it twice changes
/// nothing the second time.
/// </para>
/// </remarks>
public static class EvolutionRepair
{
    private const int EntrySize = 8;
    private const int LevelUpWormhole = 41, UseItemWormhole = 42;
    private const int LevelUpHeldItemDay = 19, LevelUpHeldItemNight = 20, LevelUpMorning = 32, LevelUpNight = 33;

    private readonly record struct Entry(int Method, int Argument, int Species, int Form, int Level);

    /// <summary>Repairs the evolution archive of a randomized romfs against the base it was made from.</summary>
    /// <returns>How many evolutions were put back (0 when the archive is not there or needs nothing).</returns>
    public static int RepairOutput(string randomizedRomFs, RomFsLayers baseLayers, GameTitle game)
    {
        if (game.Generation() != 7)
            return 0;
        string relative = game.Layout().Evolution;
        string output = Path.Combine(randomizedRomFs, relative);
        if (!File.Exists(output) || baseLayers.TryResolve(relative) is not { } basePath)
            return 0;
        var randomized = new GARC.MemGARC(File.ReadAllBytes(output));
        var original = new GARC.MemGARC(File.ReadAllBytes(basePath));
        byte[][] files = randomized.Files;
        int restored = Repair(original.Files, files, PersonalForms(baseLayers, game));
        if (restored > 0)
        {
            string temp = output + ".tmp";
            File.WriteAllBytes(temp, GARC.PackGARC(files, randomized.Version, randomized.ContentPadding).Data);
            File.Move(temp, output, overwrite: true);
        }
        return restored;
    }

    /// <summary>The form each record (personal row) is, to resolve "-1 = the same form as the one evolving".</summary>
    private static Func<int, int> PersonalForms(RomFsLayers layers, GameTitle game)
    {
        // Loaded only when a record needs it: most opens find nothing to repair.
        var index = new Lazy<PersonalIndex?>(() =>
        {
            try { return PersonalIndex.Of(GameData.Load(layers, game)); }
            catch (Exception e) when (e is IOException or InvalidDataException or ArgumentException or IndexOutOfRangeException) { return null; }
        });
        return row => index.Value?.Pokemon(row)?.Form ?? 0;
    }

    /// <summary>The repair on the records themselves (Generation 7, 8 entries of 8 bytes each); changes <paramref name="randomized"/>.</summary>
    public static int Repair(byte[][] original, byte[][] randomized, Func<int, int> formOfRow)
    {
        int restored = 0;
        for (int row = 0; row < Math.Min(original.Length, randomized.Length); row++)
        {
            byte[] record = randomized[row];
            if (original[row].Length != record.Length || record.Length % EntrySize != 0)
                continue;
            var before = Read(original[row]);
            var after = Read(record);
            bool changed = false;
            foreach (var lost in before.Where(e => e.Method is LevelUpWormhole or UseItemWormhole))
            {
                int Form(Entry e) => e.Form < 0 ? formOfRow(row) : e.Form;
                if (after.Any(e => e.Method != 0 && e.Species == lost.Species && Form(e) == Form(lost)))
                    continue; // still there
                int kept = after.FindIndex(e => e.Method != 0 && e.Species == lost.Species);
                if (kept < 0)
                    continue; // the randomizer sent this Pokémon elsewhere: not ours to change
                int free = after.Count(e => e.Method == 0);
                if (lost.Method == UseItemWormhole)
                {
                    if (free < 2)
                        continue;
                    Put(after, new Entry(LevelUpHeldItemDay, lost.Argument, lost.Species, lost.Form, 0));
                    Put(after, new Entry(LevelUpHeldItemNight, lost.Argument, lost.Species, lost.Form, 0));
                }
                else
                {
                    if (free < 1)
                        continue;
                    var k = after[kept];
                    int level = k.Level > 0 ? k.Level : lost.Level; // the randomizer may have moved the level (Rufflet 54 → 40): both follow it
                    if (k.Method == 4)
                        after[kept] = k with { Method = LevelUpMorning };
                    Put(after, new Entry(LevelUpNight, 0, lost.Species, lost.Form, level));
                }
                changed = true;
                restored++;
            }
            if (changed)
                Write(after, record);
        }
        return restored;
    }

    private static List<Entry> Read(byte[] record)
    {
        var list = new List<Entry>();
        for (int o = 0; o + EntrySize <= record.Length; o += EntrySize)
            list.Add(new Entry(BitConverter.ToUInt16(record, o), BitConverter.ToUInt16(record, o + 2),
                BitConverter.ToUInt16(record, o + 4), (sbyte)record[o + 6], record[o + 7]));
        return list;
    }

    private static void Put(List<Entry> entries, Entry entry) => entries[entries.FindIndex(e => e.Method == 0)] = entry;

    private static void Write(List<Entry> entries, byte[] record)
    {
        for (int i = 0; i < entries.Count; i++)
        {
            int o = i * EntrySize;
            var e = entries[i];
            BitConverter.TryWriteBytes(record.AsSpan(o), (ushort)e.Method);
            BitConverter.TryWriteBytes(record.AsSpan(o + 2), (ushort)e.Argument);
            BitConverter.TryWriteBytes(record.AsSpan(o + 4), (ushort)e.Species);
            record[o + 6] = (byte)(sbyte)e.Form;
            record[o + 7] = (byte)e.Level;
        }
    }
}
