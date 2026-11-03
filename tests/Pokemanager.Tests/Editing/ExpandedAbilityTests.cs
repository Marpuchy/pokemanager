using pk3DS.Core.Structures.PersonalInfo;

namespace Pokemanager.Tests.Editing;

/// <summary>
/// The USUM Expansion numbers abilities past 255 and keeps the ninth bit of each of the three in byte 0x53 of the
/// personal entry. Measured on its files: Nacli 0x10 / 0x05 / 0x1D with 0x53 = 1 (Purifying Salt 272, Sturdy,
/// Clear Body), Koraidon 0x20 ×3 with 0x53 = 7 (Orichalcum Pulse 288).
/// </summary>
public sealed class ExpandedAbilityTests
{
    private static PersonalInfoSM Entry(byte a1, byte a2, byte a3, byte high)
    {
        var data = new byte[PersonalInfoSM.SIZE];
        data[0x18] = a1;
        data[0x19] = a2;
        data[0x1A] = a3;
        data[PersonalInfoSM.AbilityHighBits] = high;
        return new PersonalInfoSM(data);
    }

    [Fact]
    public void TheNinthBit_IsRead()
    {
        Assert.Equal([272, 5, 29], Entry(0x10, 0x05, 0x1D, 1).Abilities);
        Assert.Equal([288, 288, 288], Entry(0x20, 0x20, 0x20, 7).Abilities);
        Assert.Equal([65, 65, 34], Entry(65, 65, 34, 0).Abilities); // the game's own entries: 0x53 is 0
    }

    [Fact]
    public void AnAbilityUnder256_ClearsItsBit_AndOneOver256_SetsIt()
    {
        // Nacli randomized to Sand Rush: before, the bit stayed and the game read ability 402, which has no name.
        var nacli = Entry(0x10, 0x05, 0x1D, 1);
        nacli.Abilities = [146, 5, 29];
        Assert.Equal([146, 5, 29], nacli.Abilities);
        Assert.Equal(0, nacli.Write()[PersonalInfoSM.AbilityHighBits]);

        nacli.Abilities = [146, 300, 272];
        Assert.Equal([146, 300, 272], nacli.Abilities);
        Assert.Equal(0b110, nacli.Write()[PersonalInfoSM.AbilityHighBits]);
    }

    [Fact]
    public void TheOtherBitsOfThatByte_AreKept()
    {
        var entry = Entry(1, 2, 3, 0xF9);
        entry.Abilities = [4, 5, 6];
        Assert.Equal(0xF8, entry.Write()[PersonalInfoSM.AbilityHighBits]);
    }
}
