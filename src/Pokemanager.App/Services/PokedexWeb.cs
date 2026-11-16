using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Media.Imaging;
using Pokemanager.App.Resources;
using Pokemanager.Model.Data;
using Pokemanager.Model.Dump;

namespace Pokemanager.App.Services;

/// <summary>
/// The Pokédex as one web page (2026-11-16, user: "the app uses too much while playing, and all I look at is the
/// Pokédex: a link to a page with my Pokédex"). A single file with everything inside — the ROM's data (types, stats,
/// abilities, held items, evolutions with the mod's megas, moves by level), the sprites as data URIs and what the save
/// has seen and caught at that moment — so it opens in any browser with the application closed and no internet.
/// It is written again when the button is pressed or the ROM is built.
/// </summary>
public static class PokedexWeb
{
    /// <summary>Where a project's page is kept: <c>Documents\Pokemanager\Pokedex\&lt;project&gt;.html</c>.</summary>
    public static string PathFor(string projectPath) =>
        System.IO.Path.Combine(AppSettings.DocumentsRoot, "Pokedex", System.IO.Path.GetFileNameWithoutExtension(projectPath) + ".html");

    /// <param name="seen">Whether the save has seen a species (null: no save, nothing marked).</param>
    public static void Write(string path, string title, GameData rom, GameNames names, PokemonSprites sprites,
        Func<int, bool>? seen, Func<int, bool>? caught, int dexMax)
    {
        var index = PersonalIndex.Of(rom);
        bool gen7 = rom.Title.Generation() == 7;
        string N(IReadOnlyList<string> list, int id) => id >= 0 && id < list.Count ? list[id] : $"#{id}";

        var types = new JsonArray([.. Enumerable.Range(0, 18).Select(t =>
        {
            var c = TypeColors.Of(t);
            return (JsonNode)new JsonObject { ["n"] = N(names.Types, t), ["c"] = $"#{c.R:X2}{c.G:X2}{c.B:X2}" };
        })]);


        var entries = new JsonArray();
        var keyOfRow = new Dictionary<int, string>();
        for (int row = 1; row < rom.Personal.Length; row++)
        {
            if (index.Pokemon(row) is not { } mon || rom.IsDuplicateMega(mon.Species, mon.Form))
                continue;
            keyOfRow[row] = $"{mon.Species}:{mon.Form}";
        }
        foreach (var (row, key) in keyOfRow)
        {
            var (species, form) = index.Pokemon(row)!.Value;
            var p = rom.Personal[row];
            bool own = species < rom.VanillaSpeciesCount && form == 0;
            bool inDex = form == 0 && species <= dexMax;
            // Only the levels it learns something at, as the app's Pokédex: which moves stays out (2026-11-16, user).
            var learn = new JsonArray();
            if (row < rom.Learnsets.Length)
                foreach (int level in rom.Learnsets[row].Levels.Select(l => (int)l).Distinct().Order())
                    learn.Add(level);
            var matchups = new JsonArray([.. TypeMatchups.Groups(p.Types.Distinct())
                .Select(g => (JsonNode)new JsonArray(g.Multiplier, new JsonArray([.. g.Types.Select(t => (JsonNode)t)])))]);
            entries.Add(new JsonObject
            {
                ["k"] = key,
                ["s"] = species,
                ["f"] = form,
                ["n"] = row < names.PersonalEntries.Count ? names.PersonalEntries[row] : N(names.Species, species),
                ["t"] = new JsonArray([.. p.Types.Distinct().Select(t => (JsonNode)t)]),
                ["st"] = new JsonArray(p.HP, p.ATK, p.DEF, p.SPA, p.SPD, p.SPE),
                ["ab"] = new JsonArray(N(names.Abilities, p.Abilities[0]), N(names.Abilities, p.Abilities[1]), N(names.Abilities, p.Abilities[2])),
                ["cl"] = own && species < names.Classifications.Count ? names.Classifications[species] : "",
                ["en"] = own && species < names.PokedexEntries.Count ? names.PokedexEntries[species] : "",
                ["h"] = p.Height / 100.0,
                ["w"] = p.Weight / 10.0,
                ["cr"] = p.CatchRate,
                ["eg"] = string.Join(" · ", new[] { p.EggGroups[0], p.EggGroups[1] }.Distinct()
                    .Select(g => g >= 0 && g < GameNames.EggGroups.Count ? GameNames.EggGroups[g] : "?")),
                ["gr"] = p.EXPGrowth < GameNames.ExpGrowth.Count ? GameNames.ExpGrowth[p.EXPGrowth] : "?",
                ["ge"] = p.Gender switch
                {
                    255 => Strings.Pkm_Genderless,
                    254 => Strings.Dex_FemaleOnly,
                    0 => Strings.Dex_MaleOnly,
                    var g => string.Format(Strings.Dex_Ratio, 100 - (g * 100 / 254), g * 100 / 254),
                },
                ["hc"] = p.HatchCycles,
                ["d"] = inDex,
                ["sn"] = inDex && seen?.Invoke(species) == true,
                ["ca"] = inDex && caught?.Invoke(species) == true,
                ["lg"] = Legendaries.Contains(species),
                ["mg"] = rom.MegaForms.Contains((species, form)),
                ["i"] = Sprite(sprites.ForPersonalEntry(row)),
                ["mu"] = matchups,
                ["l"] = learn,
            });
        }

        // How each Pokémon becomes another, and the megas next to the Pokémon they come from.
        var edges = new JsonArray();
        int FormCount(int s) => s > 0 && s < rom.Personal.Length && rom.Personal[s].FormeCount is > 0 and var n ? n : 1;
        for (int row = 0; row < rom.Evolutions.Length; row++)
        {
            if (index.Pokemon(row) is not { } from)
                continue;
            foreach (var (_, e) in rom.Evolutions[row].InUse())
            {
                if (e.Species <= 0 || e.Species > rom.LastSpecies)
                    continue;
                int targetForm = EvolutionSlots.TargetForm(e, from.Form, FormCount(e.Species), gen7);
                edges.Add(new JsonArray($"{from.Species}:{from.Form}", $"{e.Species}:{targetForm}", EvolutionText.How(e, gen7, names)));
            }
        }
        foreach (var (key, stone) in rom.MegaStoneByForm)
            if (!rom.IsDuplicateMega(key.Species, key.Form))
                edges.Add(new JsonArray($"{key.Species}:0", $"{key.Species}:{key.Form}",
                    stone > 0 && stone < names.Items.Count ? string.Format(Strings.Evo_MegaWith, names.Items[stone]) : Strings.Evo_Mega));

        var text = new JsonObject
        {
            ["title"] = title,
            ["search"] = Strings.Web_Search,
            ["all"] = Strings.Web_All,
            ["seen"] = Strings.Web_Seen,
            ["caught"] = Strings.Web_Caught,
            ["forms"] = Strings.Web_Forms,
            ["stats"] = Strings.Pkm_Stats,
            ["total"] = Strings.Badge_Total,
            ["statNames"] = new JsonArray(Strings.Stat_HP, Strings.Stat_Atk, Strings.Stat_Def, Strings.Stat_SpA, Strings.Stat_SpD, Strings.Stat_Spe),
            ["abilities"] = Strings.Web_Abilities,
            ["abilityNames"] = new JsonArray(Strings.Field_Ability1, Strings.Field_Ability2, Strings.Field_AbilityHidden),
            ["showAbilities"] = Strings.Web_ShowAbilities,
            ["moves"] = Strings.Web_MoveLevels,
            ["evo"] = Strings.Web_Evo,
            ["gender"] = Strings.Dex_GenderRatio,
            ["hatch"] = Strings.Field_HatchCycles,
            ["evolution"] = Strings.Web_Evolution,
            ["noEvolution"] = Strings.Web_NoEvolution,
            ["matchups"] = Strings.Web_Matchups,
            ["data"] = Strings.Web_Data,
            ["height"] = Strings.Web_Height,
            ["weight"] = Strings.Web_Weight,
            ["catchRate"] = Strings.Field_CatchRate,
            ["eggGroups"] = Strings.Dex_EggGroups,
            ["growth"] = Strings.Field_ExpGrowth,
            ["noEntry"] = Strings.Dex_NoEntry,
            ["made"] = string.Format(Strings.Web_Made, DateTime.Now.ToString("g")),
        };
        var data = new JsonObject { ["text"] = text, ["types"] = types, ["entries"] = entries, ["edges"] = edges };
        string json = data.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping })
            .Replace("</", "<\\/");

        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        string page = Template.Replace("/*DATA*/null", json).Replace("{{TITLE}}", System.Net.WebUtility.HtmlEncode(title))
            .Replace("{{LANG}}", Strings.Culture?.TwoLetterISOLanguageName ?? System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
        File.WriteAllText(path, page, new UTF8Encoding(false));
    }

    private static string Sprite(Bitmap? bitmap)
    {
        if (bitmap is null)
            return "";
        using var stream = new MemoryStream();
        bitmap.Save(stream);
        return "data:image/png;base64," + Convert.ToBase64String(stream.ToArray());
    }

    private static string Template => template ??= ReadTemplate();
    private static string? template;

    private static string ReadTemplate()
    {
        using var stream = typeof(PokedexWeb).Assembly.GetManifestResourceStream("Pokemanager.App.Assets.pokedex.html")
                           ?? throw new InvalidOperationException("pokedex.html is not embedded");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
