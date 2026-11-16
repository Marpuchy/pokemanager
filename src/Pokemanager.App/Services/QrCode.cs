using System.Text;

namespace Pokemanager.App.Services;

/// <summary>
/// A QR code for a short text (the Pokédex link for the phone, 2026-11-16): byte mode, error correction level M,
/// versions 1 to 9 (up to 180 bytes). Written after the standard (ISO/IEC 18004) the way Project Nayuki's reference
/// encoder does it: Reed-Solomon over GF(256) with 0x11D, the codewords interleaved by block, the function patterns,
/// the zigzag placement and the mask with the lowest penalty. No package needed.
/// </summary>
public static class QrCode
{
    // Level M, versions 1-9: error correction codewords per block, blocks, total codewords.
    private static readonly int[] EccPerBlock = [0, 10, 16, 26, 18, 24, 16, 18, 22, 22];
    private static readonly int[] Blocks = [0, 1, 1, 1, 2, 2, 4, 4, 4, 5];
    private static readonly int[] Total = [0, 26, 44, 70, 100, 134, 172, 196, 242, 292];
    private static readonly int[][] Alignment =
    [
        [], [], [6, 18], [6, 22], [6, 26], [6, 30], [6, 34], [6, 22, 38], [6, 24, 42], [6, 26, 46],
    ];

    /// <summary>The modules, <c>[row, column]</c>, true for dark; null when the text is too long.</summary>
    public static bool[,]? Encode(string text)
    {
        byte[] data = Encoding.UTF8.GetBytes(text);
        int version = 0;
        for (int v = 1; v <= 9; v++)
            if ((4 + 8 + 8 * data.Length + 7) / 8 <= Total[v] - EccPerBlock[v] * Blocks[v])
            {
                version = v;
                break;
            }
        if (version == 0)
            return null;

        // The data bits: mode, count, bytes, terminator, padding.
        int capacity = Total[version] - EccPerBlock[version] * Blocks[version];
        var bits = new List<bool>();
        void Add(int value, int count) { for (int i = count - 1; i >= 0; i--) bits.Add(((value >> i) & 1) != 0); }
        Add(0b0100, 4);
        Add(data.Length, 8);
        foreach (byte b in data)
            Add(b, 8);
        Add(0, Math.Min(4, capacity * 8 - bits.Count));
        Add(0, (8 - bits.Count % 8) % 8);
        for (int pad = 0xEC; bits.Count < capacity * 8; pad ^= 0xEC ^ 0x11)
            Add(pad, 8);
        var codewords = new byte[capacity];
        for (int i = 0; i < bits.Count; i++)
            if (bits[i])
                codewords[i >> 3] |= (byte)(1 << (7 - (i & 7)));

        byte[] all = Interleave(codewords, version);
        return Draw(all, version);
    }

    private static byte[] Interleave(byte[] data, int version)
    {
        int blocks = Blocks[version], ecc = EccPerBlock[version], raw = Total[version];
        int shortBlocks = blocks - raw % blocks, shortLength = raw / blocks;
        byte[] divisor = Divisor(ecc);
        var parts = new List<byte[]>();
        for (int i = 0, k = 0; i < blocks; i++)
        {
            int length = shortLength - ecc + (i < shortBlocks ? 0 : 1);
            byte[] block = data[k..(k + length)];
            k += length;
            byte[] remainder = Remainder(block, divisor);
            var full = new List<byte>(block);
            if (i < shortBlocks)
                full.Add(0); // a placeholder, so every block has the same length; skipped below
            full.AddRange(remainder);
            parts.Add([.. full]);
        }
        var result = new List<byte>(raw);
        for (int i = 0; i < parts[0].Length; i++)
            for (int j = 0; j < parts.Count; j++)
                if (i != shortLength - ecc || j >= shortBlocks)
                    result.Add(parts[j][i]);
        return [.. result];
    }

    private static byte Multiply(int x, int y)
    {
        int z = 0;
        for (int i = 7; i >= 0; i--)
        {
            z = (z << 1) ^ ((z >> 7) * 0x11D);
            z ^= ((y >> i) & 1) * x;
        }
        return (byte)z;
    }

    private static byte[] Divisor(int degree)
    {
        var result = new byte[degree];
        result[degree - 1] = 1;
        int root = 1;
        for (int i = 0; i < degree; i++)
        {
            for (int j = 0; j < degree; j++)
            {
                result[j] = Multiply(result[j], root);
                if (j + 1 < degree)
                    result[j] ^= result[j + 1];
            }
            root = Multiply(root, 0x02);
        }
        return result;
    }

    private static byte[] Remainder(byte[] data, byte[] divisor)
    {
        var result = new byte[divisor.Length];
        foreach (byte b in data)
        {
            int factor = b ^ result[0];
            Array.Copy(result, 1, result, 0, result.Length - 1);
            result[^1] = 0;
            for (int i = 0; i < result.Length; i++)
                result[i] ^= Multiply(divisor[i], factor);
        }
        return result;
    }

