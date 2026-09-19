using System.Globalization;
using Pokemanager.App.Resources;
using Pokemanager.Model.Data;

namespace Pokemanager.App.Services;

/// <summary>
/// The part a trainer plays, in words: "Gym leader 3", "Elite Four 1", "Rival 5", "Guzma". A ROM with randomized
/// trainer and class names is otherwise a list of strangers — this is what still says which one is the rival.
/// </summary>
public static class TrainerRoleNames
{
    /// <summary>The label of a tag from <see cref="TrainerRoles"/>, or null when the trainer is an ordinary one.</summary>
    public static string? Of(string? tag, bool gen7 = false)
    {
        if (string.IsNullOrEmpty(tag))
            return null;
        if (tag.StartsWith("THEMED:", StringComparison.Ordinal))
        {
            // "THEMED:LYSANDRE-LEADER" is the character, and the game already tells you who they are.
            string name = tag["THEMED:".Length..].Replace("-LEADER", "", StringComparison.Ordinal);
            if (name.Length == 0)
                return null;
            // The games translate these people (Hau is Tilo in Spanish, Lusamine is Samina): the name the player reads
            // is the one that helps. The ones with no translation of their own keep the international name.
            return Strings.ResourceManager.GetString("Character_" + name, Strings.Culture) is { Length: > 0 } translated
                ? translated
                : Capitalize(name);
        }
        // "RIVAL5-2" is the same fight with another starter: the number of the fight is what matters.
        string family = tag.Split('-')[0];
        bool leader = tag.EndsWith("-LEADER", StringComparison.Ordinal);
        int? number = Number(family);
        return family switch
        {
            _ when family.StartsWith("GYM", StringComparison.Ordinal) =>
                Format(leader ? Strings.Role_GymLeader : Strings.Role_Gym, number),
            _ when family.StartsWith("ELITE", StringComparison.Ordinal) => Format(Strings.Role_Elite, number),
            "CHAMPION" => Strings.Role_Champion,
            "UBER" => Strings.Role_Uber,
            _ when family.StartsWith("RIVAL", StringComparison.Ordinal) => Format(Strings.Role_Rival, number),
            // In Sun/Moon and Ultra Sun/Ultra Moon the friend who keeps challenging you is Hau, and the player knows him
            // by name, not as "friend 7".
            _ when family.StartsWith("FRIEND", StringComparison.Ordinal) =>
                Format(gen7 ? Strings.Role_Hau : Strings.Role_Friend, number),
            "STRONG" => Strings.Role_Strong,
            _ => null,
        };
    }

    /// <summary>What a whole difficulty is called.</summary>
    public static string Of(TrainerDifficulty difficulty) => difficulty switch
    {
        TrainerDifficulty.Boss => Strings.Role_Bosses,
        TrainerDifficulty.Important => Strings.Role_Important,
        _ => Strings.Role_Ordinary,
    };

    private static string Format(string label, int? number) =>
        number is { } n ? string.Format(CultureInfo.CurrentCulture, label, n) : label.Replace(" {0}", "", StringComparison.Ordinal);

    private static int? Number(string family) =>
        int.TryParse(new string([.. family.Where(char.IsDigit)]), out int n) ? n : null;

    private static string Capitalize(string name) =>
        name.Length == 0 ? name : char.ToUpper(name[0], CultureInfo.CurrentCulture) + name[1..].ToLower(CultureInfo.CurrentCulture);
}
