using Pokemanager.Model.Data;
using Pokemanager.Model.Edits;
using Pokemanager.Model.Editing;
using Pokemanager.Model.Dump;

namespace Pokemanager.Model.Difficulty;

/// <summary>How hard the adventure is meant to be, as one choice made when the run is set up.</summary>
public enum DifficultyLevel
{
    /// <summary>For a first run of a randomized game: the trainers stop thinking and their Pokémon stop rolling well.</summary>
    Relaxed,

    /// <summary>The game's own values, which is also what puts a profile back.</summary>
    Normal,

    /// <summary>Every trainer plays properly and the bosses are built to win.</summary>
    Challenge,

    /// <summary>Everything the games can be told to do, which is not a fair fight and is not meant to be.</summary>
    Nightmare,
}

/// <summary>
/// What a profile does to one kind of trainer (<see cref="TrainerDifficulty"/>). **A null leaves the ROM's own value**,
/// which is how <see cref="DifficultyLevel.Normal"/> is simply every field back as the game shipped it.
/// </summary>
/// <param name="Ai">Bits 0-2 of the AI byte: 0x00, 0x01, 0x03, 0x05 or 0x07, the five the games actually use.</param>
/// <param name="Ivs">0-31, written as the generation stores it (<see cref="TrainerIvs"/>).</param>
/// <param name="BagItems">Healing items the trainer's bag carries, 0-4; 0 empties it.</param>
/// <param name="MegaFromLevel">The last team member carries its own Mega Stone from this level on.</param>
public sealed record DifficultyRule(int? Ai = null, int? Ivs = null, int? BagItems = null, int? MegaFromLevel = null);

/// <param name="TrainerLevelPercent">
/// What the build adds to every trainer level (<see cref="Projects.GameSettings.TrainerLevelPercent"/>). It is one
/// value for the whole game and not an edit per trainer, so applying a profile twice cannot pile up.
/// </param>
public sealed record DifficultyProfile(
    DifficultyLevel Level, double TrainerLevelPercent, DifficultyRule Regular, DifficultyRule Important, DifficultyRule Boss)
{
    public DifficultyRule For(TrainerDifficulty kind) => kind switch
    {
        TrainerDifficulty.Boss => Boss,
        TrainerDifficulty.Important => Important,
        _ => Regular,
    };
}

/// <summary>
/// What applying a profile did: how many values it wrote, how many it put back to what the ROM says (a profile owns
/// its fields, so choosing another one — or Normal — undoes the first), and how many trainers were touched at all.
/// </summary>
public sealed record DifficultyResult(int Trainers, int Values, int PutBack)
{
    public bool DidNothing => Values == 0 && PutBack == 0;
}

/// <summary>
/// The difficulty profiles (model A of <c>docs/game-ai.md</c>): one choice that writes what a player would otherwise
/// have to set trainer by trainer. The trainers are told apart by <see cref="TrainerRoles"/> — UPR ZX's own tag lists,
/// so it works on a ROM with every trainer name randomized — because the same rule must not hit a Youngster and the
/// Champion alike.
/// </summary>
/// <remarks>
/// Applying a profile is **an ordinary batch of edits**, like the sweeps of Advanced: Game, so it shows in the trainers
/// tab, it can be undone with Ctrl+Z and it is put back with "Put all back". It always starts from the ROM's own values
/// (<see cref="OwnedFields"/> are reverted first), so switching from one profile to another is not the two piled up,
/// and choosing <see cref="DifficultyLevel.Normal"/> is what takes a profile off.
/// </remarks>
public static class DifficultyProfiles
{
    private const string T = GameTables.Trainers;

    /// <summary>
    /// What a boss's bag is filled with. Verified on the real Pokémon X dump: item 23 is the Full Restore, and the
    /// ids of the healing items are the same in every Generation 6 and 7 game.
    /// </summary>
    public const int FullRestore = 23;

    /// <summary>
    /// The five values of the AI byte the games themselves use. A profile writes one of these into bits 0-2 and
    /// **keeps the rest of the byte** (a rival at 0x87 set to Basic becomes 0x81).
    /// </summary>
    public const int AiNone = 0x00, AiBasic = 0x01, AiStrong = 0x03, AiExpert = 0x07;