    private static bool[,] Draw(byte[] codewords, int version)
    {
        int size = 17 + 4 * version;
        var modules = new bool[size, size];
        var function = new bool[size, size];
        void Set(int x, int y, bool dark) { modules[y, x] = dark; function[y, x] = true; }

        for (int i = 0; i < size; i++)
        {
            Set(6, i, i % 2 == 0);
            Set(i, 6, i % 2 == 0);
        }
        foreach (var (cx, cy) in new[] { (3, 3), (size - 4, 3), (3, size - 4) })
            for (int dy = -4; dy <= 4; dy++)
                for (int dx = -4; dx <= 4; dx++)
                {
                    int x = cx + dx, y = cy + dy;
                    if (x < 0 || y < 0 || x >= size || y >= size)
                        continue;
                    int distance = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    Set(x, y, distance != 2 && distance != 4);
                }
        int[] positions = Alignment[version];
        for (int i = 0; i < positions.Length; i++)
            for (int j = 0; j < positions.Length; j++)
            {
                if ((i == 0 && j == 0) || (i == 0 && j == positions.Length - 1) || (i == positions.Length - 1 && j == 0))
                    continue;
                for (int dy = -2; dy <= 2; dy++)
                    for (int dx = -2; dx <= 2; dx++)
                        Set(positions[i] + dx, positions[j] + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
            }
        DrawFormat(0, size, Set);
        if (version >= 7)
        {
            int rem = version;
            for (int i = 0; i < 12; i++)
                rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
            int bits = (version << 12) | rem;
            for (int i = 0; i < 18; i++)
            {
                bool bit = ((bits >> i) & 1) != 0;
                int a = size - 11 + i % 3, b = i / 3;
                Set(a, b, bit);
                Set(b, a, bit);
            }
        }

        // The codewords, two columns at a time from the bottom right, up and down in turn.
        int index = 0;
        for (int right = size - 1; right >= 1; right -= 2)
        {
            if (right == 6)
                right = 5;
            for (int vertical = 0; vertical < size; vertical++)
                for (int j = 0; j < 2; j++)
                {
                    int x = right - j;
                    bool upward = ((right + 1) & 2) == 0;
                    int y = upward ? size - 1 - vertical : vertical;
                    if (!function[y, x] && index < codewords.Length * 8)
                    {
                        modules[y, x] = ((codewords[index >> 3] >> (7 - (index & 7))) & 1) != 0;
                        index++;
                    }
                }
        }

        bool[,]? best = null;
        int bestPenalty = int.MaxValue;
        for (int mask = 0; mask < 8; mask++)
        {
            var candidate = (bool[,])modules.Clone();
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    if (!function[y, x] && Masked(mask, x, y))
                        candidate[y, x] = !candidate[y, x];
            DrawFormat(mask, size, (x, y, dark) => candidate[y, x] = dark);
            int penalty = Penalty(candidate, size);
            if (penalty < bestPenalty)
            {
                bestPenalty = penalty;
                best = candidate;
            }
        }
        return best!;
    }

    private static bool Masked(int mask, int x, int y) => mask switch
    {
        0 => (x + y) % 2 == 0,
        1 => y % 2 == 0,
        2 => x % 3 == 0,
        3 => (x + y) % 3 == 0,
        4 => (x / 3 + y / 2) % 2 == 0,
        5 => x * y % 2 + x * y % 3 == 0,
        6 => (x * y % 2 + x * y % 3) % 2 == 0,
        _ => ((x + y) % 2 + x * y % 3) % 2 == 0,
    };

    /// <summary>Format information: level M (00) and the mask, with its BCH bits, in both copies.</summary>
    private static void DrawFormat(int mask, int size, Action<int, int, bool> set)
    {
        int data = mask; // level M = 00
        int rem = data;
        for (int i = 0; i < 10; i++)
            rem = (rem << 1) ^ ((rem >> 9) * 0x537);
        int bits = ((data << 10) | rem) ^ 0x5412;
        bool Bit(int i) => ((bits >> i) & 1) != 0;
        for (int i = 0; i <= 5; i++)
            set(8, i, Bit(i));
        set(8, 7, Bit(6));
        set(8, 8, Bit(7));
        set(7, 8, Bit(8));
        for (int i = 9; i < 15; i++)
            set(14 - i, 8, Bit(i));
        for (int i = 0; i < 8; i++)
            set(size - 1 - i, 8, Bit(i));
        for (int i = 8; i < 15; i++)
            set(8, size - 15 + i, Bit(i));
        set(8, size - 8, true);
    }

    /// <summary>Runs of five or more, 2×2 blocks and the dark balance (any mask gives a valid code; this picks a clean one).</summary>
    private static int Penalty(bool[,] m, int size)
    {
        int penalty = 0, dark = 0;
        for (int a = 0; a < size; a++)
        {
            int rowRun = 1, colRun = 1;
            for (int b = 1; b < size; b++)
            {
                if (m[a, b] == m[a, b - 1]) rowRun++;
                else { if (rowRun >= 5) penalty += rowRun - 2; rowRun = 1; }
                if (m[b, a] == m[b - 1, a]) colRun++;
                else { if (colRun >= 5) penalty += colRun - 2; colRun = 1; }
            }
            if (rowRun >= 5) penalty += rowRun - 2;
            if (colRun >= 5) penalty += colRun - 2;
        }
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                if (m[y, x]) dark++;
                if (x + 1 < size && y + 1 < size && m[y, x] == m[y, x + 1] && m[y, x] == m[y + 1, x] && m[y, x] == m[y + 1, x + 1])
                    penalty += 3;
            }
        int total = size * size;
        penalty += (Math.Abs(dark * 20 - total * 10) + total - 1) / total * 10;
        return penalty;
    }
}
