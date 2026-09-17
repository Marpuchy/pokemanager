using Pokemanager.Model.Data;
using Pokemanager.Model.Dump;

namespace Pokemanager.Tests.Editing;

/// <summary>
/// The trainer model over hand-made records, so both generations are covered without a ROM. Generation 6 keeps one IVs
/// byte and no EVs or nature; Generation 7 keeps the six IVs, the six EVs, the nature and shiny.
/// </summary>
public class TrainerTableTests
{
    /// <summary>X/Y record: format, class, battle type, count, four items, AI, three zeros, healer, money, prize.</summary>
    private static byte[] Record6(byte ai = 7, byte count = 1, byte format = 0, ushort item = 0)
    {
        byte[] data = new byte[0x14];
        data[0] = format;
        data[1] = 30; // class
        data[2] = 0; // single battle
        data[3] = count;
        BitConverter.GetBytes(item).CopyTo(data, 4);
        data[0x0C] = ai;
        data[0x11] = 40; // money
        return data;
    }

    /// <summary>Team entry without item or moves: IVs byte, packed ability/gender, level, species, form.</summary>
    private static byte[] Member6(byte ivs = 150, ushort level = 29, ushort species = 619)
    {
        byte[] data = new byte[8];
        data[0] = ivs;
        data[1] = 1 << 4; // ability slot 1
        BitConverter.GetBytes(level).CopyTo(data, 2);
        BitConverter.GetBytes(species).CopyTo(data, 4);
        return data;
    }

    [Fact]
    public void Gen6_ReadsTheRecordAndTheTeam()
    {
        var trainer = Trainer.Read(GameTitle.X, Record6(ai: 7, item: 25), Member6());

        Assert.False(trainer.Unreadable);
        Assert.Equal(6, trainer.Generation);
        Assert.Equal(7, trainer.Ai);
        Assert.Equal(30, trainer.TrainerClass);
        Assert.Equal(40, trainer.Money);
        Assert.Equal(25, trainer.GetItem(0));
        Assert.Equal(0, trainer.GetItem(3));
        Assert.Equal(1, trainer.Count);

        var mon = trainer.Member(0)!;
        Assert.Equal(619, mon.Species);
        Assert.Equal(29, mon.Level);
        Assert.Equal(1, mon.Ability);
        Assert.Equal(150, mon.IvByte);
        // Generation 7 fields are not there.
        Assert.Equal(0, mon.Nature);
        Assert.Equal(0, mon.GetIv(0));
        Assert.Equal(0, mon.GetEv(1));
        Assert.False(mon.Shiny);
    }

    [Fact]
    public void Gen6_WritingAGeneration7FieldDoesNothing()
    {
        var trainer = Trainer.Read(GameTitle.X, Record6(), Member6());
        byte[] before = trainer.WriteTeam();

        var mon = trainer.Member(0)!;
        mon.Nature = 5;
        mon.SetIv(0, 31);
        mon.SetEv(2, 252);
        mon.Shiny = true;

        Assert.Equal(0, mon.Nature);
        Assert.Equal(before, trainer.WriteTeam());
    }

    [Fact]
    public void Gen6_RoundTripsByteForByte()
    {
        byte[] record = Record6(ai: 5, count: 2, format: 3, item: 17);
        // format 3 = the entries carry a held item and four moves: 8 + 2 + 8 bytes each.
        byte[] team = [.. Member6().Concat(new byte[10]), .. Member6(ivs: 0, level: 31, species: 25).Concat(new byte[10])];

        var trainer = Trainer.Read(GameTitle.X, record, team);

        Assert.Equal(2, trainer.Count);
        Assert.True(trainer.CustomMoves);
        Assert.True(trainer.HeldItems);
        Assert.Equal(record, trainer.WriteData());
        Assert.Equal(team, trainer.WriteTeam());
    }

