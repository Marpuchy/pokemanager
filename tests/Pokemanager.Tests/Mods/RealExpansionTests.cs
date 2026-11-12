using PKHeX.Core;
using GameLanguage = Pokemanager.Model.Dump.GameLanguage;
using Pokemanager.App.Services;
using Pokemanager.Save;
using Pokemanager.Model.Edits;
using Pokemanager.Model.Build;
using Pokemanager.Model.Data;
using Pokemanager.Model.Dump;
using Pokemanager.Model.Editing;
using Pokemanager.Model.Mods;
using Pokemanager.Model.Projects;

namespace Pokemanager.Tests.Mods;

/// <summary>
/// The USUM Expansion Mod over a real Ultra Sun: what the application shows once the mod is on. Opt-in, like the
/// other real-data tests: <c>POKEMANAGER_USUM_DUMP</c> is an imported Ultra Sun game folder (the one with
/// <c>pokemanager-game.json</c>) and <c>POKEMANAGER_MOD_LIBRARY</c> the mods library holding <c>usum-expansion</c>.
/// </summary>
public sealed class RealExpansionTests
{
    private static (string Dump, string Library)? Paths()
    {
        string? dump = Environment.GetEnvironmentVariable("POKEMANAGER_USUM_DUMP");
        string? library = Environment.GetEnvironmentVariable("POKEMANAGER_MOD_LIBRARY");
        if (string.IsNullOrEmpty(dump) || string.IsNullOrEmpty(library))
            return null;
        return (dump, library);
    }

    private static (GameDump Dump, EditorSession Session) Open(bool withMod)
    {
        var (dumpDir, library) = Paths()!.Value;
        var project = new Project { DumpDirectory = dumpDir, Game = GameTitle.UltraSun };
        if (withMod)
            project.Mods.Set(ModCatalog.UsumExpansionId, true);
        var dump = GameDump.Open(project.RomFsPath, project.ExeFsPath, GameLanguage.Spanish);
        return (dump, EditorSession.Open(project, null, new ModLibrary(library)));
    }

    [Fact]
    public void WithTheMod_TheNewSpeciesMovesAndItemsHaveTheirNames()
    {
        Assert.SkipWhen(Paths() is null, "POKEMANAGER_USUM_DUMP / POKEMANAGER_MOD_LIBRARY are not set.");

        var (vanillaDump, vanilla) = Open(withMod: false);
        var vanillaNames = new GameNames(vanillaDump, vanilla.Original, vanilla.Layers);
        var (dump, session) = Open(withMod: true);
        var names = new GameNames(dump, session.Original, session.Layers);

        Assert.True(session.Original.IsExpanded);
        Assert.True(session.Original.SpeciesCount > vanilla.Original.SpeciesCount);
        Assert.Equal(session.Original.SpeciesCount, names.Species.Count);

        // Every species past the game's own has a real name from the mod's text, not a placeholder.
        int unnamed = Enumerable.Range(vanilla.Original.SpeciesCount, session.Original.SpeciesCount - vanilla.Original.SpeciesCount)
            .Count(i => string.IsNullOrWhiteSpace(names.Species[i]) || names.Species[i] == string.Format(Pokemanager.App.Resources.Strings.Species_Entry, i));
        Assert.Equal(0, unnamed);

        // A list per table, never shorter than it: nothing indexes out of range.
        Assert.True(names.Moves.Count >= session.Original.Moves.Length);
        Assert.True(names.Items.Count >= session.Original.Items.Length);
        Assert.True(names.Moves.Count > vanillaNames.Moves.Count || session.Original.Moves.Length == vanilla.Original.Moves.Length);
        Assert.True(names.PersonalEntries.Count == session.Original.Personal.Length);

        TestContext.Current.SendDiagnosticMessage(
            $"species {vanilla.Original.SpeciesCount} -> {session.Original.SpeciesCount}, moves {vanilla.Original.Moves.Length} -> {session.Original.Moves.Length} ({vanillaNames.Moves.Count} -> {names.Moves.Count} names), items {vanilla.Original.Items.Length} -> {session.Original.Items.Length} ({vanillaNames.Items.Count} -> {names.Items.Count} names); last: {names.Species[^1]}, {names.Moves[^1]}, {names.Items[^1]}");
    }

