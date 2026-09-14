namespace Pokemanager.Battle;

/// <summary>Helpers for Showdown's battle protocol.</summary>
public static class ShowdownProtocol
{
    /// <summary>
    /// What one player (or a spectator, <paramref name="side"/> null) may see of an update. Showdown writes
    /// <c>|split|p1</c> followed by the exact line for that side (HP numbers) and the public one (HP percentage).
    /// </summary>
    public static List<string> ForSide(IReadOnlyList<string> lines, string? side)
    {
        var result = new List<string>(lines.Count);
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].StartsWith("|split|", StringComparison.Ordinal) && i + 2 < lines.Count)
            {
                result.Add(lines[i][7..] == side ? lines[i + 1] : lines[i + 2]);
                i += 2;
            }
            else
            {
                result.Add(lines[i]);
            }
        }
        return result;
    }
}
