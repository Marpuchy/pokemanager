using PKHeX.Core;
using Pokemanager.Save;

namespace Pokemanager.Multiplayer;

/// <summary>
/// One Pokémon as the Locke rules see it. <see cref="Key"/> (the encryption constant) survives evolution, nicknames,
/// moving between boxes and the ROM changing.
/// </summary>
/// <param name="Box">Box index, or null for the party.</param>
/// <param name="Hp">Current HP; only known for the party (boxed Pokémon are healed), -1 otherwise.</param>
/// <param name="PrimaryType">Primary type in the ROM being played (randomized), -1 when unknown.</param>
/// <param name="Owned">Caught by this trainer (not traded in).</param>
public sealed record SnapshotPokemon(
    uint Key,
    ushort Species,
    byte Form,
    string Nickname,
    int Level,
    int? Box,
    int Hp,
    int MetLocation,
    bool WasEgg,
    bool IsEgg,
    bool IsShiny,
    int PrimaryType,
    bool Owned,
    DateOnly? MetDate);

/// <summary>What the rules need from a save at one moment: its Pokémon and milestones. Neutral, serializable.</summary>
public sealed record SaveSnapshot(
    int Generation,
    string Game,
    int BoxCount,
    int MilestoneCount,
    List<bool> Milestones,
    List<SnapshotPokemon> Pokemon,
    DateTime Taken)
{
    public int MilestonesEarned => Milestones.Count(m => m);

    public static SaveSnapshot Read(SaveDocument save, DateTime taken)
    {
        var list = new List<SnapshotPokemon>();
        for (int i = 0; i < save.PartyCount; i++)
            Add(save, new SaveSlot(null, i), list);
        for (int box = 0; box < save.BoxCount; box++)
            for (int slot = 0; slot < save.BoxSlotCount; slot++)
                Add(save, new SaveSlot(box, slot), list);
        var milestones = Enumerable.Range(0, save.MilestoneCount).Select(save.GetMilestone).ToList();
        return new SaveSnapshot(save.Generation, save.Version.ToString(), save.BoxCount, save.MilestoneCount, milestones,
            list, taken);
    }

    private static void Add(SaveDocument save, SaveSlot slot, List<SnapshotPokemon> list)
    {
        var pk = save.Get(slot);
        if (pk.Species == 0 || pk.EncryptionConstant == 0)
            return;
        bool owned = pk.OriginalTrainerName == save.TrainerName && pk.TID16 == save.TrainerId && pk.SID16 == save.SecretId;
        var types = save.Personal(pk.Species, pk.Form)?.Types;
        DateOnly? met = null;
        if (pk.MetMonth is >= 1 and <= 12)
        {
            int year = 2000 + pk.MetYear;
            met = new DateOnly(year, pk.MetMonth, Math.Clamp((int)pk.MetDay, 1, DateTime.DaysInMonth(year, pk.MetMonth)));
        }
        list.Add(new SnapshotPokemon(
            pk.EncryptionConstant, pk.Species, pk.Form, pk.Nickname, save.Level(pk), slot.Box,
            slot.IsParty ? pk.Stat_HPCurrent : -1, pk.MetLocation, pk.WasEgg, pk.IsEgg, pk.IsShiny,
            types is { Length: > 0 } ? types[0] : -1, owned, met));
    }
}
