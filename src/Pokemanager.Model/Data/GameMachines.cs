using Pokemanager.Model.Dump;

namespace Pokemanager.Model.Data;

/// <summary>
/// The moves the TMs teach, read from the executable the way UPR ZX finds them (2026-11-10, user: "las descripciones de
/// las MT se ven las del juego original"). Generation 7 only: the table is 100 little-endian move ids right after a run
/// of bytes that appears once in <c>code.bin</c> — UPR ZX's <c>Gen7Constants.tmDataPrefixSM</c>/<c>USUM</c>.
/// </summary>
public static class GameMachines
{
    public const int Count = 100;

    private static readonly byte[] PrefixSM = Convert.FromHexString("034003410342034303");
    private static readonly byte[] PrefixUSUM = Convert.FromHexString("03BC03BD03BE03BF03");

    /// <summary>
    /// The item of TM <paramref name="tm"/> (0-based): TM01-92 are items 328-419, TM93-95 618-620 and TM96-100 690-694
    /// (UPR ZX's three blocks, the same in every 3DS game).
    /// </summary>
    public static int Item(int tm) => tm switch
    {
        < 92 => 328 + tm,
        < 95 => 618 + tm - 92,
        _ => 690 + tm - 95,
    };

    /// <summary>The 100 moves of the TMs, or null when the game is not Generation 7 or the table is not found once.</summary>
    public static int[]? Read(byte[] code, GameTitle title)
    {
        if (title.Generation() != 7)
            return null;
        byte[] prefix = title.Family() == GameFamily.USUM ? PrefixUSUM : PrefixSM;
        int at = Find(code, prefix);
        if (at < 0 || Find(code, prefix, at + 1) >= 0)
            return null;
        int start = at + prefix.Length;
        if (start + Count * 2 > code.Length)
            return null;
        return [.. Enumerable.Range(0, Count).Select(i => (int)BitConverter.ToUInt16(code, start + i * 2))];
    }

    private static int Find(byte[] data, byte[] pattern, int from = 0)
    {
        for (int i = from; i <= data.Length - pattern.Length; i++)
        {
            int j = 0;
            while (j < pattern.Length && data[i + j] == pattern[j])
                j++;
            if (j == pattern.Length)
                return i;
        }
        return -1;
    }
}
