using pk3DS.Core.CTR;
using pk3DS.Core.Structures;
using Pokemanager.Model.Dump;

namespace Pokemanager.Model.Data;

/// <summary>
/// A trainer as the game stores it, neutral over the generation: the record in <c>trdata</c> (class, battle type, bag,
/// the AI byte, money) and the team in <c>trpoke</c>.
/// </summary>
/// <remarks>
/// The two formats are genuinely different and the differences are not hidden. Generation 6 keeps one byte per team
/// member for its IVs (0–255, the game divides it) and has no EVs or nature; Generation 7 keeps the six IVs, the six
/// EVs, the nature and a shiny flag. A field the game does not have reads 0 and writing it does nothing, the same rule
/// the personal table uses for the Generation 7 Z-move fields.
/// <para>
/// Everything not exposed here (the prize, the bytes that are always 0 in the real dumps) is kept in the backing
/// structure and written back untouched, so a trainer that is read and written again is byte-identical.
/// </para>
/// </remarks>
public sealed class Trainer
{
    private readonly TrainerData6? six;
    private readonly TrainerData7? seven;
    private readonly byte[] rawData;
    private readonly byte[] rawTeam;

    /// <summary>6 or 7; <see cref="Unreadable"/> trainers report the generation of their game anyway.</summary>
    public int Generation { get; }

    /// <summary>A record this application could not parse: it is passed through to the build untouched.</summary>
    public bool Unreadable => six is null && seven is null;

    private Trainer(int generation, byte[] rawData, byte[] rawTeam, TrainerData6? six = null, TrainerData7? seven = null)
    {
        Generation = generation;
        this.rawData = rawData;
        this.rawTeam = rawTeam;
        this.six = six;
        this.seven = seven;
    }

