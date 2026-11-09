namespace Pokemanager.Model.Data;

/// <summary>
/// The Pokémon the randomizer treats as legendary, as one list for this side too: UPR ZX's own (#144-#807) and, for a
/// species-expansion mod, what the patched <c>Pokemon</c> class adds (Generation 8-9 legendaries and mythicals, the
/// Paradox Pokémon and the Ultra Beasts — 2026-11-09, user). Kept in step with <c>upr/patch/Pokemon.java</c>.
/// </summary>
public static class Legendaries
{
    private static readonly HashSet<int> All =
    [
        144, 145, 146, 150, 151, 243, 244, 245, 249, 250, 251, 377, 378, 379, 380, 381, 382, 383, 384, 385, 386,
        480, 481, 482, 483, 484, 485, 486, 487, 488, 489, 490, 491, 492, 493, 494, 638, 639, 640, 641, 642, 643, 644,
        645, 646, 647, 648, 649, 716, 717, 718, 719, 720, 721, 772, 773, 785, 786, 787, 788, 789, 790, 791, 792, 800,
        801, 802, 807,
        // Ultra Beasts
        793, 794, 795, 796, 797, 798, 799, 803, 804, 805, 806,
        // Generation 8
        808, 809, 888, 889, 890, 891, 892, 893, 894, 895, 896, 897, 898, 905,
        // Generation 9
        1001, 1002, 1003, 1004, 1007, 1008, 1014, 1015, 1016, 1017, 1024, 1025,
        // Paradox Pokémon
        984, 985, 986, 987, 988, 989, 990, 991, 992, 993, 994, 995, 1005, 1006, 1009, 1010, 1020, 1021, 1022, 1023,
    ];

    public static bool Contains(int species) => All.Contains(species);
}
