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
