using Pokemanager.Save;

namespace Pokemanager.Multiplayer;

/// <summary>
/// One Pokémon as other players see it. Ids (species, item, ability, moves, types) are the same in every game, so the
/// viewer names them in their own language. Types and stats come from the ROM the owner plays (randomized).
/// </summary>
/// <param name="Hp">Current HP (party only; boxed Pokémon are always healed).</param>
/// <param name="Stats">HP, Atk, Def, Spe, SpA, SpD (the order of <see cref="SaveDocument.Stats"/>).</param>
/// <param name="MoveTypes">Type of each move in the owner's ROM (-1 for an empty slot).</param>
/// <param name="MovePp">Current and maximum PP of each move: current1, max1, current2, max2…</param>
/// <param name="MoveCategories">Category of each move in the owner's ROM (0 status, 1 physical, 2 special; -1 empty). Since 1.2.1.</param>
/// <param name="Ball">Poké Ball the Pokémon was caught in (0 unknown). Since 1.2.1.</param>
/// <param name="BaseStats">Base stats in the owner's ROM, in the order of <paramref name="Stats"/>. Since 1.2.2.</param>
/// <param name="Ivs">IVs in the order of <paramref name="Stats"/>. Since 1.2.2.</param>
/// <param name="Evs">EVs in the order of <paramref name="Stats"/>. Since 1.2.2.</param>
public sealed record SharedPokemon(
    ushort Species, byte Form, byte Gender, bool IsShiny, bool IsEgg, string Nickname, int Level,
    int HeldItem, int Ability, int Nature, ushort[] Moves, int[] Stats, int[] Types, int Hp,
    int[]? MoveTypes = null, int[]? MovePp = null, int[]? MoveCategories = null, int Ball = 0, int[]? BaseStats = null, int[]? Ivs = null, int[]? Evs = null);

/// <param name="Slots">Every slot of the box in order; null where it is empty.</param>
/// <param name="Wallpaper">The box's wallpaper number in the game (-1 unknown). Since 1.2.1.</param>
public sealed record SharedBox(string Name, List<SharedPokemon?> Slots, int Wallpaper = -1);

/// <summary>What a player shares about their save: trainer summary, party and boxes. Serializable.</summary>
/// <param name="Game">PKHeX version code of the save (X, Y, OR, AS, SN, MN, US, UM).</param>
/// <param name="Boxes">Null when the room does not share boxes.</param>
/// <param name="Party">Null when the room does not share the party.</param>
public sealed record TrainerSnapshot(
    string Trainer, string Game, int Generation, int MilestoneCount, List<bool> Milestones, uint Money,
    int PlayedHours, int PlayedMinutes, List<SharedPokemon>? Party, List<SharedBox>? Boxes, DateTime SavedAt)
{
    public int MilestonesEarned => Milestones.Count(m => m);

    /// <summary>Reads the save (it is never modified).</summary>
    public static TrainerSnapshot Read(SaveDocument save)
    {
        var party = new List<SharedPokemon>();
        for (int i = 0; i < save.PartyCount; i++)
        {
            if (Share(save, new SaveSlot(null, i)) is { } pk)
                party.Add(pk);
        }
        var boxes = new List<SharedBox>();
        for (int box = 0; box < save.BoxCount; box++)
        {
            var slots = new List<SharedPokemon?>();
            for (int slot = 0; slot < save.BoxSlotCount; slot++)
                slots.Add(Share(save, new SaveSlot(box, slot)));
            boxes.Add(new SharedBox(save.BoxName(box), slots, save.BoxWallpaper(box)));
        }
        var milestones = Enumerable.Range(0, save.MilestoneCount).Select(save.GetMilestone).ToList();
        return new TrainerSnapshot(save.TrainerName, save.Version.ToString(), save.Generation, save.MilestoneCount, milestones,
            save.Money, save.PlayedHours, save.PlayedMinutes, party, boxes, save.LastSaved ?? File.GetLastWriteTime(save.SavePath));
    }

    private static SharedPokemon? Share(SaveDocument save, SaveSlot slot)
    {
        var pk = save.Get(slot);
        if (pk.Species == 0)
            return null;
        var personal = save.Personal(pk.Species, pk.Form);
        var types = personal?.Types ?? [];
        int[]? baseStats = personal is null ? null : [personal.HP, personal.ATK, personal.DEF, personal.SPE, personal.SPA, personal.SPD];
        ushort[] moves = pk.Moves.ToArray();
        int[] ppUps = [pk.Move1_PPUps, pk.Move2_PPUps, pk.Move3_PPUps, pk.Move4_PPUps];
        int[] pp = [pk.Move1_PP, pk.Move2_PP, pk.Move3_PP, pk.Move4_PP];
        var moveTypes = moves.Select(m => m != 0 && m < save.Rom.Moves.Length ? save.Rom.Moves[m].Type : -1).ToArray();
        var movePp = moves.SelectMany((m, i) => new[] { pp[i], save.MaxPP(m, ppUps[i]) }).ToArray();
        var moveCategories = moves.Select(m => m != 0 && m < save.Rom.Moves.Length ? save.Rom.Moves[m].Category : -1).ToArray();
        return new SharedPokemon(
            pk.Species, pk.Form, pk.Gender, pk.IsShiny, pk.IsEgg, pk.Nickname, save.Level(pk), pk.HeldItem, pk.Ability,
            (int)pk.Nature, moves, save.Stats(pk), types.Length >= 2 ? [types[0], types[1]] : [.. types],
            slot.IsParty ? pk.Stat_HPCurrent : -1, moveTypes, movePp, moveCategories, pk.Ball, baseStats,
            [pk.IV_HP, pk.IV_ATK, pk.IV_DEF, pk.IV_SPE, pk.IV_SPA, pk.IV_SPD], [pk.EV_HP, pk.EV_ATK, pk.EV_DEF, pk.EV_SPE, pk.EV_SPA, pk.EV_SPD]);
    }

    /// <summary>Only what the room's rules let other players see.</summary>
    public TrainerSnapshot FilteredBy(RoomRules rules) =>
        this with { Party = rules.ShareParty ? Party : null, Boxes = rules.ShareBoxes ? Boxes : null };
}

/// <summary>
/// How a player presents themselves in rooms (the Pokemanager profile).
/// </summary>
/// <param name="Sprite">Trainer sprite id (Pokémon Showdown's names, e.g. "red"), or null.</param>
/// <param name="Image">A custom picture instead: a small PNG (at most <see cref="MaxImageBytes"/>).</param>
/// <param name="Color">Accent color, "#RRGGBB".</param>
public sealed record PlayerProfile(string Name, string? Sprite = null, byte[]? Image = null, string Color = "#1C3A70")
{
    public const int MaxImageBytes = 64 * 1024;

    /// <summary>What can travel: an oversized picture is dropped (the sprite or initials are shown instead).</summary>
    public PlayerProfile Sanitized() => this with
    {
        Name = Name.Trim().Length == 0 ? "?" : Name.Trim()[..Math.Min(Name.Trim().Length, 24)],
        Image = Image is { Length: > 0 and <= MaxImageBytes } ? Image : null,
        Color = System.Text.RegularExpressions.Regex.IsMatch(Color, "^#[0-9A-Fa-f]{6}$") ? Color : "#1C3A70",
    };
}

/// <summary>What players can see of each other. Chosen by the host.</summary>
public sealed record RoomRules(bool ShareParty = true, bool ShareBoxes = true);
