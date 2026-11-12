namespace Pokemanager.Model.Data;

/// <summary>
/// Small changes to the game's executable that are not a table anyone can read: they are found by the bytes around
/// them, never by an address, and nothing is written unless what is there is exactly what is expected.
/// </summary>
public static class GameCode
{
    /// <summary>
    /// End of the routine that decides whether a Pokémon comes out shiny. **Measured in three real games** — Pokémon X
    /// (0x4F3C2), the user's Ultra Moon (0x2205C6) and their Alpha Sapphire (0x4EC66) — where it appears exactly once
    /// and is followed, nine bytes in, by the conditional branch below.
    /// </summary>
    private static readonly byte[] ShinySignature = [0x21, 0xE2, 0x03, 0x20, 0x92, 0xE1, 0x1C];

    /// <summary>How far the branch's condition byte sits from the start of the signature.</summary>
    private const int ShinyBranch = 9;

    /// <summary>ARM condition of the branch as the game ships it: <c>BEQ</c>, taken only when the check passed.</summary>
    private const byte BranchIfEqual = 0x0A;

    /// <summary>The same branch with no condition: <c>B</c>, so every Pokémon takes the shiny path.</summary>
    private const byte BranchAlways = 0xEA;

    /// <summary>
    /// Makes every Pokémon that is not shiny-locked come out shiny, by taking the condition off that branch (the
    /// change the 3DS community's code.bin edit makes). Nothing is written when the signature is not there exactly
    /// once, or when the byte is not the branch this expects.
    /// </summary>
    /// <param name="code">The decompressed executable, changed in place.</param>
    /// <returns>Whether the game was changed.</returns>
    public static bool MakeEveryPokemonShiny(byte[] code) => SetShinyBranch(code, BranchAlways);

    /// <summary>Whether the executable has the shiny branch this application knows how to change.</summary>
    public static bool HasShinyBranch(byte[] code) => FindShiny(code) is not null;

    /// <summary>Whether it has already been changed (the branch has lost its condition).</summary>
    public static bool IsEveryPokemonShiny(byte[] code) =>
        FindShiny(code) is { } at && code[at + ShinyBranch] == BranchAlways;

    /// <summary>
    /// Where the routine reads how many times to roll a Pokémon's identity looking for a shiny one — 1, or 3 with the
    /// Shiny Charm. **Measured in Ultra Sun and the USUM Expansion's executable**: two <c>LDRB R0, [R4, #0x23]</c>
    /// (<c>E5D40023</c>), the loop's start and its test, 0x1E and 0xA2 bytes after the shiny signature.
    /// </summary>
    private static readonly int[] RollReads = [0x1E, 0xA2];

    private const uint LoadRollCount = 0xE5D40023;

    /// <summary><c>MOV R0, #imm</c>: the immediate is 8 bits rotated right by an even amount (bits 0-11).</summary>
    private const uint MoveR0 = 0xE3A00000;

    /// <summary>
    /// The most rolls written. The loop is the game's (disassembled in the Expansion's executable: up to that many
    /// times, a new identity and the game's own shiny test, out at the first shiny), so more rolls only cost a few more
    /// random numbers at an encounter; the shiny rule itself — the IDs' xor under 16 — is left alone, so the game, the
    /// save and PKHeX all agree on which Pokémon are shiny.
    /// </summary>
    public const int MostRolls = 16384;

    /// <summary>The game's own odds.</summary>
    public const int GameOdds = 4096;

    /// <summary>
    /// How many rolls give odds of about one in <paramref name="odds"/>: 1 − (4095/4096)^rolls = 1/odds, rounded to the
    /// nearest count one <c>MOV</c> can hold (2026-11-12, user: "1 in 2 and no shinies" — 255 was the cap then, about 1
    /// in 16; 1 in 2 is 2832 rolls now).
    /// </summary>
    public static int RollsFor(int odds)
    {
        if (odds <= 1)
            return MostRolls;
        double exact = Math.Log(1 - 1.0 / odds) / Math.Log((GameOdds - 1) / (double)GameOdds);
        return Encodable().MinBy(n => Math.Abs(n - exact));
    }

