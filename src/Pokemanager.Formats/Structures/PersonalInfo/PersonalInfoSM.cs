using System;
using System.Linq;

namespace pk3DS.Core.Structures.PersonalInfo;

public class PersonalInfoSM : PersonalInfoXY
{
    public new const int SIZE = 0x54;

    public PersonalInfoSM(byte[] data)
    {
        if (data.Length != SIZE)
            return;
        Data = data;

        TMHM = GetBits(Data.Skip(0x28).Take(0x10).ToArray()); // 36-39
        TypeTutors = GetBits(Data.Skip(0x38).Take(0x4).ToArray()); // 40
        SpecialTutors =
        [
            GetBits(Data.Skip(0x3C).Take(0x0A).ToArray()),
        ];
    }

    public override byte[] Write()
    {
        SetBits(TMHM).CopyTo(Data, 0x28);
        SetBits(TypeTutors).CopyTo(Data, 0x38);
        SetBits(SpecialTutors[0]).CopyTo(Data, 0x3C);
        return Data;
    }

    // No accessing for 3C-4B

    public int SpecialZ_Item { get => BitConverter.ToUInt16(Data, 0x4C); set => BitConverter.GetBytes((ushort)value).CopyTo(Data, 0x4C); }
    public int SpecialZ_BaseMove { get => BitConverter.ToUInt16(Data, 0x4E); set => BitConverter.GetBytes((ushort)value).CopyTo(Data, 0x4E); }
    public int SpecialZ_ZMove { get => BitConverter.ToUInt16(Data, 0x50); set => BitConverter.GetBytes((ushort)value).CopyTo(Data, 0x50); }
    public bool LocalVariant { get => Data[0x52] == 1; set => Data[0x52] = value ? (byte)1 : (byte)0; }

    /// <summary>
    /// Pokemanager: an expansion mod (USUM Expansion) numbers abilities past 255 and keeps the ninth bit of each of the
    /// three in byte 0x53, bits 0-2 (Nacli: 0x10 with bit 0 set = 272, Purifying Salt). The game ships that byte as 0, so
    /// reading it changes nothing for a game without the mod. Writing an ability under 256 clears its bit: leaving it set
    /// turned a randomized Sand Rush (146) into ability 402, which has no name.
    /// </summary>
    public const int AbilityHighBits = 0x53;

    public override int[] Abilities
    {
        get =>
        [
            Data[0x18] | ((Data[AbilityHighBits] & 1) << 8),
            Data[0x19] | (((Data[AbilityHighBits] >> 1) & 1) << 8),
            Data[0x1A] | (((Data[AbilityHighBits] >> 2) & 1) << 8),
        ];
        set
        {
            if (value?.Length != 3) return;
            Data[0x18] = (byte)value[0];
            Data[0x19] = (byte)value[1];
            Data[0x1A] = (byte)value[2];
            int high = ((value[0] >> 8) & 1) | (((value[1] >> 8) & 1) << 1) | (((value[2] >> 8) & 1) << 2);
            Data[AbilityHighBits] = (byte)((Data[AbilityHighBits] & 0xF8) | high);
        }
    }
}