    /// <summary>Reads one trainer; a record that does not parse becomes an <see cref="Unreadable"/> pass-through.</summary>
    public static Trainer Read(GameTitle title, byte[] data, byte[] team)
    {
        int generation = title.Generation();
        try
        {
            if (generation == 6)
            {
                // The first file of the archive is a dummy; a record with no Pokémon divides by zero in pk3DS and is
                // caught below. Both are passed through untouched.
                if (data.Length < 0x10)
                    return new Trainer(generation, data, team);
                return new Trainer(generation, data, team, six: new TrainerData6(data, team, title.Family() == GameFamily.ORAS));
            }
            if (data.Length < 0x14)
                return new Trainer(generation, data, team);
            return new Trainer(generation, data, team, seven: new TrainerData7(data, team));
        }
        // A record shorter than its format says, or a team that does not match it (the archives have a dummy entry, and
        // a randomizer may leave odd ones): passed through untouched rather than guessed at.
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or DivideByZeroException or IOException)
        {
            return new Trainer(generation, data, team);
        }
    }

    /// <summary>Which AI the game runs for this trainer: a bitfield, see <see cref="TrainerAI"/>.</summary>
    public int Ai
    {
        get => six?.AI ?? seven?.AI ?? 0;
        set
        {
            if (six is not null) six.AI = (byte)value;
            else if (seven is not null) seven.AI = (byte)value;
        }
    }

    /// <summary>Index into the trainer class names; decides the sprite, the music and the prize money multiplier.</summary>
    public int TrainerClass
    {
        get => six?.Class ?? seven?.TrainerClass ?? 0;
        set
        {
            if (six is not null) six.Class = value;
            else if (seven is not null) seven.TrainerClass = value;
        }
    }

    /// <summary>0 single; in Generation 7 the values of <see cref="BattleMode"/> (1 double, 2 multi).</summary>
    public int BattleType
    {
        get => six?.BattleType ?? (int?)seven?.Mode ?? 0;
        set
        {
            if (six is not null) six.BattleType = (byte)value;
            else if (seven is not null) seven.Mode = (BattleMode)value;
        }
    }

    /// <summary>Prize money multiplier.</summary>
    public int Money
    {
        get => six?.Money ?? seven?.Money ?? 0;
        set
        {
            if (six is not null) six.Money = (byte)value;
            else if (seven is not null) seven.Money = value;
        }
    }

    /// <summary>Generation 6: the trainer heals the player's team after the battle. Generation 7: the flag at 0x0D.</summary>
    public bool Flag
    {
        get => six?.Healer ?? seven?.Flag ?? false;
        set
        {
            if (six is not null) six.Healer = value;
            else if (seven is not null) seven.Flag = value;
        }
    }

    /// <summary>Whether the team entries carry their own four moves; otherwise the game fills them from the learnset.</summary>
    public bool CustomMoves
    {
        get => six?.Moves ?? true;
        set { if (six is not null) six.Moves = value; }
    }

    /// <summary>Whether the team entries carry a held item (Generation 7 entries always do).</summary>
    public bool HeldItems
    {
        get => six?.Item ?? true;
        set { if (six is not null) six.Item = value; }
    }

    /// <summary>The four items the trainer may use during the battle (0 = none).</summary>
    public int GetItem(int index) =>
        (uint)index >= 4 ? 0 : six is not null ? six.Items[index] : seven is not null ? SevenItem(index) : 0;

    public void SetItem(int index, int value)
    {
        if ((uint)index >= 4)
            return;
        if (six is not null)
            six.Items[index] = (ushort)value;
        else if (seven is not null)
            SevenItem(index, value);
    }

    private int SevenItem(int index) => index switch
    {
        0 => seven!.Item1, 1 => seven!.Item2, 2 => seven!.Item3, _ => seven!.Item4,
    };

    private void SevenItem(int index, int value)
    {
        switch (index)
        {
            case 0: seven!.Item1 = value; break;
            case 1: seven!.Item2 = value; break;
            case 2: seven!.Item3 = value; break;
            default: seven!.Item4 = value; break;
        }
    }

    /// <summary>Team members, 0 to 6.</summary>
    public int Count
    {
        get => six?.NumPokemon ?? seven?.NumPokemon ?? 0;
        set
        {
            value = Math.Clamp(value, 0, 6);
            if (six is not null)
            {
                var team = six.Team;
                Array.Resize(ref team, value);
                for (int i = 0; i < value; i++)
                    team[i] ??= NewMember6();
                six.Team = team;
                six.NumPokemon = (byte)value;
            }
            else if (seven is not null)
            {
                while (seven.Pokemon.Count > value)
                    seven.Pokemon.RemoveAt(seven.Pokemon.Count - 1);
                while (seven.Pokemon.Count < value)
                    seven.Pokemon.Add(new TrainerPoke7 { Level = 1 });
                seven.NumPokemon = value;
            }
        }
    }

    private static TrainerData6.Pokemon NewMember6() => new(new byte[100], HasItem: false, HasMoves: false) { Level = 1 };

    /// <summary>The team member in that slot, or null when the trainer has no such slot.</summary>
    public TrainerMon? Member(int slot)
    {
        if ((uint)slot >= Count)
            return null;
        if (six is not null)
            return new TrainerMon(six.Team[slot]);
        return seven is not null ? new TrainerMon(seven.Pokemon[slot]) : null;
    }

    /// <summary>The team member in that slot, growing the team if needed; null when the slot cannot exist.</summary>
    public TrainerMon? Grow(int slot)
    {
        if ((uint)slot >= 6)
            return null;
        if (slot >= Count)
            Count = slot + 1;
        return Member(slot);
    }

    public IEnumerable<TrainerMon> Team => Enumerable.Range(0, Count).Select(i => Member(i)!);

    /// <summary>The record as the game stores it.</summary>
    public byte[] WriteData() => six?.Write() ?? WriteSeven().Data ?? rawData;

    /// <summary>The team as the game stores it.</summary>
    public byte[] WriteTeam() => six?.WriteTeam() ?? WriteSeven().Team ?? rawTeam;

    private (byte[]? Data, byte[]? Team) WriteSeven()
    {
        if (seven is null)
            return (null, null);
        seven.Write(out byte[] data, out byte[] team);
        return (data, team);
    }
}

/// <summary>One Pokémon of a trainer's team. See <see cref="Trainer"/> for what each generation actually stores.</summary>
public sealed class TrainerMon
{
    private readonly TrainerData6.Pokemon? six;
    private readonly TrainerPoke7? seven;

    internal TrainerMon(TrainerData6.Pokemon entry) => six = entry;
    internal TrainerMon(TrainerPoke7 entry) => seven = entry;

    public int Species
    {
        get => six?.Species ?? seven?.Species ?? 0;
        set { if (six is not null) six.Species = (ushort)value; else if (seven is not null) seven.Species = value; }
    }

