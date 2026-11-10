using pk3DS.Core.Structures;

namespace Pokemanager.Model.Data;

/// <summary>What an evolution needs besides its method: nothing, a level, an item, a move, a Pokémon or a move type.</summary>
public enum EvolutionArgument { None, Level, Item, Move, Species, Type }

/// <summary>
/// The evolutions of one personal row (species or form): eight slots of method, argument and target, as the game's
/// evolution archive keeps them (XY <c>a/2/1/5</c>, ORAS <c>a/1/9/2</c>, Gen 7 <c>a/0/1/4</c>). Generation 7 adds the
/// target form and a level of its own; in Generation 6 the level of a level method <b>is</b> the argument. A record
/// of another size is kept as it was read and written back untouched.
/// </summary>
public sealed class EvolutionSlots
{
    public const int Count = 8;

    private readonly byte[] raw;
    private readonly EvolutionSet? set;

    public bool Generation7 { get; }

    public EvolutionSlots(byte[] data, bool generation7)
    {
        raw = data;
        Generation7 = generation7;
        EvolutionSet parsed = generation7 ? new EvolutionSet7(data) : new EvolutionSet6(data);
        set = parsed.PossibleEvolutions is { Length: Count } ? parsed : null;
    }

    /// <summary>False for a record of an unexpected size: it reads as empty and is written back as it was.</summary>
    public bool IsReadable => set is not null;

    public EvolutionMethod? Slot(int index) => set?.PossibleEvolutions[index];

    public byte[] Write() => set?.Write() ?? raw;

    /// <summary>The slots in use, in the game's order.</summary>
    public IEnumerable<(int Index, EvolutionMethod Evolution)> InUse() =>
        set is null ? [] : set.PossibleEvolutions.Select((e, i) => (i, e)).Where(p => p.e.Method != 0);

    /// <summary>
    /// The methods of Generations 6 and 7 (PKHeX's <c>EvolutionType</c>, 0-42), with what each needs. The friendship of
    /// methods 1-3 is a threshold of the game's code (220), not part of the data.
    /// </summary>
    public const int MethodCount = 43;

    public static EvolutionArgument ArgumentOf(int method) => method switch
    {
        6 or 8 or 17 or 18 or 19 or 20 or 42 => EvolutionArgument.Item,
        21 => EvolutionArgument.Move,
        22 => EvolutionArgument.Species,
        29 => EvolutionArgument.Type,
        _ => EvolutionArgument.None,
    };

    /// <summary>Methods that happen on reaching a level (the level is the argument in Generation 6).</summary>
    public static bool UsesLevel(int method) => method is 4 or (>= 9 and <= 15) or 23 or 24 or 30 or 31 or 32 or 33 or 34
        or 36 or 37 or 38 or 40 or 41;

    /// <summary>
    /// The form an evolution gives. A slot's form -1 means "the same form", which only counts when the target has it
    /// (Galarian Meowth becomes the one Perrserker). Method 34 (PKHeX's <c>LevelUpFormFemale1</c>) is Espurr's female
    /// branch: its form is -1 too, but the game makes it form 1 — read as "the same", female Meowstic was lost.
    /// </summary>
    public static int TargetForm(EvolutionMethod e, int fromForm, int targetFormCount, bool generation7)
    {
        if (!generation7)
            return e.Method == 34 && targetFormCount > 1 ? 1 : 0;
        if (e.Form >= 0)
            return e.Form;
        if (e.Method == 34 && targetFormCount > 1)
            return 1;
        return fromForm < targetFormCount ? fromForm : 0;
    }

    /// <summary>The level an evolution happens at, in whichever place the generation keeps it.</summary>
    public static int LevelOf(EvolutionMethod e, bool generation7) =>
        UsesLevel(e.Method) ? generation7 ? e.Level : e.Argument : 0;
}
