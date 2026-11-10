using pk3DS.Core;
using pk3DS.Core.CTR;
using Pokemanager.App.Resources;
using Pokemanager.Model.Data;
using Pokemanager.Model.Dump;

namespace Pokemanager.App.Services;

/// <summary>Display names: game texts in the project's language, and UI lists in the UI language.</summary>
public sealed class GameNames
{
    public IReadOnlyList<string> Species { get; }
    public IReadOnlyList<string> PersonalEntries { get; }
    public IReadOnlyList<string> Types { get; }
    public IReadOnlyList<string> Abilities { get; }
    public IReadOnlyList<string> Items { get; }
    public IReadOnlyList<string> Moves { get; }

    /// <summary>Trainer names, by trainer id; the game leaves a placeholder for the unnamed ones.</summary>
    public IReadOnlyList<string> TrainerNames { get; }

    /// <summary>Trainer class names ("Leader", "Youngster"), by class index.</summary>
    public IReadOnlyList<string> TrainerClasses { get; }

    /// <summary>Nature names in the game's index order (PKHeX's list, in the project's text language).</summary>
    public IReadOnlyList<string> Natures { get; }

    /// <summary>Items with "none" in slot 0, for the pickers where 0 means the trainer carries nothing.</summary>
    public IReadOnlyList<string> ItemChoices { get; }

    /// <summary>Moves with "none" in slot 0 (the game's own entry there is a row of dashes).</summary>
    public IReadOnlyList<string> MoveChoices { get; }

    /// <summary>The bag's description of each item, in the project's text language (empty when the game has none).</summary>
    public IReadOnlyList<string> ItemDescriptions { get; }

    /// <summary>Growth rates in the game's index order.</summary>
    public static IReadOnlyList<string> ExpGrowth =>
        [Strings.Growth_MediumFast, Strings.Growth_Erratic, Strings.Growth_Fluctuating, Strings.Growth_MediumSlow, Strings.Growth_Fast, Strings.Growth_Slow];

    /// <summary>Egg groups in the game's index order (0 = none).</summary>
    public static IReadOnlyList<string> EggGroups =>
    [
        Strings.Egg_None, Strings.Egg_Monster, Strings.Egg_Water1, Strings.Egg_Bug, Strings.Egg_Flying, Strings.Egg_Field,
        Strings.Egg_Fairy, Strings.Egg_Grass, Strings.Egg_HumanLike, Strings.Egg_Water3, Strings.Egg_Mineral,
        Strings.Egg_Amorphous, Strings.Egg_Water2, Strings.Egg_Ditto, Strings.Egg_Dragon, Strings.Egg_Undiscovered,
    ];

    public static IReadOnlyList<string> MoveCategories => [Strings.Category_Status, Strings.Category_Physical, Strings.Category_Special];

    /// <summary>Species classification ("Seed Pokémon") by species, in the project's text language; empty when unknown.</summary>
    public IReadOnlyList<string> Classifications { get; }

    /// <summary>This version's Pokédex entry by species (plain text with line breaks); empty when the game has none.</summary>
    public IReadOnlyList<string> PokedexEntries { get; }