    /// <summary>
    /// Expert plus bit 7, what the games give their hardest trainers (Ultra Moon: 18 of 32 bosses and 17 important
    /// trainers ship with 0x87). What bit 7 does is not documented; the user chose it for Challenge and Nightmare
    /// (2026-11-10). The harder profiles **only add** bits: Challenge set ordinary trainers to 0x03 and so took the
    /// Expert bit from the 150 the game ships at 0x05/0x07 — they attacked at random (user: "hacen ataques aleatorios").
    /// </summary>
    public const int AiTop = 0x87;

    public static readonly IReadOnlyList<DifficultyProfile> All =
    [
        // The bosses keep a little of their bite: with AI 0x00 and no IVs a gym leader stops being a fight at all.
        new(DifficultyLevel.Relaxed, TrainerLevelPercent: -10,
            Regular: new(Ai: AiNone, Ivs: 0, BagItems: 0),
            Important: new(Ai: AiBasic, Ivs: 1, BagItems: 0),
            Boss: new(Ai: AiBasic, Ivs: 6, BagItems: 1)),

        new(DifficultyLevel.Normal, TrainerLevelPercent: 0, new(), new(), new()),

        new(DifficultyLevel.Challenge, TrainerLevelPercent: 5,
            Regular: new(Ai: AiTop, Ivs: 12),
            Important: new(Ai: AiTop, Ivs: 18),
            Boss: new(Ai: AiTop, Ivs: 25, BagItems: 2, MegaFromLevel: 30)),

        // The AI is already at the game's top in Challenge, so above it the levers left are the team and the bag.
        new(DifficultyLevel.Nightmare, TrainerLevelPercent: 15,
            Regular: new(Ai: AiTop, Ivs: 18),
            Important: new(Ai: AiTop, Ivs: 31, BagItems: 1),
            Boss: new(Ai: AiTop, Ivs: 31, BagItems: 4, MegaFromLevel: 1)),
    ];

    public static DifficultyProfile Of(DifficultyLevel level) => All.First(p => p.Level == level);

    /// <summary>
    /// The trainer level percentage after going from profile <paramref name="from"/> (null = Normal) to
    /// <paramref name="to"/>: <paramref name="current"/> moved by the difference between the two profiles' own
    /// percentages, so a value the player set survives a change and a round trip.
    /// </summary>
    public static double LevelPercentAfter(double current, DifficultyLevel? from, DifficultyLevel to) =>
        current + Of(to).TrainerLevelPercent - Of(from ?? DifficultyLevel.Normal).TrainerLevelPercent;

    /// <summary>The level from which the profile gives megas (the lowest of its rules), or null when it gives none.</summary>
    public static int? MegaFromLevel(DifficultyProfile profile) =>
        new[] { profile.Regular, profile.Important, profile.Boss }.Select(r => r.MegaFromLevel).Where(l => l is not null).Min();

    /// <summary>
    /// The fields a profile owns: they are put back to what the ROM says before it writes, so a profile applied over
    /// another is that profile and not the two together. Everything else a trainer has — its team, its moves, its
    /// money — is left exactly as it is, hand edits included.
    /// </summary>
    public static IEnumerable<string> OwnedFields(int generation)
    {
        yield return "ai";
        yield return "heldItems";
        for (int i = 1; i <= 4; i++)
            yield return $"item{i}";
        for (int slot = 1; slot <= 6; slot++)
        {
            yield return $"p{slot}.item";
            if (generation == 6)
                yield return $"p{slot}.ivs";
            else
                foreach (string stat in Stats)
                    yield return $"p{slot}.iv{stat}";
        }
    }

    private static readonly string[] Stats = ["Hp", "Atk", "Def", "Spa", "Spd", "Spe"];