    public int Form
    {
        get => six?.Form ?? seven?.Form ?? 0;
        set { if (six is not null) six.Form = (ushort)value; else if (seven is not null) seven.Form = value; }
    }

    public int Level
    {
        get => six?.Level ?? seven?.Level ?? 0;
        set { if (six is not null) six.Level = (ushort)value; else if (seven is not null) seven.Level = value; }
    }

    /// <summary>Ability slot: in Generation 6 the high nibble of the packed byte, in Generation 7 two bits.</summary>
    public int Ability
    {
        get => six?.Ability ?? seven?.Ability ?? 0;
        set { if (six is not null) six.Ability = value; else if (seven is not null) seven.Ability = value; }
    }

    public int Gender
    {
        get => six?.Gender ?? seven?.Gender ?? 0;
        set { if (six is not null) six.Gender = value; else if (seven is not null) seven.Gender = value; }
    }

    public int Item
    {
        get => six?.Item ?? seven?.Item ?? 0;
        set { if (six is not null) six.Item = (ushort)value; else if (seven is not null) seven.Item = value; }
    }

    public int GetMove(int index)
    {
        if ((uint)index >= 4)
            return 0;
        return six is not null ? six.Moves[index] : seven?.Moves[index] ?? 0;
    }

    public void SetMove(int index, int value)
    {
        if ((uint)index >= 4)
            return;
        if (six is not null)
            six.Moves[index] = (ushort)value;
        else if (seven is not null)
        {
            int[] moves = seven.Moves;
            moves[index] = value;
            seven.Moves = moves;
        }
    }

    /// <summary>
    /// Generation 6 only: the byte the game turns into the six IVs (0–255; pk3DS reads it as <c>IVs = value / 8</c>).
    /// Ordinary trainers have 0, gym leaders 150, the Champion of X 200. Reads 0 and writes nothing in Generation 7,
    /// which has the six IVs instead.
    /// </summary>
    public int IvByte
    {
        get => six?.IVs ?? 0;
        set { if (six is not null) six.IVs = (byte)value; }
    }

    /// <summary>Generation 7 only: one IV, 0–31, in HP/Atk/Def/SpA/SpD/Spe order. Reads 0 and writes nothing in Generation 6.</summary>
    public int GetIv(int stat) => (uint)stat >= 6 ? 0 : seven?.IVs[stat] ?? 0;

    public void SetIv(int stat, int value)
    {
        if (seven is null || (uint)stat >= 6)
            return;
        int[] ivs = seven.IVs;
        ivs[stat] = value;
        seven.IVs = ivs;
    }

    /// <summary>Generation 7 only: one EV, 0–252, in the same order. Reads 0 and writes nothing in Generation 6.</summary>
    public int GetEv(int stat) => (uint)stat >= 6 ? 0 : seven?.EVs[stat] ?? 0;

    public void SetEv(int stat, int value)
    {
        if (seven is null || (uint)stat >= 6)
            return;
        int[] evs = seven.EVs;
        evs[stat] = value;
        seven.EVs = evs;
    }

    /// <summary>Generation 7 only. Reads 0 and writes nothing in Generation 6.</summary>
    public int Nature
    {
        get => seven?.Nature ?? 0;
        set { if (seven is not null) seven.Nature = value; }
    }

    /// <summary>Generation 7 only. Reads false and writes nothing in Generation 6.</summary>
    public bool Shiny
    {
        get => seven?.Shiny ?? false;
        set { if (seven is not null) seven.Shiny = value; }
    }
}

/// <summary>Reads the two trainer archives of a game.</summary>
public static class TrainerArchive
{
    /// <summary>
    /// One <see cref="Trainer"/> per file of the <c>trdata</c> archive. An empty array when the game folder does not
    /// have the archives (imported by a version before 3.0) or they cannot be read.
    /// </summary>
    public static Trainer[] Read(RomFsLayers layers, GameTitle title)
    {
        try
        {
            var layout = title.Layout();
            byte[][] data = GameData.ReadFiles(layers, layout.TrainerData);
            byte[][] teams = GameData.ReadFiles(layers, layout.TrainerPokemon);
            return Enumerable.Range(0, Math.Min(data.Length, teams.Length))
                .Select(i => Trainer.Read(title, data[i], teams[i]))
                .ToArray();
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or IndexOutOfRangeException
                                       or InvalidDataException or ArgumentException)
        {
            return [];
        }
    }
}