    [Fact]
    public void Gen6_GrowingTheTeamAddsAMember()
    {
        var trainer = Trainer.Read(GameTitle.X, Record6(count: 1), Member6());

        var added = trainer.Grow(2)!;
        added.Species = 25;
        added.Level = 50;

        Assert.Equal(3, trainer.Count);
        Assert.Equal(3, trainer.WriteData()[3]);
        Assert.Equal(3 * 8, trainer.WriteTeam().Length);
        Assert.Equal(25, trainer.Member(2)!.Species);
    }

    [Fact]
    public void Gen7_ReadsAndWritesTheRicherEntry()
    {
        byte[] record = new byte[0x14];
        BitConverter.GetBytes((ushort)12).CopyTo(record, 0); // class
        record[2] = 1; // doubles
        record[3] = 1; // one Pokémon
        BitConverter.GetBytes((ushort)78).CopyTo(record, 4); // first bag item
        record[0x0C] = 0x87;
        record[0x11] = 60;

        byte[] member = new byte[0x20];
        member[0] = (1 << 4) | 2; // ability slot 1, gender 2
        member[1] = 15; // nature
        member[2] = 252; // EV HP
        BitConverter.GetBytes(0x7FFFFFFFu).CopyTo(member, 8); // every IV 31
        member[0x0E] = 50; // level
        BitConverter.GetBytes((ushort)727).CopyTo(member, 0x10);
        BitConverter.GetBytes((ushort)270).CopyTo(member, 0x14); // item
        BitConverter.GetBytes((ushort)53).CopyTo(member, 0x18); // first move

        var trainer = Trainer.Read(GameTitle.UltraMoon, record, member);

        Assert.Equal(7, trainer.Generation);
        Assert.Equal(0x87, trainer.Ai);
        Assert.Equal(1, trainer.BattleType);
        Assert.Equal(78, trainer.GetItem(0));
        Assert.Equal(60, trainer.Money);

        var mon = trainer.Member(0)!;
        Assert.Equal(727, mon.Species);
        Assert.Equal(50, mon.Level);
        Assert.Equal(270, mon.Item);
        Assert.Equal(53, mon.GetMove(0));
        Assert.Equal(15, mon.Nature);
        Assert.Equal(252, mon.GetEv(0));
        Assert.Equal(31, mon.GetIv(3));
        // The Generation 6 IVs byte does not exist here.
        Assert.Equal(0, mon.IvByte);
        mon.IvByte = 255;
        Assert.Equal(0, mon.IvByte);

        Assert.Equal(record, trainer.WriteData());
        Assert.Equal(member, trainer.WriteTeam());
    }

    [Fact]
    public void IvConversion_MatchesTheValuesTheGameUses()
    {
        // pk3DS reads the Generation 6 byte as value / 8; these are the numbers measured in the real X dump.
        Assert.Equal(0, TrainerIvs.FromByte(0));      // filler trainers
        Assert.Equal(18, TrainerIvs.FromByte(150));   // gym leaders
        Assert.Equal(25, TrainerIvs.FromByte(200));   // the Champion of X
        Assert.Equal(31, TrainerIvs.FromByte(255));

        Assert.Equal(0, TrainerIvs.ToByte(0));
        Assert.Equal(144, TrainerIvs.ToByte(18));
        Assert.Equal(200, TrainerIvs.ToByte(25));
        // The highest goes back as 255, not 248, so "the highest" is really the highest the byte holds.
        Assert.Equal(255, TrainerIvs.ToByte(31));
        Assert.Equal(255, TrainerIvs.ToByte(99));
        Assert.Equal(0, TrainerIvs.ToByte(-5));
    }

    [Fact]
    public void UnreadableRecord_IsPassedThroughUntouched()
    {
        byte[] data = [1, 2, 3];
        byte[] team = [4, 5];

        var trainer = Trainer.Read(GameTitle.X, data, team);

        Assert.True(trainer.Unreadable);
        Assert.Equal(0, trainer.Ai);
        trainer.Ai = 7; // writing does nothing
        Assert.Equal(data, trainer.WriteData());
        Assert.Equal(team, trainer.WriteTeam());
    }
}