    /// <summary>
    /// Writes the profile over every trainer of the game. The caller wraps it in one undo step; nothing is written for
    /// a value that already says what the profile wants, so the count is what actually moved.
    /// </summary>
    /// <param name="megaFromLevel">Overrides the level from which the profile's rules give megas; null keeps theirs.</param>
    public static DifficultyResult Apply(EditorSession session, DifficultyLevel level, int? megaFromLevel = null)
    {
        var profile = Of(level);
        var title = session.Current.Title;
        int generation = title.Generation();
        var stones = session.Current.MegaStoneBySpecies;
        string[] owned = [.. OwnedFields(generation)];

        int trainers = 0, values = 0, putBack = 0;
        for (int id = 0; id < session.Current.Trainers.Length; id++)
        {
            // A record this application could not parse is passed through to the build untouched.
            if (session.Current.Trainers[id].Unreadable)
                continue;

            // Back to the ROM's own values first, so a profile over another is that profile and not the two piled up.
            int restored = 0;
            foreach (string field in owned)
            {
                if (!session.IsModified(T, id, field))
                    continue;
                session.Set(T, id, field, session.GetOriginal(T, id, field));
                restored++;
            }

            int changed = 0;
            var rule = profile.For(TrainerRoles.DifficultyOf(title, id));
            if (rule.Ai is { } ai)
            {
                int current = session.GetInt(T, id, "ai");
                // Easier profiles replace bits 0-2 and keep the rest; harder ones only add (never below the ROM's).
                changed += Set(session, id, "ai", level > DifficultyLevel.Normal ? current | ai : (current & ~0x07) | ai);
            }
            if (rule.Ivs is { } ivs)
                changed += SetTeamIvs(session, id, generation, ivs);
            if (rule.BagItems is { } bag)
            {
                for (int i = 1; i <= 4; i++)
                    changed += Set(session, id, $"item{i}", i <= bag ? FullRestore : 0);
            }
            if ((rule.MegaFromLevel is null ? null : megaFromLevel ?? rule.MegaFromLevel) is { } from && stones.Count > 0)
                changed += SetMegaStone(session, id, generation, from, stones);

            values += changed;
            putBack += restored;
            if (changed > 0 || restored > 0)
                trainers++;
        }
        return new DifficultyResult(trainers, values, putBack);
    }

    /// <param name="ivs">0-31; Generation 6 stores the byte it converts to, Generation 7 each of the six.</param>
    private static int SetTeamIvs(EditorSession session, int id, int generation, int ivs)
    {
        int changed = 0;
        for (int slot = 1; slot <= 6; slot++)
        {
            if (session.GetInt(T, id, $"p{slot}.species") <= 0 && session.GetInt(T, id, $"p{slot}.level") <= 0)
                continue;
            if (generation == 6)
            {
                changed += Set(session, id, $"p{slot}.ivs", TrainerIvs.ToByte(ivs));
                continue;
            }
            foreach (string stat in Stats)
                changed += Set(session, id, $"p{slot}.iv{stat}", ivs);
        }
        return changed;
    }

    /// <summary>
    /// The last member carries its own Mega Stone when the team reaches <paramref name="from"/>. A species with no mega
    /// evolution is left alone, and Generation 6 also needs the record's "uses held items" flag, without which the game
    /// ignores what the entry carries.
    /// </summary>
    private static int SetMegaStone(EditorSession session, int id, int generation, int from, IReadOnlyDictionary<int, int> stones)
    {
        int last = 0, highest = 0;
        for (int slot = 1; slot <= 6; slot++)
        {
            if (session.GetInt(T, id, $"p{slot}.species") <= 0)
                continue;
            last = slot;
            highest = Math.Max(highest, session.GetInt(T, id, $"p{slot}.level"));
        }
        if (last == 0 || highest < from || !stones.TryGetValue(session.GetInt(T, id, $"p{last}.species"), out int stone))
            return 0;

        int changed = Set(session, id, $"p{last}.item", stone);
        if (generation == 6 && changed > 0)
            changed += Set(session, id, "heldItems", 1);
        return changed;
    }

    private static int Set(EditorSession session, int id, string field, int value)
    {
        if (session.GetInt(T, id, field) == value)
            return 0;
        session.SetInt(T, id, field, value);
        return 1;
    }
}