    /// <summary>
    /// A Pokémon the mod added, in a save: the save editor creates it, finds nothing wrong with it, writes it and reads
    /// it back. Before, a save with one of those in it was "not safe for the game" and could not be written at all.
    /// </summary>
    [Fact]
    public void ASaveHoldingANewSpeciesCanBeEditedAndWritten()
    {
        Assert.SkipWhen(Paths() is null, "POKEMANAGER_USUM_DUMP / POKEMANAGER_MOD_LIBRARY are not set.");

        var (_, session) = Open(withMod: true);
        var rom = session.Original;
        string dir = Directory.CreateTempSubdirectory("pokemanager-expansion-save-").FullName;
        try
        {
            var sav = new SAV7USUM { OT = "Test", Language = (int)LanguageID.English };
            string path = Path.Combine(dir, "main");
            File.WriteAllBytes(path, sav.Write().ToArray());

            var doc = SaveDocument.Open(path, rom);
            Assert.Equal(rom.LastSpecies, doc.MaxSpecies);
            Assert.True(doc.DexMaxSpecies < doc.MaxSpecies);

            ushort species = (ushort)(rom.LastSpecies - 10);
            var pk = doc.Create(species, 30);
            doc.Set(new SaveSlot(null, 0), pk);
            Assert.Empty(doc.Check(doc.Get(new SaveSlot(null, 0))));
            Assert.All(doc.Stats(doc.Get(new SaveSlot(null, 0))), s => Assert.True(s > 0));
            Assert.False(doc.GetSeen(species));
            doc.SetCaught(species, true); // past the Pokédex: nothing to set, and nothing breaks

            doc.Write(Path.Combine(dir, "backups"));
            var reread = SaveDocument.Open(path, rom);
            Assert.Equal(species, reread.Get(new SaveSlot(null, 0)).Species);
            Assert.Equal(30, reread.Level(reread.Get(new SaveSlot(null, 0))));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// The mod's own Mega Stones (505-520, 961+) are items PKHeX's Ultra Sun lists do not have, and the game keeps them in
    /// the Items pocket (2026-11-04, user: "the Mega Stones give this error, but they are in my bag in game"). The pocket
    /// comes from the ROM's item table, so they are safe for the game, can be picked and can be given.
    /// </summary>
    [Fact]
    public void TheModsMegaStones_GoInTheItemsPocket()
    {
        Assert.SkipWhen(Paths() is null, "POKEMANAGER_USUM_DUMP / POKEMANAGER_MOD_LIBRARY are not set.");

        var (_, session) = Open(withMod: true);
        var rom = session.Original;
        string dir = Directory.CreateTempSubdirectory("pokemanager-expansion-bag-").FullName;
        try
        {
            var sav = new SAV7USUM { OT = "Test", Language = (int)LanguageID.English };
            string path = Path.Combine(dir, "main");
            File.WriteAllBytes(path, sav.Write().ToArray());
            var doc = SaveDocument.Open(path, rom);
            doc.Set(new SaveSlot(null, 0), doc.Create(25, 5));

            var items = doc.Pouches.First(p => p.Type == InventoryType.Items);
            Assert.Contains((ushort)517, doc.PouchItems(items));
            Assert.Contains((ushort)520, doc.PouchItems(items));
            Assert.DoesNotContain((ushort)517, doc.PouchItems(doc.Pouches.First(p => p.Type == InventoryType.Medicine)));
            doc.SetPouch(items, [(517, 1), (520, 1)]);
            Assert.Empty(doc.Validate());
            Assert.Equal(1, doc.GiveItem(1000, 1));
            Assert.True(doc.HasItem(1000));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// The mod keeps the game's icon table and appends the new species' icons after it, in species order: Meltan is
    /// the first icon after the egg, and every species up to the last one has its own.
    /// </summary>
    [Fact]
    public void TheNewSpeciesHaveTheirIcons()
    {
        Assert.SkipWhen(Paths() is null, "POKEMANAGER_USUM_DUMP / POKEMANAGER_MOD_LIBRARY are not set.");

        var (_, session) = Open(withMod: true);
        var data = session.Original;
        string code = session.Project.CodePath(session.Mods);
        var icons = PokemonIcons.Load(session.Layers, code, GameTitle.UltraSun.Layout().Icons[0], data.SpeciesCount, data.VanillaSpeciesCount,
            Path.Combine(session.Project.ExeFsPath, "code.bin"));

        Assert.NotNull(icons);
        Assert.Equal(1, icons.IndexFor(1));
        Assert.Equal(1152, icons.IndexFor(807));       // Zeraora, the game's last
        Assert.Equal(1154, icons.IndexFor(808));       // Meltan, after the egg
        Assert.Equal(1154 + 98, icons.IndexFor(906));  // Sprigatito
        Assert.True(icons.IndexFor(data.LastSpecies) < icons.Count);
        Assert.NotNull(icons.For(data.LastSpecies));
    }

    /// <summary>
    /// The trial slots show the game's type crystals with the mod on too: the mod's item icon archive has a longer run
    /// of same-shaped icons (new Poké Balls), so it is the game's own archive that is read.
    /// </summary>
    [Fact]
    public void TheTrialCrystalsAreTheGamesWithTheModOn()
    {
        Assert.SkipWhen(Paths() is null, "POKEMANAGER_USUM_DUMP / POKEMANAGER_MOD_LIBRARY are not set.");

        var (_, vanilla) = Open(withMod: false);
        var (_, modded) = Open(withMod: true);
        var expected = ItemIcons.LoadTypeCrystals(vanilla.Layers, GameTitle.UltraSun);
        var actual = ItemIcons.LoadTypeCrystals(modded.Layers, GameTitle.UltraSun);

        Assert.NotNull(expected);
        Assert.NotNull(actual);
        Assert.Equal(expected.Length, actual.Length);
        for (int i = 0; i < expected.Length; i++)
            Assert.Equal(expected[i]?.Rgba, actual[i]?.Rgba);
    }

    /// <summary>The mod's own species carry abilities past 255, read whole: Nacli's first is Purifying Salt (272).</summary>
    [Fact]
    public void AbilitiesPast255_AreReadWhole()
    {
        Assert.SkipWhen(Paths() is null, "POKEMANAGER_USUM_DUMP / POKEMANAGER_MOD_LIBRARY are not set.");

        var (dump, session) = Open(withMod: true);
        var names = new GameNames(dump, session.Original, session.Layers);
        var nacli = session.Original.Personal[932];
        Assert.Equal(272, nacli.Abilities[0]);
        Assert.Equal(5, nacli.Abilities[1]);
        Assert.False(string.IsNullOrWhiteSpace(names.Abilities[272]));
        Assert.True(names.Abilities.Count > 300);
    }

    /// <summary>
    /// What hung the user's battle against Hala (2026-11-04), and the rule they asked for: the mod's megas sit among the
    /// ordinary forms, so the randomizer can hand a trainer Mega Staraptor itself. The build sends it out as Staraptor
    /// holding its stone, as the last Pokémon, so it mega evolves in battle; the Z-crystal stays on another one.
    /// </summary>
    [Fact]
    public void BattleSafety_TheRivalsStarterIsTheMega_WhenItCanMegaEvolve()
    {
        Assert.SkipWhen(Paths() is null, "POKEMANAGER_USUM_DUMP / POKEMANAGER_MOD_LIBRARY are not set.");
        var (_, session) = Open(withMod: true);
        var data = session.Current;
        byte[][] records = [.. Pokemanager.Model.Data.GameData.ReadFiles(session.Layers, data.Layout.TrainerData)];
        byte[][] teams = [.. Pokemanager.Model.Data.GameData.ReadFiles(session.Layers, data.Layout.TrainerPokemon)];

        // Hau's line is in every one of his battles, whatever the randomizer made of it.
        var lines = Pokemanager.Model.Data.BattleSafety.StarterFamilies(data, data.Title, records, teams);
        var roles = Pokemanager.Model.Data.TrainerRoles.Of(data.Title);
        var hau = roles.Where(r => r.Value.StartsWith("FRIEND") && r.Value.EndsWith("-0")).Select(r => r.Key).ToList();
        Assert.NotEmpty(hau);
        Assert.All(hau, id => Assert.True(lines.ContainsKey(id)));

        // Put a Pokémon that can mega evolve of that line in the middle of his last battle and Charizard (another mega,
        // holding its stone) last: the starter takes the mega and the last slot.
        int line = lines[hau[0]];
        var first = Pokemanager.Model.Data.BattleSafety.FirstStages(data);
        var megaSpecies = data.MegaStoneByForm.Where(p => p.Value != 0).Select(p => p.Key.Species).ToHashSet();
        int? starter = megaSpecies.Where(s => first.GetValueOrDefault(s) == line).Cast<int?>().FirstOrDefault();
        int lastFight = hau.OrderByDescending(id => Trainer.Read(data.Title, records[id], teams[id]).Team.Last().Level).First();
        var trainer = Trainer.Read(data.Title, records[lastFight], teams[lastFight]);
        var team = trainer.Team.ToList();
        Assert.True(team.Count >= 3);
        // The line of this dump may have no mega: give the test one by using a line that has.
        if (starter is null)
        {
            starter = 6; // Charizard
            foreach (int id in hau)
            {
                var t = Trainer.Read(data.Title, records[id], teams[id]);
                var m = t.Team.First(x => first.GetValueOrDefault(x.Species) == line);
                m.Species = 6;
                m.Form = 0;
                records[id] = t.WriteData().ToArray();
                teams[id] = t.WriteTeam().ToArray();
            }
            trainer = Trainer.Read(data.Title, records[lastFight], teams[lastFight]);
            team = trainer.Team.ToList();
            // Venusaur holding its own stone, somewhere else: the mega the usual rule would have kept.
            var other = team.First(m => m.Species != 6);
            other.Species = 3;
            other.Form = 0;
            other.Item = data.MegaStoneByForm[(3, 1)];
        }
        else
        {
            team[1].Species = starter.Value;
            team[1].Form = 0;
            team[^1].Species = 6;
            team[^1].Item = data.MegaStoneByForm[(6, 1)];
        }
        records[lastFight] = trainer.WriteData().ToArray();
        teams[lastFight] = trainer.WriteTeam().ToArray();

        Pokemanager.Model.Data.BattleSafety.FixTrainers(data, data.Title, records, teams, new TrainerBattleItems());
        var fixedTeam = Trainer.Read(data.Title, records[lastFight], teams[lastFight]).Team.ToList();
        Assert.True(starter == fixedTeam[^1].Species, $"line {line} starter {starter} fight {lastFight} lv {fixedTeam[^1].Level}: {string.Join(", ", fixedTeam.Select(m => $"{m.Species}[{m.Item}]"))}");
        Assert.Contains(data.MegaStoneByForm.Where(p => p.Key.Species == starter && p.Value != 0).Select(p => p.Value), s => s == fixedTeam[^1].Item);
        Assert.Single(fixedTeam, m => data.MegaStones.Contains(m.Item));
    }

    [Fact]
    public void BattleSafety_MegaLastAndAsItself_ZCrystalNeverLast()
    {
        Assert.SkipWhen(Paths() is null, "POKEMANAGER_USUM_DUMP / POKEMANAGER_MOD_LIBRARY are not set.");
        var (_, session) = Open(withMod: true);
        var data = session.Current;
        const int Staraptor = 398;
        Assert.Contains((Staraptor, 1), data.MegaForms);
        int staraptorite = data.MegaStoneByForm[(Staraptor, 1)];
        Assert.NotEqual(0, staraptorite);

        byte[][] records = [.. Pokemanager.Model.Data.GameData.ReadFiles(session.Layers, data.Layout.TrainerData)];
        byte[][] teams = [.. Pokemanager.Model.Data.GameData.ReadFiles(session.Layers, data.Layout.TrainerPokemon)];

        // Hala: Mega Staraptor (form 1, no item) in the first slot and the Fightinium Z on the last one.
        var hala = Trainer.Read(data.Title, records[23], teams[23]);
        var team = hala.Team.ToList();
        int count = team.Count;
        Assert.True(count >= 3);
        team[0].Species = Staraptor;
        team[0].Form = 1;
        team[0].Item = 0;
        team[^1].Item = 782;
        // And a Totem Lurantis in the middle, as UPR ZX put it in the user's Ultra Moon.
        team[1].Species = 754;
        team[1].Form = 1;
        records[23] = hala.WriteData().ToArray();
        teams[23] = hala.WriteTeam().ToArray();

        // Megas from level 1 here: Hala's team is level 26, under the default 35.
        var rules = new TrainerBattleItems();
        rules.Boss.MegaFromLevel = 1;
        rules.Important.MegaFromLevel = 1;
        Assert.True(BattleSafety.FixTrainers(data, data.Title, records, teams, rules) > 0);

        var fixedTeam = Trainer.Read(data.Title, records[23], teams[23]).Team.ToList();
        Assert.Equal(count, fixedTeam.Count);
        Assert.Equal(Staraptor, fixedTeam[^1].Species);  // the mega is the last one
        Assert.Equal(0, fixedTeam[^1].Form);             // sent out as itself
        Assert.Equal(staraptorite, fixedTeam[^1].Item);  // and mega evolves with its stone
        Assert.Equal(1, fixedTeam.Count(m => BattleSafety.IsZCrystal(m.Item)));
        Assert.False(BattleSafety.IsZCrystal(fixedTeam[^1].Item));
        Assert.DoesNotContain(fixedTeam, m => BattleSafety.IsTotem(m.Species, m.Form)); // no totem outside a totem battle
        Assert.Equal(0, BattleSafety.FixTrainers(data, data.Title, records, teams, rules)); // nothing left to do

        // 2026-11-09, user: with the default rules, bosses and important trainers mega evolve from level 35 and carry a
        // crystal, ordinary ones neither.
        BattleSafety.FixTrainers(data, data.Title, records, teams, new TrainerBattleItems());
        var stones = data.MegaStones.ToHashSet();
        for (int id = 1; id < records.Length; id++)
        {
            var trainer = Trainer.Read(data.Title, records[id], teams[id]);
            if (trainer.Unreadable || trainer.Count == 0)
                continue;
            var kind = TrainerRoles.DifficultyOf(data.Title, id);
            int megas = trainer.Team.Count(m => stones.Contains(m.Item));
            int crystals = trainer.Team.Count(m => BattleSafety.IsZCrystal(m.Item));
            bool high = trainer.Team.Last().Level >= 35;
            if (kind == TrainerDifficulty.Regular)
                Assert.Equal((0, 0), (megas, crystals));
            if (kind == TrainerDifficulty.Boss)
            {
                Assert.True(high ? megas == 1 && stones.Contains(trainer.Team.Last().Item) : megas == 0, $"boss {id} mega");
                Assert.True(trainer.Count < 2 || crystals == 1, $"boss {id} crystal");
                Assert.False(BattleSafety.IsZCrystal(trainer.Team.Last().Item), $"boss {id} crystal on the last");
            }
        }
    }

    /// <summary>Issue #2: the advanced editor shows the teams as the build writes them.</summary>
    [Fact]
    public void TrainersAsBuilt_HaveTheLevelPercentageAndTheBossMegas()
    {
        Assert.SkipWhen(Paths() is null, "POKEMANAGER_USUM_DUMP / POKEMANAGER_MOD_LIBRARY are not set.");
        var (_, session) = Open(withMod: true);
        session.Project.Tweaks.TrainerLevelPercent = 50;
        var built = Pokemanager.Model.Build.TrainersAsBuilt.Build(session);
        var before = session.Current.Trainers;
        Assert.Equal(before.Length, built.Count);

        var hala = built[23];
        var original = before[23];
        Assert.Equal(GameLevels.Scale(original.Member(0)!.Level, 50), hala.Member(0)!.Level);
        var last = hala.Member(hala.Count - 1)!;
        Assert.Contains(last.Item, session.Current.MegaStoneByForm.Values);
        // The session itself is not touched.
        Assert.Equal(original.Member(0)!.Level, session.Current.Trainers[23].Member(0)!.Level);
    }

    /// <summary>
    /// Meowstic's form 3 repeats its mega and nothing reaches it (2026-11-10, user): it is not listed, and the build gives
    /// it Mega Meowstic's stats, types and abilities.
    /// </summary>
    [Fact]
    public void MeowsticsDuplicateMega_IsHidden_AndBuiltAsTheMega()
    {
        Assert.SkipWhen(Paths() is null, "POKEMANAGER_USUM_DUMP / POKEMANAGER_MOD_LIBRARY are not set.");
        var (_, session) = Open(withMod: true);
        var data = session.Current;
        Assert.True(data.IsDuplicateMega(678, 3));
        Assert.False(data.IsDuplicateMega(678, 2));
        var index = PersonalIndex.Of(data);
        int mega = index.Row(678, 2), duplicate = index.Row(678, 3);
        // The mod ships both as the same mega (74/48/76/143/101/124); a randomizer or an edit sets them apart.
        session.SetInt(GameTables.Personal, mega, "spa", 150);
        Assert.NotEqual(150, data.Personal[duplicate].SPA);

        var outputs = ModBuilder.BuildEdits(session);
        var garc = new pk3DS.Core.CTR.GARC.MemGARC(outputs["romfs/" + data.Layout.Personal]);
        var built = new pk3DS.Core.Structures.PersonalInfo.PersonalInfoSM(garc.GetFile(duplicate));
        var source = data.Personal[mega];
        Assert.Equal([source.HP, source.ATK, source.DEF, source.SPA, source.SPD, source.SPE],
            [built.HP, built.ATK, built.DEF, built.SPA, built.SPD, built.SPE]);
        Assert.Equal(source.Types, built.Types);
        Assert.Equal(254, built.Gender);   // the rest of the row is its own
        Assert.Empty(ModBuilder.MirrorDuplicateMegas(data));   // a second build has nothing left to copy
    }
}