    /// <param name="dump">The imported game: its text is the fallback, and the language to read.</param>
    /// <param name="original">The data being read (the session's base), which says how many of each thing there are.</param>
    /// <param name="layers">
    /// The session's stack of romfs layers. <b>The names come from the same stack as the data</b>: a mod that adds
    /// species, moves and items ships the text archive that names them, and reading the dump's own text instead left
    /// every one of them unnamed (or, for moves and items, out of range). Null reads the dump alone.
    /// </param>
    public GameNames(GameDump dump, GameData original, RomFsLayers? layers = null)
    {
        var text = new TextSource(dump, layers);
        // Every list is as long as the table it names: a ROM with more entries than its text (a mod that did not grow
        // the text, or did it only for some languages) gets a placeholder instead of an index out of range.
        Species = Pad(text.Get(TextName.SpeciesNames), original.SpeciesCount, Strings.Species_Entry);
        Types = text.Get(TextName.Types);
        Abilities = Pad(text.Get(TextName.AbilityNames), AbilityCount(original), Strings.Names_Ability);
        Items = Pad(text.Get(TextName.ItemNames), original.Items.Length, Strings.Names_Item);
        Moves = Pad(text.Get(TextName.MoveNames), original.Moves.Length, Strings.Names_Move);
        TrainerNames = text.Get(TextName.TrainerNames);
        TrainerClasses = text.Get(TextName.TrainerClasses);
        Natures = [.. PKHeX.Core.GameInfo.GetStrings(SaveNames.PkhexLanguage(dump.Language)).natures.Take(25)];
        ItemChoices = [Strings.Pkm_None, .. Items.Skip(1)];
        MoveChoices = [Strings.Pkm_None, .. Moves.Skip(1)];
        ItemDescriptions = [.. text.Get(TextName.ItemFlavor).Select(Plain)];
        // The split between species and forms comes from the ROM's own personal table, not from pk3DS's
        // GameInfo.MaxSpeciesID: a ROM with a species-expansion mod has more species than the game ships with.
        PersonalEntries = BuildPersonalEntryNames(original, Species, original.LastSpecies, dump.Language);
        var (classifications, entries) = GameText.PokedexFiles(dump.Title);
        Classifications = [.. (text.Raw(classifications) ?? []).Select(Plain)];
        PokedexEntries = [.. (text.Raw(entries) ?? []).Select(Plain)];
    }

    private static string Plain(string line) => GameText.ToEditable(line).Replace("\\c", "\n").Replace("\\r", "\n").Trim();

    /// <summary>The list as the game has it, followed by "Move 920"-style placeholders up to <paramref name="count"/>.</summary>
    private static string[] Pad(string[] names, int count, string format)
    {
        if (names.Length >= count)
            return names;
        var padded = new string[count];
        Array.Copy(names, padded, names.Length);
        for (int i = names.Length; i < count; i++)
            padded[i] = string.Format(format, i);
        return padded;
    }

    /// <summary>One more than the highest ability any entry of the personal table names.</summary>
    private static int AbilityCount(GameData data) =>
        data.Personal.Length == 0 ? 0 : data.Personal.Max(p => p.Abilities.Length == 0 ? 0 : p.Abilities.Max()) + 1;

    /// <summary>
    /// The game text of the project's language, read from the highest layer that has the archive — a mod's, a
    /// randomization's, or the dump's own (already loaded by pk3DS, so that case reads nothing).
    /// </summary>
    private sealed class TextSource
    {
        private readonly GameConfig config;
        private readonly GameTitle title;
        private readonly byte[][]? files;

