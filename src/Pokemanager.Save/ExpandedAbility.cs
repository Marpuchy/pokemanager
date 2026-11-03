using PKHeX.Core;

namespace Pokemanager.Save;

/// <summary>
/// The ability of a Generation 7 Pokémon under an expansion mod, which numbers abilities past 255. The save keeps the
/// low byte where the game always did (0x14) and the ninth bit in bit 4 of the next byte, next to the ability number
/// (0x15, bits 0-2). Measured on the user's Ultra Moon save with the USUM Expansion: a Nacli created while its ROM said
/// ability 402 stores 0x92 and 0x11 — and the game shows a blank ability, even after the ROM was fixed, until that bit goes.
/// </summary>
public static class ExpandedAbility
{
    private const int NumberByte = 0x15;
    private const int NinthBit = 0x10;

    public static int Get(PKM pk) =>
        pk is PK7 ? pk.Ability | ((pk.Data[NumberByte] & NinthBit) != 0 ? 0x100 : 0) : pk.Ability;

    /// <returns>Whether anything changed.</returns>
    public static bool Set(PKM pk, int ability)
    {
        if (Get(pk) == ability)
            return false;
        if (pk is PK7)
        {
            pk.Ability = ability & 0xFF;
            pk.Data[NumberByte] = (byte)(ability > byte.MaxValue ? pk.Data[NumberByte] | NinthBit : pk.Data[NumberByte] & ~NinthBit);
        }
        else
        {
            pk.Ability = ability;
        }
        return true;
    }
}