    /// <summary>Every roll count from 1 to <see cref="MostRolls"/> that one <c>MOV</c> can hold.</summary>
    private static IEnumerable<int> Encodable() =>
        Enumerable.Range(0, 16).SelectMany(rot => Enumerable.Range(1, 255).Select(imm => (long)imm << ((32 - 2 * rot) % 32)))
            .Where(n => n is >= 1 and <= MostRolls).Select(n => (int)n).Distinct().Order();

    /// <summary>The <c>MOV R0, #n</c> instruction for a count <see cref="Encodable"/> lists.</summary>
    private static uint MoveR0Of(int n)
    {
        for (int rot = 0; rot < 16; rot++)
        {
            uint value = (uint)n;
            uint imm = (value << (2 * rot)) | (rot == 0 ? 0 : value >> (32 - 2 * rot));
            if (imm <= 0xFF)
                return MoveR0 | (uint)(rot << 8) | imm;
        }
        throw new ArgumentOutOfRangeException(nameof(n), n, "Not a MOV immediate.");
    }

    /// <summary>The odds those rolls really give: one in 1 / (1 − (4095/4096)^rolls).</summary>
    public static int OddsFor(int rolls) => (int)Math.Round(1 / (1 - Math.Pow((GameOdds - 1) / (double)GameOdds, rolls)));

    /// <summary>Whether this executable has the two reads where the number of rolls can be written.</summary>
    public static bool CanSetShinyRolls(byte[] code) =>
        FindShiny(code) is { } at && RollReads.All(d => at + d + 4 <= code.Length
            && (BitConverter.ToUInt32(code, at + d) == LoadRollCount || (BitConverter.ToUInt32(code, at + d) & 0xFFFFF000) == MoveR0));

    /// <summary>
    /// Makes the game roll a Pokémon's identity <paramref name="rolls"/> times (the nearest count a <c>MOV</c> holds)
    /// looking for a shiny one, instead of 1 (3 with the Shiny Charm, which this then overrides). Nothing is written
    /// unless both reads are where they were measured.
    /// </summary>
    /// <returns>Whether the game was changed.</returns>
    public static bool SetShinyRolls(byte[] code, int rolls)
    {
        if (!CanSetShinyRolls(code) || FindShiny(code) is not { } at)
            return false;
        int count = Encodable().MinBy(n => Math.Abs(n - Math.Clamp(rolls, 1, MostRolls)));
        uint instruction = MoveR0Of(count);
        bool changed = false;
        foreach (int d in RollReads)
        {
            if (BitConverter.ToUInt32(code, at + d) == instruction)
                continue;
            BitConverter.TryWriteBytes(code.AsSpan(at + d, 4), instruction);
            changed = true;
        }
        return changed;
    }

    private static bool SetShinyBranch(byte[] code, byte condition)
    {
        if (FindShiny(code) is not { } at)
            return false;
        if (code[at + ShinyBranch] == condition)
            return false;
        code[at + ShinyBranch] = condition;
        return true;
    }

    /// <summary>Where the signature is, or null when it is missing or appears more than once (then it is not it).</summary>
    private static int? FindShiny(byte[] code)
    {
        int found = -1;
        for (int i = 0; i + ShinySignature.Length + ShinyBranch < code.Length; i++)
        {
            int j = 0;
            while (j < ShinySignature.Length && code[i + j] == ShinySignature[j])
                j++;
            if (j < ShinySignature.Length)
                continue;
            if (found >= 0)
                return null;
            found = i;
        }
        if (found < 0)
            return null;
        byte branch = code[found + ShinyBranch];
        return branch is BranchIfEqual or BranchAlways ? found : null;
    }
}