        public TextSource(GameDump dump, RomFsLayers? layers)
        {
            config = dump.Config;
            title = dump.Title;
            if (layers is null)
                return;
            string relative = GameText.Archive(title, dump.Language);
            if (layers.TryResolve(relative) is not { } path)
                return;
            string own = Path.GetFullPath(Path.Combine(dump.RomFsPath, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (string.Equals(Path.GetFullPath(path), own, StringComparison.OrdinalIgnoreCase))
                return;
            try
            {
                files = new GARC.MemGARC(File.ReadAllBytes(path)).Files;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
                                           or ArgumentException or IndexOutOfRangeException)
            {
                files = null; // a layer whose text does not read: the dump's names are better than none
            }
        }

        public string[] Get(TextName name) => Raw(GameText.FileIndex(title, name)) ?? config.GetText(name);

        /// <summary>The lines of one file of the archive by its index, or null when there is no such file.</summary>
        public string[]? Raw(int index)
        {
            if (index < 0)
                return null;
            if (files is not null && index < files.Length)
            {
                try
                {
                    return GameText.Read(title, files[index]);
                }
                catch (InvalidDataException)
                {
                    // fall back to the dump's own file below
                }
            }
            return index < config.GameTextStrings.Length ? (string[])config.GameTextStrings[index].Clone() : null;
        }
    }

    /// <summary>The class as the game names it, or an empty string when the id is not one.</summary>
    public string TrainerClassName(int trainerClass) =>
        trainerClass >= 0 && trainerClass < TrainerClasses.Count ? TrainerClasses[trainerClass] : "";

    /// <summary>"Leader Korrina" — the class as the game names it plus the trainer's name, both in the project's language.</summary>
    public string TrainerLabel(int id, int trainerClass)
    {
        string cls = trainerClass >= 0 && trainerClass < TrainerClasses.Count ? TrainerClasses[trainerClass] : "";
        string name = id >= 0 && id < TrainerNames.Count ? TrainerNames[id] : "";
        string label = string.Join(' ', new[] { cls, name }.Where(p => !string.IsNullOrWhiteSpace(p)));
        return label.Length == 0 ? string.Format(Strings.Trainer_Unnamed, id) : label;
    }

    /// <summary>
    /// Entries above the species count are alternate forms: the base species tells with <c>FormStatsIndex</c> where
    /// they start and with <c>FormeCount</c> how many forms it has.
    /// </summary>
    /// <param name="maxSpecies">The ROM's last species (<see cref="GameData.LastSpecies"/>); the rows above it are forms.</param>
    private static string[] BuildPersonalEntryNames(GameData data, IReadOnlyList<string> species, int maxSpecies, GameLanguage language)
    {
        var names = new string[data.Personal.Length];
        for (int i = 0; i < names.Length; i++)
            names[i] = i < species.Count ? species[i] : string.Format(Strings.Species_Entry, i);

        // A ROM with more species than its text archive names (a species-expansion mod that did not grow the text)
        // leaves the extra ones with their row label, and their forms are named after it.
        string SpeciesName(int id) => id < species.Count ? species[id] : string.Format(Strings.Species_Entry, id);

        // Forms by name (2026-11-09, user: "Raichu (forma 1)" in the evolution line): a mega from the ROM's own mega
        // table — so a mod's megas are named too —, otherwise PKHeX's form name for the species as the game shipped it
        // ("Alola", "Gorra Original"); a form neither knows (one a mod added) keeps its number.
        var pkhex = PKHeX.Core.GameInfo.GetStrings(SaveNames.PkhexLanguage(language));
        var context = data.Title.Generation() == 7 ? PKHeX.Core.EntityContext.Gen7 : PKHeX.Core.EntityContext.Gen6;
        int vanillaLast = data.Title.Generation() == 7 ? 807 : 721;

        for (int s = 1; s <= maxSpecies && s < data.Personal.Length; s++)
        {
            var p = data.Personal[s];
            if (p.FormStatsIndex <= 0)
                continue;
            var megas = data.MegaForms.Where(m => m.Species == s).Select(m => m.Form).Order().ToList();
            string[] known = s <= vanillaLast
                ? PKHeX.Core.FormConverter.GetFormList((ushort)s, pkhex.types, pkhex.forms, PKHeX.Core.GameInfo.GenderSymbolUnicode, context)
                : [];
            // A species-expansion mod adds the later generations' forms in their own order (Galarian Meowth is form 2,
            // Bloodmoon Ursaluna form 1, Alcremie's nine flavours): those are named from the newest list that has as many
            // forms as the ROM (issue #2: they read "forma 2").
            if (data.IsExpanded && known.Length < p.FormeCount)
                foreach (var later in new[] { PKHeX.Core.EntityContext.Gen9, PKHeX.Core.EntityContext.Gen8 })
                {
                    string[] list = PKHeX.Core.FormConverter.GetFormList((ushort)s, pkhex.types, pkhex.forms, PKHeX.Core.GameInfo.GenderSymbolUnicode, later);
                    if (list.Length >= p.FormeCount)
                    {
                        known = list;
                        break;
                    }
                }
            for (int form = 1; form < p.FormeCount; form++)
            {
                int index = p.FormStatsIndex + form - 1;
                if (index >= names.Length)
                    continue;
                int mega = megas.IndexOf(form);
                names[index] = mega >= 0
                    ? string.Format(Strings.Species_Mega, SpeciesName(s)) + (megas.Count > 1 ? " " + (char)('X' + mega) : "")
                    : form < known.Length && known[form].Trim() is { Length: > 0 } formName && !known[form].StartsWith("Mega", StringComparison.Ordinal)
                        ? string.Format(Strings.Species_FormNamed, SpeciesName(s), formName)
                        : string.Format(Strings.Species_Form, SpeciesName(s), form);
            }
        }

        return names;
    }
}
