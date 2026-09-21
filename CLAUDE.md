# Pokemanager

Editor + randomizer + live dashboard for the 3DS Pokémon games (X/Y, ORAS, Sun/Moon, Ultra Sun/Ultra Moon), on a 3DS
emulator (Citra / Azahar). It started X/Y-only; see "Every 3DS Pokémon game" in section 2.

This document started as the handover of an earlier research session. Everything marked
**verified** was read directly from the pk3DS and Azahar source code, or measured on real data — not taken from forums.

**Project language: English** (code, comments, UI, docs, commit messages). The UI is localized with
`.resx` resources; Spanish (`Strings.es.resx`) is selectable in Settings and applies on restart.
Talk to the user in Spanish.

---

## 1. What it is

A middleman between three things: the user's game dump, the emulator, and the user.

**The rule that governs everything:** the application never modifies the dump. It is read-only,
always. Everything it produces can be deleted and regenerated from `dump + project`.

### The layers (the order matters and is not the obvious one)

```
original dump  →  randomization(seed)  →  manual edits  →  build
```

The randomizer shuffles the original; manual edits are applied on top. So manual edits always win,
and the result stays reproducible because the random part depends only on (dump, seed, settings).

Consequences: changing the seed does not destroy edits; removing edits gives back the pure random;
the shareable project weighs KBs and contains no game bytes.

### Edit addressing

An edit is `{table, id, field, value}`. Neutral with respect to the on-disk format. This is what lets
the edit layer survive randomizer changes and be read in a diff.

---

## 2. Decisions already made

| Decision | Choice |
|---|---|
| Target game | Pokémon X / Y (Gen 6) |
| Code base | Fork of `pk3DS.Core` (kwsch/pk3DS) |
| Language | C# / .NET 10 |
| UI | Avalonia 12 (desktop, cross-platform) |
| Emulator | Azahar 2126.1.1 in the original plan; **the user actually plays on Citra** |
| First milestone | The edit → see it in game loop |

**License: pk3DS is GPL-3.0.** Building on it requires publishing this project under GPL-3 with
source available. Accepted.

### Product reorientation (2026-09-13) — overrides the above

Today the user plays like this: **Universal Pokémon Randomizer ZX** (`PokeRandoZX.jar` 4.6.1, `.rnqs`
presets per group of friends) generates the ROM, and **PKHeX** tweaks the save. What they want is **one
app combining both**, not an exhaustive ROM data editor:

| Decision | Choice |
|---|---|
| Randomization engine | **UPR ZX underneath**, not our own randomizer. Invoked through Java (JDK 25 installed) with our own launcher that calls `Randomizer.randomize(file, log, seed)`, because the UPR CLI (`cli -s -i -o -d -u -l`) **cannot fix the seed**. |
| Seed | Reproducible and changeable from the app. Warn when a save is in progress: caught Pokémon keep their species, but base stats/abilities/learnsets come from the ROM and change. |
| Save editor | **PKHeX.Core** (NuGet, GPL-3) on the emulator's `main` file, **with the emulator closed** and an automatic backup before writing. Live (RPC) comes later. |
| What gets edited in the save | Party and box Pokémon, bag, trainer data, Pokédex. |
| Validation | Two levels: **"safe for the game"** (existing IDs, ranges, checksums) blocks saving; **PKHeX legality** only warns, because it compares against the original game and would flag what the randomizer makes valid. |
| Manual ROM editor already built | Kept as an **advanced** tab, applied on top of the randomized result. |
| Order | **Phase A: randomizer** → Phase B: save editor → then the live dashboard. |

X save in Azahar: `%APPDATA%\Azahar\sdmc\Nintendo 3DS\000…0\000…0\title\00040000\00055d00\data\00000001\main` (415 232 B).
Same layout under `%APPDATA%\Citra`. The user's UPR ZX and PKHeX: `D:\citra\roms\`.

**Phase A done** (`Pokemanager.Randomizer`, Randomizer tab). **Verified** with UPR ZX 4.6.1,
preset `alex.rnqs` and the real dump:
- `upr/PokemanagerUpr.java` runs with Java's source launcher (`java -cp jar X.java`).
  It repeats `CliRandomizer.performDirectRandomization` (Gen6RomHandler, `Settings.read`, `tweakForRom`,
  bundle `com/dabomstew/pkrandom/newgui/Bundle`, `new Randomizer(settings, handler, bundle, true)`).
  Warnings are printed as codes (`WARNING:CUSTOM_STARTERS_CHANGED`, `WARNING:OLD_PRESET`) and translated in C#.
- `-d` output: `<out>/0004000000055D00/{romfs/…, code.bin}`, 24 files, ~27 MB, ~9 s. `code.bin`
  comes out **decompressed**. UPR also touches romfs `.cro` files.
- **Same seed → byte-identical files.** The log differs only in "Time elapsed".
- The log is split into `--Title--` sections (15 with that preset).

### Redesign after the user's first test (2026-09-13) — overrides the phase A notes above

| Request | Implementation |
|---|---|
| The randomized ROM is **a new file** with a chosen name, in the base ROM folder, which stays clean | `RomBuilder` → `<base folder>/<name>.cxi` (+ `.cxi.log`). UPR only writes 3DS as NCCH (.cxi). **LayeredFS mods are no longer installed**: they applied to any ROM of the game. `ModInstaller.Uninstall` removes those installed by the previous version (only what its manifest lists). |
| Randomizer options **editable in the app** | Java launcher `describe-settings` / `write-settings` on `Settings` via reflection (140 options, 22 enums, misc tweaks as bits). Round-trip without changes = identical `.rnqs`. `UprOptionCatalog` labels and groups them (resources). |
| UPR and PKHeX **bundled**, no paths to pick | `tools/upr/PokeRandoZX.jar` (4.6.1) is copied with the app; PKHeX.Core comes from NuGet. Java is still the system one (to do: `jlink` in the installer). |
| Paths in a **Pokemanager settings** window | Language, emulator (default: most recently used according to `config/qt-config.ini`), base ROM, tools, cache and backups. |
| When randomizing a started save, **its Pokémon abilities must change** | `Pokemanager.Save.SaveUpdater` (PKHeX.Core). |

**Verified — why they didn't change:** (1) the user plays on **Citra**, not Azahar, and the earlier version
installed the mod under `%APPDATA%\Azahar`; (2) even if it had arrived, **in Gen 6 the save stores the ability
(`Ability` + `AbilityNumber` 1/2/4) and the party stats**; the ROM does not recalculate them. `SaveUpdater` assigns the
ability with the same number from the new personal entry (species + form) and recalculates party stats
with the Gen 6 formula (box stats are computed by the game when withdrawn). Backup in
`%LOCALAPPDATA%\Pokemanager\save-backups\<emulator>\<date>\main` (last 10 kept), atomic write and verification by re-reading.
Refuses to write if the emulator owning that save is running.

**Verified with real data:**
- The user's Citra save (trainer Puchy) comes from `pokemonXAlex.cxi` = preset `alex` + seed
  `70696688520378` (the log's "Settings String" matches `alex.rnqs`). With that seed the app reproduces the
  ROM and `SaveUpdater` finds 0 changes in its 20 Pokémon. With another seed: 20 changes, valid checksums.
- The stat formula reproduces **exactly** the stats stored by the game for the 6 party members.
- `pack` produces a 1.8 GB `.cxi` in ~30 s; extracted with pk3DS, UPR's 23 files are identical.
- UPR's `new Settings()` leaves `selectedEXPCurve` null and `write()` fails: the launcher uses `defaults()`.
- Misc tweaks UPR supports on X: `FASTEST_TEXT`, `NATIONAL_DEX_AT_START`, `BAN_LUCKY_EGG`, `RETAIN_ALT_FORMES`.

### Second user test (2026-09-13): "types changed, stats and abilities did not"

**Verified root cause:** the headless screenshot harness had saved the user's real
`%APPDATA%\Pokemanager\settings.json` with `EmulatorUserDirectory` pointing to a scratch folder, so the app
adapted a test copy instead of the Citra save. Fixes and rules:
- `AppSettings.Save()` is a no-op unless the instance came from `Load()`; `SettingsViewModel` does not save while initializing.
  **Tests and harnesses must never write the user's settings or real save** (use temp files / copies).
- Base ROM resolution picked `… - random.cxi` (sorts before `.3ds`): `Project.FindBaseRom` prefers `.3ds/.cci`
  and skips files with a `<file>.log` next to them.
- Recent projects are stored in settings (`RecentProjects`) and listed on the start page.
- Rebuilding offers **replace the previous ROM** (`ReplacePreviousRom`, default) to avoid piling up files; the
  randomization cache keeps the last 3 results.
- E2E verified: editing Magneton atk to 190 with the user's seed → exactly 1 save change (44 → 117).

The user confirmed in game that it works (2026-09-13).

### Save editor, history and Pokémon data files (2026-09-13)

User requests: a manual save editor inspired by PKHeX (phase B), a version history "in case the player randomizes the
ROM by accident when they only wanted to change some things", and a `.rnqs`-like file for Pokémon data.

| Piece | Implementation |
|---|---|
| Save editor | `Pokemanager.Save.SaveDocument` (in memory until written) + `SaveEditorViewModel`/`PokemonEditorViewModel`. Everything game-data dependent (abilities by number, EXP growth → level, gender ratio, forms, PP from the ROM's move table, party stats) uses `Session.Current`, **not PKHeX's vanilla tables**. `Check`/`Validate` = "safe for the game" (blocks `Write`); `LegalityAnalysis` is informative only. `Write` refuses when the file changed on disk since it was read (the game saved), backs up, writes atomically and verifies (shared `SaveWriter`). |
| History | `Model/Projects/ProjectHistory`: `<project>.history/<timestamp>/{project.json, version.json, main}`. Kinds: Built, Manual, BeforeRestore, BeforeImport, BeforeSaveEdit. ROMs are not stored (reproducible). Consecutive identical config+kind replaces the newest; `Prune(40)` never deletes Manual. `RestoreInto` copies randomization (enabled, preset, seed) and edits only. Restore in the app: records BeforeRestore, reopens the session, optionally puts the version's save back (`SaveDocument.ReplaceFile`, with backup) and rebuilds (adapting the current save). Building with a different seed/preset/enabled than the newest Built version while a save exists asks first (`Guard_*`). Projects with a built ROM and no history get an initial Built version on open. |
| Pokémon data | `Model/Edits/PokemonDataFile` (`.pkdata`, JSON `table → id → field → value`, magic `pokemanager-pokemon-data`). Scope Edits or All (every value), and **kind** (2026-09-15, user: moves exported apart like the Pokémon): `Pokemon` = personal + learnsets ("Export Pokémon data" in Advanced: Pokémon and the randomizer card), `Moves` = move table ("Export move data" in Advanced: Moves and the randomizer card), **saved as `.mvdata`** (user: so it is not confused with `.pkdata`; same format inside, the importer accepts both), `All` for older files without `kind`. One importer for every kind; "replace" undoes only the edits of the file's kind.
| Move descriptions (2026-09-15) | Table `movetext`, field `description` (a string edit). User: an edited description must show **whatever language the game runs in**, the base is the English one. `GameData.MoveDescriptions` = English text (`GameText.Archive(title, English)`, file `MoveFlavor` of pk3DS's `TextReference`: 15 X/Y, 16 ORAS, 112 SM, 117 USUM), in editable form (`GameText.ToEditable`: `\n` → line break, `\\` `\[` unescaped, `[VAR …]` kept); trailing breaks trimmed. `ModBuilder` writes edited lines into the text GARC of **every language** (`LanguageCount`, missing ones skipped), other lines and files untouched. `TextFile` only needs a `GameConfig` for variable names (none in descriptions). Verified on the real X dump: 618 English descriptions, all 8 languages re-read identical after writing (bytes differ in padding); Alpha Sapphire project: an edit builds `a/0/7/1…8` + the move GARC. Part of the `Moves` data kind (`.mvdata`). |
| Undo / redo (2026-09-15) | `Model/Editing/SnapshotHistory`: after every change the whole state is captured, the previous one kept Brotli-compressed (2000 steps, 512 MB cap, oldest dropped); same-label changes within 900 ms merge; identical bytes add no step. **Project** (`EditorViewModel.ProjectUndo`): `Project.CaptureState()` = sorted edits + randomization + Locke; recorded in `MarkDirty(label)` (session edits are labeled "name · field"); restore applies edit differences through `EditorSession.Set` (views follow), `RandomizationSettings.CopyFrom` keeping `LastBuiltRom`/`InstalledSeed`, Locke replaced, details rebuilt. Randomizer *options* being edited (UPR preset) are not part of it. **Save** (`SaveEditorViewModel.Undo`): `SaveDocument.CaptureState()` (bag copied, `sav.Write()`), restore = `WithState` (new document, dirty unless equal to disk) shown with `Show()` keeping slot and pocket; recorded in `Touch(label)`. Buttons ↶/↷ in the editor bar (count, tooltip with what) and Ctrl+Z / Ctrl+Y / Ctrl+Shift+Z; the Save tab (index 1 of `MainTabs`, bound to `SelectedTab`) uses the save history, every other tab the project's. |
| Pokédex page (2026-09-15) | Save → Pokédex: list (600 px) + the selected species as the game shows it: classification and **this version's entry** from the game text (`GameText.PokedexFiles`: X/Y and ORAS file 2 classifications, entries **7 for X/Omega Ruby, 6 for Y/Alpha Sapphire** — found in the real dumps, pk3DS lists none for Gen 6; SM 116/119-120, USUM 121/124-125, Ultra Sun = 124 by the Pikachu entry), height (cm/100 m) and weight (hg/10 kg), then the ROM's abilities, egg groups, gender ratio, catch rate, growth, hatch cycles and base stats with bars. USUM entries exist only for the Alola dex (others show "no entry"). |
| Roulette and Locke icons (2026-09-15) | `WheelSegment` icon/glyph: item icons (PKHeX), Nugget for money, ♥ life, ✕ nothing, on a white disc near the rim; Poké Ball in the hub. Prize rows and spins show the same icon. **Default lives 10** (`LockeSettings.DefaultMaxLives`). |
| Save Pokémon tab and icons (2026-09-15, second pass) | Left column 404 px with no scrolling (compact party cards and box cells, the problem list takes the rest and scrolls alone); the selected Pokémon in two columns (main data + legality / moves + stats). **Combo boxes empty after selecting another Pokémon** (ability, gender): the reused template replaced the list and the ComboBox dropped its selection while the index had not changed; ability, gender and form are now bound by `SelectedItem` (`SelectedAbility`…, choices are unique), and `RefreshSelections` re-notifies after showing a Pokémon or an edit. Type icons from PKHeX (`Assets/PKHeX/types/type_icon_NN.png`, game type order) and drawn category icons (`PkhexImages.Category`: physical red burst, special blue rings, status grey half circle) in the moves list, the move header (priority badge removed: power, accuracy, PP) and the save's move rows. **Gen 7 box icons** are 64×32 canvases with the same sprite as Gen 6's 40×30 (Ultra Moon: sprite 12 px right, 1 px down) and looked smaller; `PokemonSprites.PokemonBitmap` crops them to the centered 40×30 — **only Pokémon icons**: `ToBitmap` is shared with the Locke badge/stamp images, which the crop had cut and enlarged. Third pass: the stats card is two tabs (`TabControl.inline` in the theme: flush strip, a rule, no page background) — **Stats** (base, bar, nature, value) and **IV / EV** (spinners, EV total, Max IVs / Clear EVs), because the spinners covered the bars; main data rows with 1 px margins, 110 px labels and Shiny next to Gender, so the right column fits 900 px without scrolling. **Modified entries in the advanced side lists**: amber row, orange left bar, bold brown name and an orange "Modified" tag (`Border.entry.modified` in `EditorView`); rows keep 10 px clear on the right because Fluent's scroll bar is drawn over the items (the `ScrollContentPresenter` spans are set in the template, a style cannot change them). |
| Fourth pass (2026-09-15, user screenshots) | **Box last row cut** on short windows (720 px with the orange ROM warning): the party card is docked at the top, the problem list at the bottom (max 150 px) and the box fills the rest; its slots are a fixed 390×248 panel (wallpaper clipped) inside a `Viewbox StretchDirection=DownOnly`, so only the grid scales and the ◀ name ▶ header keeps its size. **Party card type chips cut**: two lines (name · ★ level, then chips), card 50 px. **Disabled NumericUpDown looked cut** (randomizer "stats generation"): Fluent's text box sits 1 px over the spinner border with an opaque background and the disabled border was near the background colour; disabled = transparent text box, border `#BFC5CA`. **Pokédex page**: list 440 px with seen/caught as small toggle flags (`ToggleButton.flag.seen/.caught`: outline off, blue/red filled on — check boxes looked bad on the selected row), count on its own line; header has height/weight badges and the same flags; the red frame around the entry replaced by a plain card with a bar in the type colours; ROM data and base stats in two equal cards with ruled rows (`Border.info-row`), the long hint as a tooltip. **Advanced: Pokémon hides abilities by default** (user: players checking a randomized ROM's changed stats may not want to see them): `EditorViewModel.ShowAbilities` (not persisted, off on every open), "Show abilities" check box in the tab bar; hidden, the header says "Abilities hidden" and the abilities section has a "Show abilities" button; shown, it has "Hide abilities". **Contest conditions** (user: in the stats tab bar): third tab "Contests" in the save's stats card — Cool/Beauty/Cute/Clever/Tough/Sheen 0–255 through PKHeX `IContestStats` (`ContestSmart` = Clever), bars in the condition colours, "All to 255"/"Clear"; shown when the PKM has them (Gen 6/7). **Date picker bug** (Trainer → adventure start): with scroll bars never auto-hiding, the picker's text box (content 1 px taller than its viewport) drew a vertical scroll bar over the date — `TextBox[AcceptsReturn=False]` sets `ScrollViewer.VerticalScrollBarVisibility=Disabled` (the template binds that attached property; affects every single-line box); Fluent's calendar glyph (drawn for 24 px, cut to "15") is hidden and `Path.calendar-glyph` is drawn over the still-clickable button; date/hour/minute widths line up with the play time row. |
| 1.2.1 look (2026-09-15, user) | **"Things that should be the same must be the same"**: `Styles/PokemonLook.axaml` (included by `App.axaml`) holds the Pokémon look for the whole app — styles (`Button.plain`, `Border.member/.cell/.box-frame/.detail-header/.badge/.info-row/.move-row`…) and keyed templates `PartyCard`, `BoxCell` (`x:DataType` `IMonCard`) and `MonSheet` (read-only sheet: type header, stats with bars — base colors when the base is known, neutral blue scaled to the highest stat otherwise; columns without data (base, IV/EV) collapse and the bar is a `ProgressBar.stat-bar` taking the width left (a fixed 110 px bar covered the values in a room's narrow card); layout (user: a 2+1 wrap looked bad): stats and moves side by side with equal height, data below across the width in two columns of pairs; stat labels carry the nature mark (red ▲ / blue ▼, `StatBars.NatureEffect`); rooms share `BaseStats`, `Ivs`, `Evs` since 1.2.2 so their sheet has Showdown colors, IV/EV and the EV total like the preview —, move rows with type/category icons, data rows). `Controls/BoxBrowser` (`IBoxBrowser`: names, index, ◀/▶ commands, wallpaper, cells; slots in a 390×248 panel in a DownOnly `Viewbox`). Implemented by `SlotViewModel`/`SaveEditorViewModel` (save), `PartyMemberViewModel` (+ `Sheet()`, project preview) and `RoomMonCard`/`RoomViewModel` (rooms). **Do not re-add local copies of these styles in views** (local styles win and the looks drift apart). Snapshot additions (optional, older peers ignore them): `SharedBox.Wallpaper`, `SharedPokemon.MoveCategories`, `Ball`. **Tab strips never wrap**: theme sets `TabControl.ItemsPanel` to a horizontal (top) or vertical (left) `StackPanel`; randomizer option groups became `TabStripPlacement=Left` tabs (`TabControl.wide-side`, 260 px). Start screen: "Back to the project" (`CloseRoomPanel`) hides the room panel. **Bag** (user chose "like the games"): pocket strip with a bar in the pocket color under the selected one, list rows (icon or 🔑 when PKHeX has no icon — Gen 6/7 key items —, name, ×N), detail panel with the game's item description (`GameNames.ItemDescriptions` = `TextName.ItemFlavor`) and item/quantity/remove; `SaveBagViewModel.SelectedItem` is set again after a pocket change (the ListBox pushes null when its items are replaced). **Trainer card** (user chose "card on top"): `SaveTrainerViewModel.Card*` (game color gradient per title, profile avatar via `EditorViewModel.ProfileAvatar`, ID, money, Pokédex seen/caught, time, start, Hall of Fame, milestones with `MilestoneIcons` images), refreshed from `SaveEditorViewModel.Touch`. Verified with harness captures (save, preview, room from a snapshot without network, bag, trainer, options). |
| Version 2.0 (2026-09-17, user; the batch they called "3.0", released as 2.0.0) | **Trainer card** redone as the game's: coloured card (game colour), picture, one field per line with a rule (name, ID, money, Pokédex, time, adventure start, Hall of Fame) and the badges along the bottom; the page below it is an `AdaptiveColumnsPanel` so the two columns fill the width (they were two fixed 520 px blocks with a gap on the right). **Roulette**: new prize kind `LockePrizeKind.Text` (free text in `LockePrize.Text`, for prizes the app cannot write: "an evolution item of your choice") and an **Already added** button (`RouletteViewModel.AlreadyAddedCommand` → `LockeRewards.MarkAddedByHand`) that counts any won prize as given without touching the save; `Claim` now only accepts item and money prizes. **History is no longer a tab**: it opens from Settings (`IDialogs.ShowHistoryAsync` → `HistoryWindow`, `SettingsViewModel.History`); `MainTabs` is now Randomizer, Save, Locke, Advanced Pokémon, Advanced Moves (the save is still index 1 for undo). **"The ROM does not have these changes yet"**: a banner over the tabs (`EditorViewModel.NeedsRomBuild`/`NeedsRomBuildText`, refreshed from `MarkDirty`, `LoadLists` and reopens) with a *Build ROM now* button; `RefreshRomNotice` does nothing until the constructor sets `ready` (it ran before `SaveEditor` existed and crashed opening a project — caught by the harness). **Trainer card = the game's card screen** (user, with screenshots of X/Y and ORAS): rows as bands ("Name:", "ID No.:", Pokédex, money, Battle Points, play time, adventure start, Hall of Fame), the trainer picture framed on the right and the milestones as **badge slots** (dark holes, the game's image once earned, faded when not); card stretched to the width so the page has no gap. Poké Miles are not shown: PKHeX's `Pokemiles` is not reachable from `SAV6XY`/`SAV6AO`. **Generation 7 trials all get a picture** (user: the fifth had none, the ROM only carries four seals): `PkhexImages.TrialCrystal(type)` = the Z-crystal of the trial's type (type crystals are items 776–793 in Pokédex type order, checked on the user's Ultra Moon), used for the five trials in the trainer card and in the project preview, faded until earned. Gen 6 keeps the game's badge images. The card also shows **every trial's crystal** (user: "the Normal, Water, Grass ones too"): the eighteen type crystals, bright when the save has them. A crystal is two items — the held piece (776–793, the only one PKHeX has an icon for) and the **bead the bag keeps, held + 31** (807–824; read from the user's Ultra Moon bag: Normalium 807, Fightinium 813, Rockium 819) — so `CrystalViewModel` checks both and draws the held icon; `SaveDocument.HasItem` looks in every pouch. The **five big trial slots** (card and preview) use **the game's own crystal icons** (user: "do not draw them yourself, find the detailed ones, the crystal with the little type symbol inside"): `Model/Data/ItemIcons.LoadTypeCrystals` reads the item icon archive of the imported game (`GameLayout.ItemIcons`; Generation 7 `a/0/6/1`, 769 LZ11 BFLIM of 32×32 — verified on the user's Ultra Moon, the **28 Z-crystals are entries 661–688 and the first 18 are the type ones in Pokédex order**), finds them as the **longest run of entries with the same number of opaque pixels** (168 px; the TM discs make a shorter run of 301) and **trims the transparent border** so they fill their slot. They keep their colour when the trial is not cleared (opacity 0.4): greyed out, every type looked the same. `PkhexImages.TrialCrystalArt(type)` (a drawn gem) stays as the fallback for a game folder without that archive. **Folders imported by an earlier version are completed on opening the project**: `GameImporter.Complete(directory)` re-imports when the manifest lacks a file this version reads and the source ROM is still where it was (`Import` only copies what is missing); anything else leaves the folder alone. **A crystal in the bag shows its icon** (roulette prizes, held items, bag): `PkhexImages.Piece(item)` maps the bead to the held piece PKHeX has a sprite for (type crystals 776–794 → 807–825, Pokémon-exclusive 798–806 → 826–834, Pikashunium 835 → 836; read from the game's item names). **An Avalonia `DrawingImage` belongs to the thread that creates it**, and the project preview is built off the UI thread, so the badge view models take the *type* and draw the image lazily inside `Icon` (a crash caught by the harness; `PkhexImages.Category` was already lazy for the same reason). **Locke changes do not ask for a ROM build** (user: the roulette is outside the game): `MarkDirty(label, affectsRom: false)` for the Locke rules and a `romDirty` flag replacing `IsDirty` in `ProjectDiffersFromBuiltRom` (cleared after a build). Checked in the harness: Locke change → no notice, stat change → notice. **Exclusive Z-moves (Gen 7)** — verified on the user's Ultra Moon: the data is in the personal entry, not in `code.bin` (`PersonalInfoSM` 0x4C `SpecialZ_Item`, 0x4E `SpecialZ_BaseMove`, 0x50 `SpecialZ_ZMove`; Decidueye = Decidium Z + Spirit Shackle → Sinister Arrow Raid, Pikachu = Pikanium Z + Volt Tackle, Mimikyu = Mimikium Z + Play Rough). New personal fields `zCrystal`, `zBaseMove`, `zMove` (they read 0 and write nothing on Gen 6, with a test) and a card "Exclusive Z-move" in Advanced: Pokémon shown only when the entry is a Gen 7 one, so a run can point a crystal at a move the Pokémon actually learns. |
| Consistency pass (2026-09-15) | Two-type colors: hard diagonal cut (both gradient stops at 0.5). Decorative corners 2 px everywhere (avatars stay round); move rows rectangular. Theme: NumericUpDown text vertically centered with white background, disabled spinner arrows light instead of grey blocks, scroll bars never auto-hide (the thumb reached the top only after hovering), side tab strips fixed size (selected tab no longer grows/shifts), CalendarDatePicker button inside the box, Fluent TimePicker (did not fit the classic height) replaced by hour/minute spinners. Visual checks: harness renders each page with `RenderTargetBitmap` in its own process (a second render in the same process came out corrupted) on copies of a project and save. |
| Save file look (2026-09-15) | Same style applied to the save editor. **Box like PKHeX**: the box's wallpaper from the game (`SaveDocument.BoxWallpaper` → `IBoxDetailWallpaper`) drawn behind a 6×5 grid of sprites, ◀ name ▶ header; PKHeX's rule for Gen 6/7: X/Y wallpapers, ORAS's own for numbers 17–24. Party as type-colored cards (sprite, held item icon, level, gender, shiny, type chips). Selected Pokémon: type-colored header (nickname, types, level · nature · ability, ball and item icons, base stat total), cards for main data (item and ball icons by the combos), moves colored by their ROM type with type/category, stats with base bars and PKHeX's nature colors (▲ red raised, ▼ blue lowered). Bag: pockets with a color and a representative item, header in the pocket color, items as cards with their icon. Images from PKHeX (GPL-3) in `App/Assets/PKHeX` (`box/`, `items/bitem_*`, `balls/_ball*`, `SOURCE.txt`, credited in `NOTICE`), loaded by `PkhexImages`; PKHeX has no Gen 6/7 key item icons (they show its unknown icon). **Bug fixed**: the box combo box showed nothing while box 1 was displayed — clearing `BoxNames` made the ComboBox push -1 into `SelectedBox`; `Open` now keeps the box, refills, reselects and notifies. |
| Advanced tabs look (2026-09-15) | Header in the type colors (`TypeColors`, two halves for two types) with sprite, type chips, abilities and number badges (BST; power/accuracy/PP/priority and Showdown category colors for moves), sections as cards in a `WrapPanel`, base stats with Showdown-colored bars (`StatFieldViewModel`), learnset rows colored by move type, a type strip in the side lists. | Import goes through `EditorSession.Set`, so values equal to the base do not become edits; unknown fields/ids are skipped and reported. |

Verified end to end on the real dump with a copy of the user's save (headless harness, temp settings): edit and write
Magneton (+1 level, EVs, move), EV total > 510 blocks writing, create Pikachu in an empty box slot, bag/trainer/Pokédex;
build with seed 12345 asks first and changes 21 things in the save; restoring the first version rebuilds seed
70696688520378 and Magneton's ability goes back (141 → 26); `.pkdata` export/import round-trips Magneton atk 190.
PKHeX legality reports randomized Pokémon as illegal (EncInvalid, AbilityUnexpected), as expected.

**Bug found by the user and fixed:** choosing the hidden ability reverted at once. Each edit re-created the ability
choices list; the ComboBox reset its selection when its `ItemsSource` changed and wrote index 0 back (ability 1).
`PokemonEditorViewModel` keeps list instances while their contents are equal and ignores values pushed by controls while
it refreshes after an edit. Reproduced and verified with real controls in the headless harness (tab/slot switches,
write, reread). **Rule: never return a new collection from a property bound to `ItemsSource` next to a two-way
`SelectedIndex` unless the contents changed.**

Trainer tab extended like PKHeX's X/Y trainer editor (`SaveDocument`: Mega Evolution flag, Vivillon, boxes unlocked,
PR Video phrases, start/Hall of Fame dates (seconds since 2000-01-01), records with PKHeX names, Maison streaks,
O-Power points and unlock-all, Friend Safari, fashion, Super Training, Poké Puffs, map position with a warning).

### Layout, spoilers, language and sprites (2026-09-13)

- **No spoilers unless asked:** the UPR log is no longer on the Randomizer page; "Show randomization log (spoilers)…"
  opens `LogWindow`. The per-Pokémon save changes after a build are inside a collapsed expander.
- **Responsive:** `Controls/AdaptiveColumnsPanel` puts cards (`Border.card`) in 1–3 columns depending on width; the
  Randomizer build page is an action bar (enable + build button always visible) plus three cards.
- **Language:** game texts follow the interface language (`Services/GameTextLanguage`), not `Project.Language` (kept
  only for old files). The user's project had Spanish game text with an English UI. Default box names ("Caja 1") are
  shown localized.
- **Sprites — verified:** box icons are in `romfs/a/0/9/3` (945 LZ11 BCLIM, 40×30). Pixel data: `0x0002`, color count,
  RGB5A1 palette, then 1 byte per pixel or a nibble **high first** for ≤16 colors (pk3DS uses `< 16`: wrong for 16), 8×8
  Morton tiles. The icon index is **not** the species: `code.bin` (Pokémon X at file offset 0x43EA98, found by content)
  has a 16-byte entry per species: default icon, female icon, pointer to per-form icons, pointer to a second per-form
  list (shiny), counts. Pointers are addresses with base 0x100000. `Model/Data/PokemonIcons`, `App/Services/PokemonSprites`.
  Examples: Pikachu 29, Charizard 9, Mega X 7, Mega Y 8, Unfezant ♂/♀ differ.

### Classic look and ▶ Play (2026-09-13)

- `Styles/ClassicTheme.axaml` on top of `FluentTheme DensityStyle="Compact"`, Light variant: PKHeX/UPR ZX look (grey
  window, white tab pages, square 23 px controls, WinForms-style tabs, blue selection, 12 px Segoe UI, `Border.card` as
  group boxes). **Fluent template values set with TemplateBinding/local values (e.g. TabItem `PART_SelectedPipe`
  visibility and height) cannot be overridden by styles**: use `Opacity` or the theme resource
  (`TabItemHeaderSelectedPipeFill`). Template-part selectors (`/template/ ContentPresenter#PART_ContentPresenter`) do work.
- ▶ Play (`EditorViewModel.Play`): opens the last built ROM in the emulator program. `Bridge/EmulatorExecutables`
  detects it among running processes, usual install folders and hints: the ROM folder and the folders the emulator
  remembers in `qt-config.ini` (`Paths\gamedirs\N\path`, `romsPath`, `recentFiles`), each with its parent (never a drive
  root), one level deep. Verified: finds the user's portable `D:\citra\citra-windows-msvc-20240303-0ff3440\citra-qt.exe`
  from `gamedirs` `D:/citra/roms` in ~25 ms. Can be chosen in Settings (`AppSettings.EmulatorExecutable`). Warns when the
  project has unbuilt changes; refuses with unwritten save edits or the emulator already open.

### Project browser (2026-09-13)

The start screen is now a browser: project list (each with Manage / ✕) and, for the selected one, a read-only preview
(`ProjectPreviewViewModel`): ROM, seed, trainer summary and the party (sprite, name, level, item); a party member shows
stats with base/IV/EV, moves with PP, ability (slot), nature, item, friendship, ball and OT. Loading runs off the UI thread
through `Services/ProjectLoader` (project + dump + session + save, ~0.5 s); **Manage** reuses that load
(`MainWindowViewModel.Manage(loaded, slot?)`), and "Manage this Pokémon" opens the editor on the Save file tab with that
slot selected (`EditorViewModel.PendingSaveSlot`, handled in `EditorView` on `DataContextChanged`).

### Locke: badges, lives and the badge roulette (2026-09-14)

- `Model/Projects/LockeSettings` (in the project JSON, defaults for old files): `MaxLives`/`LivesLost`, weighted
  `Prizes` (Item, Money, Life, Nothing) and `Spins` (one per badge; `Claimed = false` while an item/money prize is not in
  the save yet). Editor tab **Locke** (`LockeViewModel`); "Allow again" = `ForgetSpin`.
- Preview: badges from `SaveDocument.GetBadge` (Kalos order Bug, Cliff, Rumble, Plant, Voltage, Fairy, Psychic, Iceberg;
  gym types 6, 5, 1, 11, 12, 17, 13, 14 for the colors), hearts with −/+.
- `RouletteWindow` + `Controls/WheelControl`: the winner is rolled **before** the animation (`RequestAnimationFrame`,
  4.2 s ease-out) and recorded when it stops; closing mid-spin records it at once, so closing never gives a re-roll.
  `Services/LockeRewards.Claim` puts items (`SaveDocument.GiveItem`, right pouch, stacks, capped) or money into the save
  with the save editor's safety: emulator closed, BeforeSaveEdit history version, backup, verification.
- Verified headless on a copy of the user's save (1 badge): Rare Candy 754 → 757, money pending then claimed
  52 860 → 62 860, extra life 3/5 → 4/5.
- **Badge icons — verified:** the trainer card layout `a/1/1/3` file 0 (LZ11) is a darc with `badge_01.bclim` …
  `badge_08.bclim` (RGBA8; 55×64, 55×77, 70×45, 50×55, 64×64, 90×34, 60×60, 55×60) and ETC1A4 `badge_0N_base`
  silhouettes. `Model/Data/BadgeIcons` (darc reader); unearned badges are drawn greyed and faded. BCLIM textures are
  **not always square** (55×77 is stored as 64×128): `DecodeBclim` sizes the pixel data as NextPow2(w)×NextPow2(h).
- `POKEMANAGER_DATA` moves `AppSettings.DataRoot` (cache + save backups). **Harnesses that write a save must set it**: a
  harness run had put a backup of a test copy into the real `save-backups` folder (removed; nothing was pruned).

### Every 3DS Pokémon game and projects from a ROM (2026-09-14)

User request: detect the game from the chosen ROM and adapt settings, badges/trials and Pokémon to it; keep the game data in
Documents\Pokemanager, created automatically, so only the ROM has to be chosen. **The original X/Y-only design above
(dump folder with `romfs`/`exefs`, file-count detection) is superseded for new projects; old projects still open.**

| Piece | Implementation |
|---|---|
| Games | `Model/Dump/GameTitle`: X, Y, OmegaRuby, AlphaSapphire, Sun, Moon, UltraSun, UltraMoon → title ID, family (XY/ORAS/SM/USUM), generation, pk3DS version (SN/MN/US/UM exact, so Sun/Moon need no `encdata` file), `Layout()` (archives) and `Milestones()`. |
| ROM reading | `Model/Dump/RomReader`: reads single files from a decrypted NCSD (`.3ds/.cci`) or NCCH (`.cxi`) without extracting: NCSD partition 0, NCCH program ID 0x118, flags 0x18F bit 2 = no crypto (encrypted ROMs are refused with a message), ExeFS (`.code` BLZ-decompressed when the exheader says so), RomFS IVFC level 3 directory/file tables. |
| Import | `GameImporter.Import(rom, Documents\Pokemanager\Games)`: copies personal, moves, levelup, evolution, game text in every language (8 in Gen 6, 10 in Gen 7), the icon archive and the badge/trial image archive (first candidate that has them), `code.bin`, `icon.bin`, `exheader.bin` and writes `pokemanager-game.json` (game, source ROM path/size/date). Reused when the same ROM file is imported again. X 23 MB, ORAS 28 MB, USUM 45 MB, in 0.2–0.3 s. `GameDump.Open` trusts the manifest; folders without one use the old X/Y inspection. |
| Data | `GameData.Load(layers, game)`: personal 0x40 XY / 0x50 ORAS / 0x54 Gen 7 (all `PersonalInfoXY` subclasses), moves one file each in X/Y and a `"WD"` mini archive elsewhere (`Move6`/`Move7`), `Learnset6` for all. `ModBuilder` repacks with the source GARC's version (VER_4/VER_6) and padding, and the mini archive for moves. |
| Archives (verified X, AS/OR, UM) | personal/move/levelup: XY `a/2/1/8,2,4`; ORAS `a/1/9/5`, `a/1/8/9`, `a/1/9/1`; Gen 7 `a/0/1/7`, `a/0/1/1`, `a/0/1/3`. Icons: XY `a/0/9/3` (945, BCLIM 40×30), ORAS `a/0/9/1` (974), USUM `a/0/6/2` (1154, **BFLIM** 64×32, orientation 4 = rotated). The species→icon table in `code.bin` is found by the same 16-byte-entry search in every game (808 species in USUM). Badges: ORAS `a/1/0/9` file 0 darc `badge_01..08`. Trials: USUM `a/2/4/2` file 4 = ALYT with a SARC, `Shiren_Stamp_00..03` (Tapu seals, ETC1A4 256×256). **Sun/Moon archives are not verified** (no ROM available): candidates are tried and missing images are simply not shown. |
| Images | `DecodeBclim` also reads BFLIM (format/orientation at footer 0x22/0x23) and ETC1/ETC1A4 (4×4 blocks, four per 8×8 tile in Z order, block = little-endian u64, colors/flags high, indexes low column-major; alpha nibbles column-major). `MilestoneIcons` reads darc and SARC. |
| Save | `SaveDocument` works on `SaveFile`/`PKM`: SAV6XY, SAV6AO, SAV7SM, SAV7USUM (size fallbacks 0x65600/0x76000/0x6BE00/0x6CC00 for saves PKHeX does not detect). Milestones: Gen 6 `Badges` bits 0–7; Gen 7 `Misc.Stamps` bits 1–5 (PKHeX `Stamp7`: Melemele…Poni trial, island challenge). Game-only options flagged (`HasGen6Extras` Maison/O-Power/Super Training/Puffs, `HasXYExtras` Friend Safari/fashion, `HasSayings`, `HasPosition`, `HasZMoves`). Stat formula counts Hyper Training (Gen 7) as IV 31. **Gen 7 bags keep used-up items with count 0** (written by the game): only Gen 6 rejects count 0. |
| Randomizer | The launcher tries `Gen6RomHandler` then `Gen7RomHandler`; packing already goes through `Abstract3DSRomHandler`. Totem/ally/aura options are a "Totems" group shown only for Generation 7. |
| App | New project = ROM + name (`WelcomeViewModel`): detection on selection, warning when the ROM has a `.log` next to it, import off the UI thread, project in `Documents\Pokemanager\Projects`. `POKEMANAGER_DOCUMENTS` moves `Documents\Pokemanager` (**harnesses must set it**, like `POKEMANAGER_DATA`); `POKEMANAGER_SETTINGS` moves `settings.json` (player id, room memory), so a second instance on the same PC is another player. Locke roulette interval and preview use the game's milestones (5 in Gen 7); trainer tab shows badges or trials and hides what the game lacks. |

Verified on the user's real ROMs and copies of their saves (headless harness, Documents/data redirected to G:):
- Ultra Moon (`ULRandomNico.cxi`): project created in 1.7 s; preview with 6 party sprites and trials 2/5 (Melemele, Akala seals);
  roulette claim wrote 2 Rare Candies (328 → 330); UPR ZX Gen 7 randomization 10 s, edits on top, 3.5 GB `.cxi` packed in
  41 s and re-read with both the randomization and the edits; the save with the ROM it was played on: **53 Pokémon, 0 ability
  or stat changes** (formula matches the game in Gen 7).
- Alpha Sapphire: badges 0/8 with the Hoenn badge images; save + `pokmeonzaAlexAlba.cxi` 0 changes, + another AS ROM only the 2
  expected ability changes. Omega Ruby and X import and read correctly; the encrypted `PokemonPEv2.3ds` is refused.

### Installer, GitHub and branches (2026-09-14)

- **Branches: `main` is the stable, public branch (releases are built from it). From now on all work goes to `dev`**, merged
  into `main` for a release, until the user says otherwise.
- Installer: `build/build-installer.ps1 -Version X.Y.Z` → `dotnet publish` self-contained win-x64, `javac --release 17` of
  the UPR launcher into `<app>/upr` (the runner uses the `.class` when present, the source launcher otherwise), `jlink` runtime
  in `<app>/tools/java` (jdeps modules + `jdk.charsets,jdk.zipfs,jdk.localedata`, 58 MB; preferred by `UprLocator`), then
  Velopack `vpk pack` (local tool in `dotnet-tools.json`) → `artifacts/installer/PokemanagerApp-win-Setup.exe` (~102 MB) and a
  portable zip. **Pack id `PokemanagerApp`, not `Pokemanager`**: Velopack installs to `%LOCALAPPDATA%\<packId>` and deletes it on
  uninstall, and `%LOCALAPPDATA%\Pokemanager` holds the save backups. `Program.Main` calls `VelopackApp.Build().Run()` first.
- Verified locally: silent install → desktop and Start menu shortcuts, the installed app opens ("Pokemanager 1.0.0"),
  the bundled Java randomizes Ultra Moon without any system JDK, silent uninstall removes the app and keeps the backups.
- GitHub (public repo `Marpuchy/pokemanager`): `.github/workflows/ci.yml` builds and tests on push/PR to main and dev;
  `release.yml` on a `v*` tag runs tests + the installer script (Temurin 21 JDK) and publishes the release with
  `docs/release-notes.md`. Version comes from the tag (`Directory.Build.props` has the default) and shows in the window title.

### Multiplayer rooms (2026-09-14) — live over the internet, no server

**Redesign (user, same day) — overrides the rules engine committed in `207987a`:** no automatic Locke rules (links, deaths,
lives, notices: players manage them as they like). What they want: **join another player's room without files, by code; live
updates; see the other players' party and boxes unless the room's rules say otherwise.** Players are always on different networks;
no server and nothing for the user to set up. The removed engine (LinkRules/RoomState/SaveSync…) is in git history.

| Piece | Implementation |
|---|---|
| Transport | **LiteNetLib 2.1.4** (MIT, NuGet): UDP, reliable ordered, fragmented. Star: the host's app is the hub and relays. `RoomSession` runs its own loop thread (work queue + `PollEvents`), raises `Changed` there; UI marshals with `Dispatcher.UIThread.Post`. |
| Codes | `InviteCode`: `PM-…` = version, 10-byte secret, endpoints (v4/v6 + port), 2-byte SHA-256 checksum, Crockford base32 in groups of 5 (~60 chars). `PMR-…` answer = 4-byte room tag + the guest's endpoints. Typos are rejected. |
| Security | Connection key and AES-GCM key derived with HKDF from the secret; messages JSON → Brotli → AES-GCM (`MessageCodec`). |
| Reachability | Before LiteNetLib binds the port: **STUN** on that same socket (Google/Cloudflare). Then **UPnP** (SSDP + SOAP AddPortMapping, 2 h lease, fallback 0; removed on dispose, renewed every 30 min) or **NAT-PMP**. Endpoints offered: mapped public, STUN, LAN IPv4, global IPv6. Guest connects to all; if nothing after 8 s → `WaitingForAnswer` and shows its answer code; the host pastes it and sends unconnected packets to those endpoints for 45 s (**hole punching**) while the guest keeps retrying. Both behind symmetric NAT/CGNAT: no way without a relay (not implemented). |
| Protocol | `Hello` (guest) → `Welcome` (room, rules, players) + stored snapshots; `PlayerJoined`/`PlayerLeft`; `SnapshotShared`; `RulesChanged` (guests re-share; host re-filters what it holds). `RoomRules(ShareParty, ShareBoxes)` applied by the sender, the host relay and on rule changes. |
| Data | `TrainerSnapshot.Read(SaveDocument)`: trainer, game, milestones, money, time, party and every box (species, form, gender, shiny, egg, nickname, level, item, ability, nature, moves, stats HP/Atk/Def/Spe/SpA/SpD and types from the played ROM, current HP in the party). Viewer names ids in its own language. |
| Live | `SaveWatcher`: FileSystemWatcher + 0.5 s poll of size/write time, fires after 1.5 s quiet → re-read the save (read-only) and publish. **Live = every in-game save**; unsaved battle state would need the emulator RPC. |
| App | **On the start screen, not in the editor** (user, same day): a Multiplayer card under the project list (avatar, profile name, room summary) opens the room on the right side. `RoomViewModel` belongs to `MainWindowViewModel` and lives with the app (opening a project keeps the room; window closing calls `CloseForExit`, which also removes the router mapping). The shared save is the one of the project selected when the room starts (`SelectedProject` provider from the welcome page). Last room kept in `AppSettings.Room` (player id, host secret + port so the invite code survives restarts, or the guest's invite, project path). Look copied from the project preview: type-colored party cards, detail header, moves colored by the owner's ROM types with PP, boxes as type-colored tiles. |
| Profile | `AppSettings.Profile` (name — empty = save trainer name —, Showdown trainer sprite, own picture reduced to ≤96 px PNG ≤64 KB, accent color), edited in **Settings**; sent as `PlayerProfile` in `Hello`/`PlayerJoined`, live changes with `ProfileChanged` (protocol 2). `TrainerSprites`: index scraped from `play.pokemonshowdown.com/sprites/trainers/` (~1500, cached a week), search by words, PNGs downloaded on demand into `<data>/trainer-sprites`. `AvatarViewModel` (image → sprite → initials on the color) is an app-wide DataTemplate. |

**Verified:** 3 sessions over real UDP on one computer (tests): joins, live relay, late joiner gets earlier saves, hiding/showing
boxes, leave detection; answer code requested when the host is unreachable; codec rejects other rooms and tampering. On the user's
network (laptop, 2026-09-14): STUN works (public 79.117.103.22), **no UPnP/NAT-PMP answer from the router** → friends elsewhere will
likely need the answer-code step unless UPnP is enabled; the NAT did not always keep the local port. Headless screenshot with the
app hosting on copies of the Ultra Sun project/save and a second session joining over UDP.
Windows Firewall asks the first time the app listens (allow it).

Pending: relay through another player, host migration, live dashboard (RPC).

### Battles between players (2026-09-14) — phases 1–3 of 4 done: simulator with each ROM's data, room flow, battle screen

User request: battles between room players using **each player's own ROM data** (randomized stats/types/moves). Decisions:
**Pokémon Showdown's simulator run locally** (npm `pokemon-showdown` 0.11.11, MIT, Node.js), not the public server (it cannot
take custom data) and not an own C# engine (far too big). Everything is **chosen by the players before each battle** (levels,
mechanics generation, megas, Z-moves, clauses…); the team is each player's **party**. Plan: (1) simulator + ROM data, (2)
challenge flow in the room: the host runs the simulator and relays choices, (3) battle screen, (4) spectators/replays/animations.
**Bundled since 1.2.0** (`build-installer.ps1`): `npm ci` in `battle/`, then `battle/` = launcher + `pokemon-showdown`
`package.json`/`LICENSE`/`dist/{sim,data,lib,config}` + `ts-chacha20` (its only runtime dependency, found by running a battle on
the pruned copy; 95 MB) and the build machine's `node.exe` in `tools/node`; `build/check-battle.js` plays a random battle with
them or the build fails. Installer 140 MB. Verified: the Showdown and room battle tests (7) pass against that exact copy.
Release workflow installs Node 20. On Windows PowerShell 5.1 pass `-JdkHome` (the java fallback writes to stderr).

| Piece | Implementation |
|---|---|
| Launcher | `battle/pokemanager-battle.js` (`battle/package.json`, `npm install` there; `node_modules/` ignored). JSON lines on stdin/stdout: `start` (rules, two teams, seed), `choose` (side, Showdown choice), `forfeit`; out `update`, `side` (requests/errors), `end`, `invalid`, `error`. |
| ROM data per side | A rule of our own, `Pokemanager ROM Data`, in `Dex.data.Rulesets`: `onModifySpecies` returns a clone with the side's base stats/types (key species num + form index in `formeOrder`, so megas and formes use the ROM too), `onModifyType`/`onModifyMove`/`onModifyPriority` apply the side's move type, power, accuracy, category and priority only where the ROM differs from vanilla and the move did not change itself (Hidden Power, Weather Ball…), `onBegin` sets PP (and HP when not healing). No renamed species: protocol names stay canonical. |
| Ids | Showdown's `num` = game id for species, moves, abilities, items; natures and types converted from game order; form = index in `formeOrder`. |
| Rules | `BattleRules` → `gen6/gen7customgame@@@…`: Sleep Clause Mod, Species Clause, `Item Clause = 1` (needs a value in this version), OHKO, Evasion Moves, Baton Pass, Z-Move Clause, `-Mega`, `!Team Preview`, Endless Battle always. **Level is set on the sets** (Showdown's Adjust Level only acts in its validator). **Clauses/bans are validator rules**: the launcher runs `TeamValidator` first and answers `invalid` with Showdown's problems instead of starting. |
| C# | `Pokemanager.Battle`: `BattleTeam.FromParty(SaveDocument)` (mons with IVs/EVs/PP/ability/item + ROM `BattleSpecies` for every form and `BattleMove` data), `ShowdownTools.Locate` (bundled `tools/node` or PATH; `battle/` upwards), `ShowdownBattle` (process, `Received` events). |

**Verified:** 12 Pokémon of the user's real Ultra Sun party with the original ROM and the randomized ROM: Showdown's stats equal
the game formula for all (that randomization did not change stats/types/moves). With data changed in memory for one side only
(species → pure Water, base HP 200/Spe 5; a move → Grass 90): that side's HP 450, its lead moved second, the same move was
"resisted" for p1 (Fire) and "super effective" for p2 (Grass); random battle ran 47 turns to a winner. Tests: team building,
three rule sets start with matching stats (Gen 6 + level 50, many clauses), Species Clause refuses a team.

**Phase 2 done — battles in the room (no UI yet).** `RoomSession.Battles.cs`, protocol 3. Player → host: `BattleChallenge`
(id, opponent, first rules), `BattleRulesSet` (either player; clears both "ready"), `BattleReady` (team or null), `BattleCancel`
(cancel or forfeit), `BattleChoice`. Host → players: `BattleState` (phase Proposed/Running/Finished/Cancelled, ready ids, winner
player id, problems), `BattleLog` (lines already reduced with `ShowdownProtocol.ForSide`: `|split|pN` + exact line for that side +
public line), `BattleRequest`. The host runs every battle of the room, also between two guests; its own player goes through the
same handlers (`Deliver`). p1 = challenger. A team that breaks the rules sends the battle back to Proposed with the reasons; a player
leaving forfeits (running) or cancels (proposed); a host without the simulator (`RoomOptions.Battles` null) refuses with a reason.
The app host passes `ShowdownTools.Locate()`.
**Found:** Custom Game is a `debug` format, so Showdown reports everyone's exact HP (`reportExactHP = !!format.debug`); the rule's
`onBegin` sets `reportExactHP = false` / `reportPercentages = true`, plus `HP Percentage Mod`. Showdown also announces
`-start … typechange` when a side's ROM types differ from vanilla (the UI can use it). Tests: two guests negotiate, a clause refuses,
battle to the end over UDP with the same winner and turns for both, own exact HP vs opponent percentage; host battles and a guest
leaving loses; a room without simulator refuses.

**Phase 3 done (2026-09-15) — battle screen and rules in the app.** User: battles "as if we took Showdown and put it in our app",
teams = the connected save's party, rules set before the battle.

| Piece | Implementation |
|---|---|
| Battle screen | `BattleWindow` = Avalonia **`NativeWebView`** (`Avalonia.Controls.WebView` 12.1.0, MIT; WebView2 on Windows, profile in `<data>/webview`) loading `App/BattleScreen/battle.html` (Content, copied to the output). The page loads **Showdown's battle engine from play.pokemonshowdown.com** exactly as its replay embed does (`battle.js`, `battledata.js`, `battle-tooltips.js`, data, CSS: the engine `battle-*` is MIT, the client as a whole is AGPL, so **nothing of the client is copied into the repo**; `battle-room.js` is our own: move/switch/team-preview menus with Showdown's CSS classes, mega/Z/Ultra Burst, forfeit, sound). Needs internet (a room does anyway); offline the page says so. The folder is `BattleScreen`, not `Battle`: on Windows it would be the same folder as the simulator's `battle/` once bundled. |
| Page protocol | App → page: `PM.init({side, moveTypes, muted, strings})` (move num → ROM type index of the player's team, shown on the buttons; UI text from resx), `PM.add(lines)`, `PM.request(json)`, `PM.finish(text)`. Page → app (`chrome.webview.postMessage`, arrives as a JSON string in `WebMessageReceived.Body`): `ready` (page (re)loaded: the view model resends everything), `choose`, `forfeit`, `mute`. Controls show once the engine reaches the end of its queue (`subscribe('atqueueend')`) so they never appear before the turn's animations; `battle.setViewpoint(side)` for p2; `battle.myPokemon` = the request's party (tooltips). |
| Room UI | Battles card in `RoomView`: "Challenge {player}" (default mechanics = newest generation of the two), one `RoomBattleCard` per battle: generation, level (saves/50/100), full HP/PP, team preview, clauses, no megas/Z — two-way bound, any change sends `SetBattleRules` (both players unready). "Ready with my party" reads the shared save now (`BattleTeam.FromParty`). The battle window opens by itself when the battle starts; "Open battle" reopens it; cancel/forfeit (asks); close when over. |
| Host | Distinct simulator names (same profile name → "Name 2") and the winner mapped by those names; players' Showdown trainer sprites passed as `avatar` (`|player|p1|Oak|red|`). |

Verified: the page in headless Edge with a generated gen 7 battle (field, sprites, log, choice-locked moves disabled, team preview,
p2 viewpoint, result) and the real `BattleWindow` in a harness (WebView2 loads Showdown, `ready` reaches C#, Spanish strings,
a click sends `choose`, finish shows). Second instance for testing on one computer: `POKEMANAGER_SETTINGS` + `POKEMANAGER_DATA` +
`POKEMANAGER_DOCUMENTS` (another player id), app built to `artifacts/guest`.

**In-game link through the emulator (2026-09-15, built).** The user's Citra nightly (`citra-windows-msvc-20240303-0ff3440`) has
`citra-room.exe` and the SDL `citra.exe` accepts `-m nick:password@address:port <rom>` (joins at start); `citra-qt` has no such
option (Direct Connect by hand). **`citra-room` refuses to start without `--preferred-game`** (and takes `--preferred-game-id`).

| Piece | Implementation |
|---|---|
| Tunnel | `RoomSession.Emulator.cs`, protocol 4: `EmulatorRoomOpened(EmulatorRoomInfo: name, password, generation, game)` / `EmulatorRoomClosed` (also sent to late joiners after `Welcome`). Emulator UDP packets travel raw, AES-GCM sealed (`MessageCodec.Seal/Open`), **unreliable when they fit, `ReliableUnordered` otherwise**. **LiteNetLib drops the channel number of unreliable packets**, so a packet is a tunnel packet when its delivery method is not `ReliableOrdered` (room messages always are). Host: one `UdpClient` on 127.0.0.1 per guest peer towards the room server (it tells guests apart by source port). Guest: a local socket on 127.0.0.1; the emulator's address is learned from its first packet. Windows `SIO_UDP_CONNRESET` off. |
| Server/launch | `Bridge/EmulatorRoomServer`: `LocateServer`/`LocateJoinProgram` next to detected emulator programs (`citra-room`/`azahar-room`/`lime3ds-room`; `citra.exe`), `Start` (free UDP port, random password, preferred game + title id, log in `<data>/emulator-room.log`, stdin kept open, `/exit` then kill on dispose), `LaunchInRoom`, `CleanName` (accents dropped, 4–20 allowed chars). |
| App | "Play inside the game" card: the host's app **opens the emulator room by itself once another online player has a game of the same generation** (once; closing it by hand stops that), or by hand; "Play in the emulator room" starts `citra.exe -m …@127.0.0.1:<port>` with the project's built ROM (refuses while an emulator is open); Direct Connect details for Qt. |

Tests: tunnel echo through host + two guests (late joiner told, 1400-byte packets, one server client per guest, close for all);
sealed packets reject other rooms and tampering. Verified `citra-room` starts, listens and exits with the class. Not verified yet:
two real emulators linking in game through the tunnel. In-game battles between different randomized ROMs may desync (each console
computes with its own data); trades should work. Azahar's room program and command line not checked.

### Version 3.0 (2026-09-17, in progress) — inside the game: trainer AI and difficulty

Branch **`feature/game-ai`** off `dev`. The user's goal for this version: touch the game's internals, and above all let
the player make the adventure easier or harder by changing the trainer AI. Full analysis in **`docs/game-ai.md`**
(where the AI lives, what the real data says, the levers and five proposed models); the short version:

- **Verified on the real X dump**: the AI a trainer uses is one **bitfield byte** in its `trdata` record (X/Y `a/0/3/8`,
  team in `a/0/4/0`; ORAS `a/0/3/6`+`a/0/3/8`, SM `a/1/0/5`+`a/1/0/6`, USUM `a/1/0/6`+`a/1/0/7`). X uses only bits 0, 1,
  2 and 7: `0x07` (Basic+Strong+Expert) for **every gym leader, the Elite Four and the Champion**, `0x00`/`0x01` for
  filler trainers. **`0x07` is the game's own ceiling** — "harder AI" cannot be more flags; "easier" can (561 of 784
  trainers are above the floor). Bit 7 (pk3DS calls it *UseItem* in Gen 7) matches neither doubles nor carrying items:
  unresolved.
- **Verified**: the difficulty is mostly *not* in the AI byte. The IVs byte (0–255, pk3DS reads `IVs = value/8`) is 150
  for leaders and 200 for Diantha, with **no EVs or natures at all in Gen 6**; only 269 of 784 trainers define their own
  moves (the rest get their last four level-up moves, which is why randomized trainers are harmless); 59 trainers carry
  a healing bag (Diantha 4 Full Restores).
- **Verified**: the routines that decide the moves are **not in `code.bin`** but in a romfs CRO module —
  Gen 6 `DllBattle.cro` (1 040 384 B in X), Gen 7 `Battle.cro` (UPR's `gen6/gen7_offsets.ini` name them). Patching one
  means reverse engineering plus a CRR re-hash (pk3DS has `CRO.E_HashCRR`).
- **Trainers are not in the save**, so difficulty changes need a ROM rebuild and never a save adaptation.
- The pipeline gains a stage: **`dump → randomization → difficulty rules → manual edits → build`** — the rules run after
  UPR (which rewrites whole teams) and before manual edits, so a hand-edited trainer still wins.

**Order agreed with the user:** first the plumbing and the per-trainer editor (model D of the document), because the
difficulty profiles (A), the level curve (B) and the rival that follows the save (C) are only generators writing through
it; the engine patch (E) stays a spike.

| Step | State |
|---|---|
| Trainers in the layout and the importer | **Done.** `GameLayout.TrainerData`/`TrainerPokemon` (XY `a/0/3/8`+`a/0/4/0`, ORAS `a/0/3/6`+`a/0/3/8`, SM `a/1/0/5`+`a/1/0/6`, USUM `a/1/0/6`+`a/1/0/7`), copied by `GameImporter`. `Complete` now re-imports whenever **any** required file is missing from the manifest (it only looked at the Gen 7 item icons), so game folders imported by 2.x pick the trainers up when the project is opened. |
| Reading them | **Done.** `Model/Data/Trainer.cs`: `Trainer` + `TrainerMon` over `TrainerData6`/`TrainerData7`, and `TrainerArchive.Read` → `GameData.Trainers`. Differences are not hidden: Gen 6 has the IVs byte and no EVs/nature, Gen 7 has six IVs, six EVs, nature and shiny; **a field the generation lacks reads 0 and writes nothing** (same rule as the Z-move fields). A record that does not parse (the archive's dummy entry, a team that does not match) is an `Unreadable` **pass-through**, written back untouched. |
| Editing them | **Done.** Table `trainer` in `GameTables`: `ai`, `class`, `battleType`, `money`, `flag`, `customMoves`, `heldItems`, `count`, `item1…4`, and the team as `p1.` … `p6.` prefixes (`species`, `form`, `level`, `ability`, `gender`, `item`, `move1…4`, `ivs`, `nature`, `shiny`, `iv*`, `ev*`). Writing a slot the trainer does not have **grows the team**. `ModBuilder` repacks both archives whenever the table has edits. |
| Verified on the real X dump | 785 trainers read; **every one round-trips byte for byte** (which exposed an upstream pk3DS bug: `TrainerData6.Write` dropped an ORAS field — fixed in the fork, see `docs/upstream-pk3ds.md`). Korrina `ai` 0x07 → 0x00 changes **exactly one byte** of the whole GARC and leaves `trpoke` identical; a level + IVs edit changes only that Pokémon. Tests: `RealDumpTrainerTests` (4, need `POKEMANAGER_DUMP`) and `TrainerTableTests` (6, hand-made records, no ROM needed, both generations). |
| Trainers tab | **Done.** `MainTabs` gains **Advanced: Trainers** at the end (the save stays index 1, so undo is untouched): side list of the 785 trainers with a strip in the colour of their AI level and the class + name as the game writes them (`GameNames.TrainerNames`/`TrainerClasses`/`TrainerLabel`, `ListEntryViewModel` gained a live name and a strip colour); on the right `TrainerDetailViewModel` — header in the AI colour with `0x07 — Basic + Strong + Expert`, a card of AI check boxes (each showing its bit and value, with the three values the game itself uses as buttons), the record (class, battle type, money, team size, and in Gen 6 own moves / held items / healer), the four bag items, and one card per team member headed in the species' current types. Generation differences are shown, not hidden: the Gen 6 IVs byte (with "≈ 18 in every IV") or the Gen 7 nature, six IVs and six EVs. **Where an entry's moves or held item are stored but ignored** (the Gen 6 flags are off) the card says so instead of letting the user edit into a void. New field view models `BitFieldViewModel` and `BoolFieldViewModel`. |
| Verified with the real controls | Headless harness rendering `EditorView` over the real X dump: Korrina reads 0x07, three Pokémon, IVs 150, her moves and her two Hyper Potions. **Switching between six trainers writes 0 edits** (the ComboBox trap of 1.0 — ability choices are a fixed four-option list, so no `ItemsSource` ever changes under a `SelectedIndex`); an AI preset + a level + a team size make exactly 3 edits, survive reopening the trainer, and `Revert` puts all three back. |
| Second pass on the tab (2026-09-17, user) | **By category is the default mode** and one-by-one is the other one; **teams hidden by default**; the AI as **one list** instead of three check boxes; and a way to set the highest IVs. |
| — Categories | The side list is the game's **own trainer classes**, grouped **by name** (several class ids share one: X has two "Pokémon Trainer" entries and two "Team Flare"), plus "Every trainer" on top: 75 groups in X, each with its count, the range of AI values and of levels. The panel applies to the whole group what would otherwise be repeated trainer by trainer: **AI level**, **battle type**, **IVs** (with *Highest IVs*) and **levels as a percentage**. Every apply is **one undo step** (`EditorViewModel.BeginBatch`, which suspends the per-edit `ProjectUndo.Record`) and says what it did ("85 values changed in 33 trainers"); values already equal are not written. |
| — AI as a list | `AiLevelFieldViewModel`: bits 0-2 as the **five combinations the game actually uses** (0x00, 0x01, 0x03, 0x05, 0x07), because three check boxes made "expert" look like three switches when the game only ever ships those five. **The other bits are kept** (a rival at 0x87 set to Basic becomes 0x81, verified). Bit 3 (doubles) is not offered: the record's battle type already says that. The remaining bits are check boxes under "Other flags" — only bit 7 in Gen 6. |
| — Teams hidden | `EditorViewModel.ShowTrainerTeam`, off on every open like `ShowAbilities`: the trainer shows "3 Pokémon · team hidden" with a *Show team* button, and the check box in the tab bar opens them all. **Highest IVs** also sits per trainer (255 in Gen 6, 31 each in Gen 7). |
| — Bug caught by the harness | The AI list would not change anything: its `Options` returned **a new list on every read**, so `Refresh()` made the ComboBox drop its selection and write the old value back — **the documented 1.0 trap again**. Both bound lists are now built once. The rule holds for any list bound to an `ItemsSource` next to a two-way `SelectedIndex`, not just for the ones that change contents. |
| — IVs (2026-09-17, user: "more IV options") | The number is now **IVs, 0-31, in both generations** (`Model/Data/TrainerIvs`: Gen 6 byte ÷ 8 as pk3DS reads it, and back as ×8 except 31 → 255 so "the highest" really is), with the byte it becomes shown next to it. Category panel: the value, **the three the game itself uses** as buttons (ordinary 0, gym leader 18, Champion 25), **Only raise** (on by default — applying to "every trainer" must not weaken the bosses), and *Apply* / *Highest* / *Clear*. Per trainer, the same three with a value for the whole team. Before, Generation 7 could only be set to "highest" and Generation 6 asked for the raw byte. Checked with the controls: 18 over an already maxed class changes nothing with *Only raise* and 85 values without it (byte 144 exactly); clear leaves 0; per trainer 20 → byte 160. |
| UPR's trainer level options | Hidden for a while as a duplicate of the level percentage of Advanced: Trainers; **shown again** (2026-09-19, user: they belong with the randomizer's trainer options). UPR ZX's applies to every trainer as part of the randomization, the advanced one per class on top of it. |
| Shops of the built ROM | **Done.** The player's own additions to what the shops sell, in the project (not in the randomizer's settings: a seed change does not touch them and they need no randomizing again). `ShopSettings`: **`FreeRareCandies`** (Rare Candies in every ordinary Poké Mart *and* their price set to 0 — one without the other is not free) and **`ExtraItems`** (things the game never sells: the Mega Stones of a game that has none, an evolution item a randomized run made necessary). Card *Shops of the built ROM* on the Randomizer page (`ShopExtrasViewModel`), and a game whose table cannot be found says so instead of offering the option. **Where the table is** (`Model/Data/GameShops`): a flat run of `u16` item ids per shop, in **`code.bin`** in Generation 6 — not at a fixed address, so it is found by the first ids of the table, which must appear exactly once — and in the romfs module **`Shop.cro`** in Generation 7, at the fixed offset UPR ZX uses (0x50A8 Sun/Moon, 0x50BC Ultra Sun/Ultra Moon). Either way **every id of the table has to be a real item of the game or nothing is written**, so a wrong offset disables the feature instead of corrupting a ROM. **The size of each shop is in the game's code, not in the table**, so an added item takes the last slot of each shop, and **one slot of every shop is always left as the game had it** (a one-item stall is not turned into something else). The price lives in the item table (`GameTables.Items`, field `price`; the game stores a tenth of it), and **a hand-edited price wins over the free candies**, like every other edit. The item archive (X `a/2/2/0`, ORAS `a/1/9/7`, Gen 7 `a/0/1/9` — 36 bytes per entry in both generations) and `Shop.cro` are imported, so folders imported by an earlier version pick them up when the project is opened. |
| Verified on real games | **Pokémon X dump**: 718 items (Poké Ball 200, Rare Candy 4800), table found, 26 shops and 217 ids, the four TM shops hold only TMs; free candies change exactly one slot per shop with room and **not a byte of `code.bin` outside the table**. **The user's Alpha Sapphire**: found at 0x47AA3E, 24 shops, the ten Poké Marts first. **The user's Ultra Moon**: `Shop.cro` 40 KB, table at 0x50BC, the eight Poké Marts each got the Rare Candy and an item the game never sells in their last two slots, 16 ids changed, every other shop untouched and the file the same size. Tests: `RealDumpShopTests` (5, need `POKEMANAGER_DUMP`) and `ShopTableTests` (7, hand-made data, both ways of finding the table). |
| Mega Stones in the pool, not on a shelf (2026-09-18, user: "not on sale — in the pool of items that can come up, in shops, on the ground, as trainer drops, whatever") | **Found by measuring the jar, and it is the whole answer**: Generation 7 allows all 42 Mega Stones but marks **every one of them a bad item** (allowed 42/42, non-bad 0/42 in Ultra Sun/Ultra Moon), so a preset with "ban bad items" on — the usual one — never places a single one anywhere. In X they are in both lists, which is why they turn up there by themselves (measured: a plain shop randomization of X already sold three). So the fix is not to force them into shops (that option is gone) but to **take them out of the bad-items bag**: `RandomizationSettings.MegaStonesInPool`, the ids read from the game's own mega evolution table and passed to the launcher as an eighth argument, where `allowItems` puts them in both lists. Then the randomizer places them wherever it places items — shops, field items, Pickup, a trainer's held item — and nothing is forced. **Verified on the user's real Ultra Sun** with shops and field items randomized and "ban bad items" on: without the option **0 stones** in the shops, with it **14**, placed by the randomizer itself. It is part of the randomization, so it is in the cache key. |
| Mega Stones before the post-game (2026-09-18, user: "not the Z-crystals — I want the Mega Stones to be able to come up before the post-game") | What gates them is not the randomizer's pool (they were allowed everywhere) but **when the game sells them**: in Generation 7 only after the story. Option **"Mega Stones on sale from the start (N)"** in the shops card: the game's own stones go into the ordinary Poké Marts. The list is read from the game's **mega evolution archive** (XY `a/2/1/6`, ORAS `a/1/9/3`, Gen 7 `a/0/1/5`, now imported): 8-byte entries of form, method, argument, and method 1 is "hold this item", so the argument is the stone — **30 in X**, exactly the items whose names end in -ite minus the Eviolite, and **47 in the user's Ultra Sun**. They are **dealt out** across the shops instead of put in each (`AddToRegularShops` takes a second list for that): no shop has 30 shelves, but between them they do, and one slot of every shop is still left as the game had it. **Verified on the user's real Ultra Sun**: `Shop.cro` at 0x50BC, 55 slots changed, **all 47 stones on sale**, a Rare Candy in every Poké Mart, the other 20 shops untouched and the file the same size. |
| Every item of the game in the randomization (2026-09-18, user: "not adding items to shops — enable all the items, from mints to megas, respecting the earlier options like ban bad items") | The per-item shop picker was a misreading and is gone. What the user wanted is the **pool the randomizer draws from**: UPR ZX picks items from lists of its own that leave out whatever a game never hands the player. **Measured with the bundled jar**: X allows 415 of 718 ids, Sun/Moon and Ultra Sun/Ultra Moon **466 of 960** — the banned blocks are the TMs, the Z-crystals (only 2 of the 65 ids 776–840) and most of what Generation 7 added. Mega Stones were *already* allowed everywhere, in X and in Generation 7, so the user's example needed the general fix, not a special case. New option **"Allow every item of the game"** (`RandomizationSettings.AllowAllItems`, part of the randomization: it is in the cache key, so the same seed with and without it are two different ROMs): the launcher takes `all-items` as a seventh argument and `unlockItems` opens the handler's **allowed** list to every id the game has a name for (ids with no item are left out, which is what keeps rubbish out of the ROM), and **leaves the non-bad list alone on purpose** — so the randomizer's own "ban bad items" goes on deciding, which is what the user asked for. **Verified end to end on the real X ROM**, two randomizations with the same seed: with the option and *ban bad items off*, the shops sell 10 items UPR would never place (Point Card, S.S. Ticket, Data Card 05, Holo Caster, Honor of Kalos…); with *ban bad items on*, **nothing outside UPR's curated list** appears. That measurement is also why the hint says most of what it unlocks is key items. |
| Shops: locating the table after a randomization | A randomization rewrites the shops, and the Generation 6 table is found by the ids the game ships in it — so the search could stop matching and the shop options would do nothing without a word. The offset is now looked for in the **game folder's own executable** when the randomized one does not match (same length, the randomizer moves nothing: measured, 5 156 864 bytes both). |
| Shops moved to the item options (2026-09-18, user, with a screenshot) | The card was on the Randomizer's **Build** page and the user looked for it in **Options → Items**, next to UPR ZX's own item options ("Field items", "Shop items", "Balance shop prices"). It lives there now: the Build page is back to its three cards, and `UprOptionGroupViewModel` carries an optional `Shops` (set only on the Items group, `UprOptionsViewModel` takes it in its constructor), which that group's page shows under the columns of options. It is still a project setting, not a randomizer one — no seed, no randomizing again — and the card says so. Rendered in the harness on the real dump: the options load through the Java launcher first (the page is empty until they do). |
| Shops: what fits (2026-09-18, user asked whether the two options were done) | Both were: the Rare Candy option and the list of items the game never sells. Two things measured while checking: the game keeps **one price** and derives buy (×10) and sell (×5) from it, so setting it to 0 zeroes **both** — they cannot be told apart, and the test now says so; and the Mega Stones of X already ship at price 0, because the game never sells them. What was missing was honesty about **how many fit**: a shop has a fixed number of shelves and a long list was cut in silence. The card now says where they will be sold ("3 items: 8 of the 11 ordinary Poké Marts sell the lot, the smaller ones only the last that fit") and warns in orange when the list is longer than the biggest shop can take, so nothing disappears without a word. |
| Trainers tab, third pass (2026-09-17, user) | The side list is a **fixed 360 px** (it was 300 and felt cramped) and the search box now ends exactly where the rows do, so both measure the same. Every card of the tab is **470 px wide**, so the label columns line up between cards instead of each card following its own text. The team member cards keep sizing themselves: they hold a whole Pokémon sheet. |
| Hover descriptions (2026-09-17, user: "for every option in the whole app") | **The 138 randomizer options**: `Tip_<Name>` in the randomizer's resources, read by `UprOptionCatalog.Description` and shown by the option template, which already carried UPR ZX's own text for the misc tweaks (`UprOptionViewModel.Tip` = that text, ours otherwise). **The application's own options** (settings, Locke and the roulette prizes, the Randomizer page, room and battle rules, the save editor, the Pokédex flags, the box picker, the log sections): 90 controls with a `ToolTip.Tip` of their own. **The fields of the game tables** say what they are above the original value (`FieldViewModel.OriginalTip` + `Tip_Field_<table>_<field>`; the six team slots of a trainer share one text, so `p3.level` reads `Tip_Field_trainer_level`). `TooltipTests` keeps it honest: every catalogued option and every field of `GameTables` must have text, and the localization test already requires both languages. Checked at runtime in the harness (`ToolTip.GetTip` on the rendered pages), not only in the markup. The shops card also moved to the first row of the Randomizer page, where the user was looking for it. |
| Item options as options (2026-09-19, user) | The Mega Stones, "every item" and the free Rare Candies were cards with a paragraph under each: they are three check boxes among the item options now, explained only on hover. The picker of items to put on a shelf is gone for good — it had been taken off the page days ago and its view model, its eight strings and `ShopSettings.ExtraItems` were still there with nothing able to reach them. |
| Trainer list: the real reason it looked wrong (2026-09-19, user) | The two lists (by category, one by one) sat in a `DockPanel`, where **every child but the last is docked**: the category list took the width of its own longest row (197 px of the 344 available) and moved with the text. Both are in a `Panel` now and fill the column. The 360 px column of the pass before was right; this is what was eating it. |
| Trainer pictures (2026-09-19, user: "sprites, so it is more visual") | The games keep their trainers as 3D models, so there is no 2D art to read out of the ROM: `Services/TrainerClassSprites` matches the class name against **Pokémon Showdown's trainer sprites** — the collection the profiles already use, cached in `<data>/trainer-sprites` — preferring the variant of the game's generation (`acetrainer-gen6xy`, `dancer-gen7`) and falling back to a short alias table (`teamflare` → `flaregrunt`, `garcon` → `waiter`…). Measured on the real X dump: **52 of its 178 classes match by name alone**; what is left is mostly a person and not a class (Leader, Elite Four, Pokémon Trainer). Shown in the side list, in the category rows and in the detail header; rows keep a `MinHeight` so a class without a picture is not a shorter row. **It needs the internet the first time** (and nothing happens without it): pictures and the list of names are asked for once and cached, rows are told when one lands (`whenLoaded`), and a ROM with randomized class names simply matches nothing. Checked in the harness on the user's Ultra Moon: Juggler, Schoolboy, Dancer, Lady, Doctor and Chef come out, the randomized names do not. |
| Advanced: Game (2026-09-19, user: "shiny odds and assorted game options") | A sixth tab for what changes the whole game. **Shiny**: the games keep no rate that can be dialled — what is there is the branch that decides it, and taking its condition off makes everything that is not shiny-locked shiny (the 3DS community's `code.bin` edit). `Model/Data/GameCode` finds it by the bytes around it (`21 E2 03 20 92 E1 1C`, the branch nine bytes in) and writes nothing unless that signature appears **exactly once** and the byte is the `BEQ` (0x0A → 0xEA). **Verified in three real games**: Pokémon X (0x4F3C2), the user's Ultra Moon (0x2205C6) and their Alpha Sapphire (0x4EC66) — one hit each, same branch, same distance. It is a project setting (`GameSettings`, `Project.Tweaks`, in the undo state), applied at build like the shops, so it needs a rebuild and not a new randomization. UPR ZX's `ShinyChance` is **not** this: its own wording is "1/256 chance of Trainer Pokemon being shiny". **The sweeps**: experience, catch rate, egg cycles, friendship (a value, not a percentage) and trainer money, written as ordinary edits over a whole table in one undo step (`BeginBatch`), with a *Put back* that drops that field's edits — so they show in the advanced tabs, undo and revert like anything else. Tests: `GameCodeTests` (4, hand-made data: the change, a missing signature, one that appears twice, a different branch) and `RealDumpGameTests` (3, on the X dump: the branch is there, switching it on changes exactly one byte of the executable and no romfs file, and without the option the executable is not built at all). |
| The starter scene tells the truth (2026-09-19, user: "it shows Litten and says Litten, and then what you get is random") | **Measured in the user's randomized Ultra Moon and it is exactly what happens**: UPR ZX rewrites the starter scene's text **only in English** (`StarterTextOffset` of its ini, on `File<StoryText>` = `a/0/4/2`), so the English file names Tepig, Tynamo and Igglybuff while **every other language still says Rowlet, Litten and Popplio** — a Spanish player is told they are picking Litten and gets something else. `ModBuilder.BuildStarterText` rewrites that scene in **every language** when the ROM is built: the three the ROM really gives are read from the static archive (`GameStarters.Read`: first three entries of file 0, 20 bytes each, species in the first two — `a/1/5/9` in USUM, `a/1/5/5` in SM; that ROM reads 498, 602, 174 at level 5, the three its English text names), and each shipped name is swapped for the one in the same slot with `GameStarters.Rewrite`. **The type the sentence names moves too** ("Litten usa movimientos de tipo Fuego" → "Tynamo usa movimientos de tipo Eléctrico"), taken from the ROM's own personal table, so a randomized type is right as well; the line a language fills in with a variable is matched by its slot (`[VAR 0101(0002)]`). All three names are swapped **in one pass**, so a starter that took another's place is not swapped twice. English is left alone because there is nothing of the old names left in it. Generation 6 keeps its starters in a CRO module, so it is not offered there. The story text is **not imported** (10 archives of ~2.5 MB): it is read from the base ROM at build time (`ReadAnywhere`). Verified on the user's Ultra Moon: nine languages rewritten, the Spanish scene now reads Tepig / Tynamo / Igglybuff with the right types, English untouched. The models on the table are still the game's own — nothing can be done about those — so the Randomizer page also shows **Starters of this ROM** with their sprites, which is where the player can see them before choosing. Tests: `StarterTextTests` (5, on the real lines of that scene). |
| No spoilers in the application (2026-09-19, user) | The starters card of the Randomizer page is gone: the fix belongs in the ROM's own text, and the application should not be where the run is spoiled. The text rewrite stays. |
| Trainer pictures removed (2026-09-19, user: "if you cannot find sprites for all of them, better take them out") | Only 52 of X's 178 classes matched a Showdown sprite, and a ROM with randomized class names matched none, so the whole thing went — service, bindings and the index of names it needed. |
| Who each trainer is (2026-09-19, user: "the rival and Lillie are not there") | **They were there, with other names**: the user's ROM has *randomized trainer and class names*, so Hau came out as "Juggler Mallorie" and no name in the game text says who anybody is (measured: 653 trainers read of 653, zero unreadable, and not one is called Hau, Gladion or Kukui). **Lillie never battles in Sun/Moon or Ultra Sun/Ultra Moon** — there is no trainer entry for her in any of them. So the part each one plays is now taken from the **ids**, not the names: `Model/Data/TrainerRoles` carries UPR ZX's own tag lists, read out of the bundled jar (`Gen6Constants.tagTrainersXY/ORAS`, `Gen7Constants.tagTrainersSM/USUM`) — 151 tagged trainers in X, 126 in ORAS, 94 in Sun/Moon and 114 in Ultra Sun/Ultra Moon. The list shows it in the name ("Amigo 2 - Juggler Mallorie"), which also makes it searchable: typing "rival" finds the rival fights. |
| Trainers by difficulty (2026-09-19, user, like the randomizer's own groups) | The side list opens with **Bosses**, **Important** and **Ordinary** above the classes, so a difficulty change can be applied to all of them at once with the panel that was already there. The rule is UPR ZX's own — a leader, the Elite Four, the Champion or a post-game boss is a boss; rival, friend and strong are important — plus the named characters (`THEMED:`), which are the team bosses and the trial captains. In the user's Ultra Moon: 32 bosses, 82 important, 539 ordinary. |
| Typing inside a dropdown (2026-09-19, user) | `Controls/TypeAhead`, switched on for every `ComboBox` by the theme. Avalonia's own search (`IsTextSearchEnabled`, **on by default**) only looks at the start of the row, and the rows here start with a number ("#025 Pikachu"), so it is switched off and this one takes over: it matches the start of the row **or the start of any word in it**, without accents or case, keeps what was typed for a second ("pi" then "dg"), and a letter typed again steps to the next match. Both `TextInput` and `KeyDown` are handled, since what a dropdown receives depends on the platform, and a letter that arrives twice is used once. Verified in the harness with real key events on the 808-item species list: P+I selects Pidgey, K then selects Kadabra. |
| Starting the game over (2026-09-19, user) | **Start over…** in the save page: the save is copied to the backups and taken out of the emulator's folder, so the game begins a new adventure. It asks first, refuses while the emulator is open, and records a `BeforeSaveEdit` version, so it can be undone from the history like any other save change (`SaveDocument.Reset`, `SaveWriter.BackUp` pulled out of `Write`). Verified in the harness on a copy: the file goes, a backup appears, the editor goes back to "no save". |
| The Pokédex hides abilities (2026-09-19, user) | Like Advanced: Pokémon: the entry's abilities are their own block, hidden until the **Show abilities** check box of the Pokédex page is ticked (off on every open, not remembered), with "Abilities hidden" in their place. |
| Start over lives in Advanced: Game, and asks twice (2026-09-19, user) | The button moved out of the save page into the game options, and both it and **Open every box** now ask **twice**: the first question explains what happens, the second is the last chance. Nothing brings an adventure back but the copy it leaves in the backups, and no button closes the boxes again. |
| The characters have their names (2026-09-19, user: "I still cannot see Tilo, Lylia, Mayla…") | They are all there — those are the **Spanish names** of Hau, Lillie and Mallow, and the ROM has randomized trainer names, so nothing in it says so. The tags now turn into the name the player reads: `FRIEND` is **Tilo** in Generation 7 (it is Hau), and the characters are translated where the games translate them (`Character_*`: Gladio, Samina, Guzmán, Mayla, Nereida, Chris, Fabio, Zosi, Lysson, Aquiles, Magno, Pepe). The ones the games leave alone (Ilima, Kiawe, Mina, Molayne, Plumeria, Acerola, Dexio, Sina, Soliera) keep their name. **Lylia never battles in Sun/Moon or Ultra Sun/Ultra Moon**, so she is in no trainer list of those games — nor is anyone who does not fight. Checked in the user's Ultra Moon: `FRIEND2-0` reads "Tilo 2" and `THEMED:MALLOW` reads "Mayla". |
| Classes grouped by id (2026-09-19) | The side list grouped by class **name**, and a ROM with randomized names gives different classes the same one — the kahunas, the captains and the rest ended up scattered. It groups by the class **id** now (which no randomization changes) and shows the name, with the id next to it when several classes share it. |
| The save corrupted, and why (2026-09-19, user: "editing corrupts the save", "deleting the save corrupts it too") | **Measured, not guessed.** Editing does not: a no-op write is byte-identical, and a Rare Candy added by the app is stored exactly as the game stores it. Two other things did. (1) **Start over deleted only `main`**: a 3DS save is the folder `data/00000001/` *and* a 16-byte `data/00000001.metadata` (the archive's format) next to it; with only the file gone the game finds a formatted archive with nothing in it and says the save is corrupted, with both gone it starts a clean adventure. `SaveDocument.Reset` now **moves both** to `save-backups/<emulator>/<time>-start-over/`, after checking the path has that shape (`…/data/<8 hex>/main`) and that the copy matches, so it can be put back the same way. (2) **Restoring a history version with "put back the save"** replaced the live save with that version's older copy — what the option says, but it looked like lost progress. The user's save was repaired by hand from the newest game-written copy in the history (backups of what was there kept). |
| Open every box, in Advanced: Game (2026-09-19, user: "I did not want it asked twice, I wanted it in Advanced: Game") | One button next to Start over in the card *The save*; no questions, it is an edit of the save being edited like the save page's other unlocks (undoable there until written). Gone from the save page. Start over still asks twice. |
| Deleting a project (2026-09-19, user: "delete the project, not the ROM") | The ✕ of the start screen only took the project off the list, so its file stayed and a new project with the same name was refused. It now asks and **deletes the project file and its `.history`** — never the base ROM, the imported game data, the built ROMs or the save. Creating a project whose file exists but is not listed (left by the old ✕) offers to replace it, and a `.history` left without a project is removed so it does not become the new project's. Checked in the harness on copies: cancel deletes nothing; confirm removes both and leaves the other project and the game folder. |
| Gen 7 saves lost their signature (2026-09-19, user: "I added Rare Candies, wrote the save and the game said it was corrupted") | **Measured**: Sun/Moon and Ultra Sun/Ultra Moon saves carry a MemeCrypto signature (in Ultra Moon at `0x6C100`, headed by the SHA-256 of the checksum table `0x6CA00..0x6CB50`), and the game refuses a save without it. **PKHeX clears that signature in the byte array it parses**, and `SaveWriter` parsed the very bytes it was about to write to check them — so every Gen 7 save the application wrote (editor, Locke prizes, adaptation after a build, history restore) went out with 0x80 zero bytes there. `SaveUpdater.Parse` works on a copy now; `A_written_generation_7_save_keeps_its_signature` fails without it. PKHeX's own signature is the game's: re-signing a game-written save changes no byte. The user's save was repaired by re-signing it (backup kept). |
| Level cap, groundwork (2026-09-19, user) | The experience each level needs is **game data**, not code: one GARC with a file per growth rate (101 `u32`), found by its values — X `a/2/1/7`, Alpha Sapphire `a/1/9/4`, Ultra Moon `a/0/1/6` (Sun/Moon assumed the same), always the archive before the personal table. `GameSettings.LevelCap` writes every level above the cap as `0x40000000 + n` (still increasing, so no gap is zero) at build time, and nothing unless every file looks like the game's table. A prototype ROM of the user's Ultra Moon with only that archive changed (cap 20) is being tested in game: battles, Rare Candy at the cap, the summary screen. No interface yet. The planned flow: cap per badge / trial crystal, the app reads the save when the game saves and unlocks the next cap, trimming experience banked above the cap. The user's first cap is the first totem's level (17 in their ROM, 14 once the levels below were fixed). |
| Level modifiers (2026-09-19, user: "a modifier for the level of everything") | *Levels (%)* in Advanced: Game: **Everything** (fills the rest) and **Trainers**, **Wild Pokémon**, **Fixed, gifts and totems**, applied at build over whatever the ROM has (randomization included), rounded like UPR ZX (`GameLevels.Scale`, away from zero, 1–100). Trainers in every generation (a hand-edited trainer level wins); wild and fixed only in Generation 7, where the tables are known: wild = the encounter archive (Sun/Ultra Sun `a/0/8/2`, Moon/Ultra Moon `a/0/8/3`, 483 MB so read from the ROM, never imported), every eleventh file from 9, LZ11 around a mini `EA` pack of tables, min/max level at bytes 4/5 of the day half and of the night half (0x164 later); fixed = static archive file 0 (gifts, 20 B, level at +3; the three starters and eggs are left alone, as UPR does) and file 1 (encounters incl. totems and allies, 0x38 B, level at +3). `LZSS.Compress/Decompress(byte[])` added to the fork. **The user's ROM had been randomized twice** (`ULRandomNico.cxi` → `UltraLuna Random.cxi`, +20 % each time): measured, every wild range, 250/252 statics and every totem of the second is exactly `round(first × 1.2)`, while trainer levels were left alone the second time. `UltraLuna Random - niveles20.cxi` = their played ROM with wild and fixed levels ×1/1.2 (the two post-game encounters UPR had clamped at 100 taken from the first ROM): 1588/1588 wild and 287/287 fixed levels equal to the first ROM, only those two archives differ. |
| Level cap by milestones and its indicator (2026-09-19, user: "there must be an indicator of the current level cap somewhere") | `Model/Data/LevelCaps`: a plan of steps — the cap holds until the player gets a **badge** (Generation 6: the leaders are UPR ZX's `GYMn-LEADER` tags) or a **Z-crystal** (Ultra Sun/Ultra Moon, in story order: Normalium ← Ilima's totem (static 4/9), Fightinium ← Hala (trainer 23), Waterium ← totem 137, Firium ← 249, Grassium ← 24, Rockium ← Olivia 90, Electrium ← 146, Ghostium ← 39, Darkinium ← Nanu 154, Dragonium ← Vast Poni 45, Groundium ← Hapu 497, Fairium ← 162), then the **league** until the Hall of Fame, then none. Each cap is the boss's level **as the build will write it** (trainer and static level options applied, hand edits kept); the player can change any step (`LevelCapOverrides`). Checked: Pokémon X's plan is exactly the game's aces, 12 25 32 34 37 42 48 59 and Diantha 68 (`RealDumpGameTests.LevelCapPlan_IsTheLeadersAces`); the user's Ultra Moon gives 14 19 24 26 29 34 40 42 53 59 65 66 72, always rising. Sun/Moon is not offered (not checked on a real ROM). `LevelCapViewModel` (on the editor) reads the save — the save editor's document, or the file — decides the step (`HasItem` piece or bead for crystals), sets `GameSettings.LevelCap` (so the ROM notice asks for a build) and **watches the save** (`SaveWatcher`): when the game saves with a new milestone the next cap is unlocked with a status message. **Indicators**: a blue chip "Level cap N" in the editor's bar (tooltip: what it waits for), the same line on the start screen's project preview, and the plan card in Advanced: Game (crystal icons, ✓ earned, "◀ now" on the current step). Pending: trimming experience banked above the old cap when a cap is unlocked; the in-game test of the prototype (battles, Rare Candy, summary). |
| Level cap, second encoding (2026-09-19, user: "I went past the cap and it put my Pokémon at level 100") | **Measured on the user's save**: the game stored level **20** (three Rare Candies from 17 over a cap of 18: a candy ignores the cap and sets the next level's experience); the "100" was **the application**, reading the level from experience with the normal table — and the build adaptation would have recalculated its stats at 100 too. Worse, the prototype's levels above the cap were one experience point apart, so the next battle *would* have sent it to 100 in game. Now level `n` above the cap needs `0x10000000 + n × 0x01000000` (`ExperienceTable.CappedExperience`): a candy moves one level, battles none, and the value says the level (`LevelOfCapped`). `Save/ExperienceLevels.LevelOf` is the one place levels come from (save editor, stat formula); `Fit` runs in the build adaptation with the new ROM's cap — a Pokémon above it keeps its level coded, one at or under it gets its level's ordinary experience back. Tests: `ExperienceLevelsTests` (4), the table tests updated. |
| NCCH header after packing (2026-09-19, user: "fatal error" in Citra) | UPR ZX's `saveAsNCCH` writes region offsets/sizes with `RandomAccessFile.write(int)` — **one byte** — and never updates the content size, so a build whose RomFS shrank (the recompressed wild archive) promised more than the file held: "Unable to read RomFS". `Randomizer/NcchHeader.Repair` recomputes the header from the file (logo, plain, ExeFS from its file table, RomFS aligned to 4 KB up to the end, checked by its IVFC magic) and `RomBuilder` applies it to every packed ROM. Dry-run on the user's ROMs: offsets identical, only the sizes wrong. |
| Game options saved (2026-09-19) | `Project.Save` left `Shops` and `Tweaks` out of the file: shiny, level options, level cap and free candies were lost on reopening. Saved and loaded now (`ProjectTests.SaveLoad_KeepsTheGameOptionsAndTheShops`). The level percentages are **on top of the base ROM's levels**, and the card says so (the user's base was already +44 %). |
| Boxed Pokémon at level 100 (2026-09-21, user: "when it caps it puts the Pokémon at level 100") | **Level 48 of the cap encoding is exactly `0x40000000`**, the first prototype's base, so from 48 up every coded value was read as a prototype one — and in a box nothing else says the level, so the fallback answered 100 and the next build's adaptation wrote it. Measured on the user's save: six boxed Pokémon that were level 52 came out coded 100 (the party was safe: it stores the level). A coded value is a whole number of steps above `CappedBase` and a prototype one never is, which is how they are told apart now (`LevelOfCapped`, `IsPrototype`). |
| Mega Stones on sale again (2026-09-21, user: "I am level 60 and have not seen a single one") | Their project had **`megaStonesInPool` off**, and in Generation 7 every stone is a bad item, so with "ban bad items" the randomizer never places one. That option belongs to the randomization (a run in progress would have to be rolled again), so the old **"Mega Stones on sale from the start"** came back as a project setting (`ShopSettings.MegaStonesOnSale`): the game's own stones are dealt out across the ordinary Poké Marts when the ROM is built, no new seed. Test on the X dump: all 30 on sale, one slot of every shop left as the game had it, other shops untouched. |
| Trainers that mega evolve (2026-09-21, user) | *Mega evolution* card in Advanced: Trainers: from a level on, the **last** Pokémon of every trainer of the group carries its own Mega Stone (`GameData.MegaStoneBySpecies`, read from the same mega evolution table; a species with two keeps the first). A species with no mega evolution is left alone and Generation 6 also gets the record's "uses held items" flag, without which the game ignores what the entry carries. Checked in the harness on the user's Ultra Moon: 52 trainers over level 30 got a matching stone on their last slot. |
| Spin again from the roulette (2026-09-21, user) | The Locke tab's "Allow again" is also a button in the roulette window: it forgets the spin and the wheel goes back to ready. What a claimed prize already put in the save stays there. |
| The trial crystals were the wrong ones (2026-09-21, user) | The game's crystal icons are the items **776-793 in Pokédex order**, and they were being indexed with a **game-order** type: Melemele's Fighting trial showed the Firium Z. `PkhexImages.CrystalIcon(type)` turns one into the other, for the trainer card's five trial slots and the start screen's. |
| Spinning a milestone again (2026-09-21, user: "let me click the badges/crystals and re-roll as often as I like") | Any earned badge or crystal of the start screen can be clicked now (`CanSpin` is just "earned"): the roulette window decides what to offer — claim what is pending, or **Spin again**. |
| Finding an item in the bag (2026-09-21, user) | The item picker of the bag is a search box over the dropdown: it narrows the pocket's list to what the name contains, without accents or case, and picking one changes the item in the slot. A pocket holds hundreds (335 in the user's Items pocket), and a plain dropdown made them hard to find. Checked in the harness: "piedra" leaves 15, picking the first writes it. |
| The trainer picture is the game's (2026-09-21, user: "the photo I want is the one in the game, not the app's") | The games draw the trainer as a 3D model — the card and the passport render it live, so there is no photo to read. **But the screen that starts the adventure has one**: `a/1/6/2` (HeroSelect) holds eight 100×100 BFLIM portraits, `m1_1`…`m1_4` and `f1_1`…`f1_4`, and the save says which one the player is: gender + `MyStatus.DressUpSkinColor` (0-3). `TrainerPortraits.Load` reads it (imported with the rest of the game data) and the trainer card shows it; the profile avatar is only used when the game has none (Generation 6, where that screen has not been found). |
| **The application in two halves** (2026-09-21, user: "redistribute it, we touch trainers in many places, items in others") | The shell is **two tabs**: **Juego (ROM)** — what is built into the game and needs a rebuild — and **Partida** — what is touched while playing. `MainTabs` keeps the save at index 1, so the undo routing (`SaveTabIndex`) and the "open this Pokémon" jump from the start screen did not change. Juego holds *Crear y opciones* (the randomizer, its options and ours), *Pokémon*, *Movimientos*, *Entrenadores* and *Datos*; Partida holds *Pokémon*, *Mochila*, *Entrenador*, *Pokédex* and *Locke*. What moved: the **Locke** is a tab of the run; **opening every box** and **starting over** sit with the run's other unlocks (Partida → Entrenador → Desbloqueos), not in the ROM's rules. |
| One file per section, in one place (2026-09-21, user) | The **Datos** tab of Juego is where every transferable file is, one per section: Pokémon `.pkdata`, moves `.mvdata`, **trainers `.trdata`** (new: `PokemonDataKind.Trainers` over the `trainer` table) and **rules and shops `.gmdata`** (new: `GameRulesFile`, the project's `GameSettings` + `ShopSettings`, values only, so it works between games). The randomizer's own options keep travelling in their `.rnqs` preset, which the card says. The export/import buttons that were repeated in the advanced tabs and on the Randomizer page are gone from there. |
| **One page per subject, not two** (2026-09-21, user: "unify the randomizer's options and the ones outside, I do not want things overlapping") | The application's own options are **inside the randomizer's option groups**, next to UPR ZX's on the same subject, instead of in a tab of their own: *Pokémon* holds the table sweeps (experience, catch rate, egg cycles, friendship), *Entrenadores* the trainer levels and money, *Pokémon salvajes* and *Iniciales, estáticos e intercambios* their level percentages, *Objetos* the item and shop options (Mega Stones in the pool, every item, free Rare Candies, stones on sale) and *General y ajustes varios* the shiny patch and the level cap plan. Each card says it is not the randomizer's, so changing it needs a build and not a new seed. **Nothing appears twice**: UPR ZX's four level modifiers (trainers always, wild/static/totem in Generation 7, where this application writes those levels itself) are hidden — but still read and written back, so a preset keeps its value —, and what a preset carries is shown in our card with a button to put it to 0, because the two would pile up. Its "increased shiny chance" is the odds of a **trainer's** Pokémon being shiny (1/256), which its own label did not say and made it look like ours: it is called that now. Rendered in the harness over the user's Ultra Moon project, whose preset carries +20 % wild and static: every group shows one card, no duplicated option, and the warning with its button. |
| Everything looked modified (2026-09-21, user: "compare them with the original values and do not say they are modified when they match") | **Two causes, both measured on the user's real projects.** (1) `IsModified` only asked whether the project *held* an edit for that entry, and a project keeps edits whose value the ROM already has — the base moves under them when the seed changes, a rebuild lands or a data file comes from another ROM. `EditorSession.Open` now compares each edit with the base as it applies it and **drops the ones that say what the ROM says** (`MatchedTheRom`), the same rule `Set` always had; the editor says how many and saves the project without them. In their Ultra Moon project **266 of 5713** were such no-ops. (2) The other 4792 really differ: they are abilities, held items and learnsets imported from another ROM, so **974 of 975 species** were marked and rightly so. For that there was no way back short of entry by entry, so each advanced tab has **Put all back (N)** — it asks, drops that section's edits in one undo step and says what it did. Reverting also **clears the mark when the table normalizes what it stores** (a learnset comes back sorted, so 24 edits survived their own undo). Checked with the real controls on a copy of their project: 4792 → 0 edits and 0 marks, one Ctrl+Z brings them back. |
| The tag says what changed (2026-09-21, user: "some have their stats changed and others do not, and it says modified all the same") | "Modified" was a blanket word over any edit of the entry's tables, so a Pokémon whose stats are the ROM's but whose **abilities** came from an imported data file looked exactly like one with new stats. `Services/EditSummary` turns the edited fields into the kinds a player thinks in — Stats, Types, Abilities, Items, EV yield, Moves, Text, and for trainers AI, Team, Data — and the tag shows **the first kind**, with an ellipsis and the whole list in its tooltip when there is more than one (the row shares its width with the name: full sentences trimmed "Charmander"). The order is fixed, so a stat change always reads *Stats*. On the user's Ultra Moon project every species now reads *Habilidades…* (abilities, items, moves), which is the answer to their question: no stat of theirs was touched. |
| Importing data from another game (2026-09-21, user: "importing a .pkdata from Ultra Sun crashes, because there are Pokémon that do not exist in Pokémon X — ignore them, and the same for the options and the moves") | **The crash was real and in one place**: every table refused an id out of range with an `ArgumentOutOfRangeException` that the importer catches, except the **learnsets**, which indexed their array straight — an Ultra Sun file has 976 and Pokémon X has 799, so the import took the application down. It is an id out of range like the others now, and the importer also catches what could never reach it before (index, key, overflow, JSON). **Beyond the ids, the values**: a file from another game names abilities, moves, items and species this one has no row for, and writing the number would leave the ROM pointing at nothing. `Model/Edits/DataLimits` reads what the target really holds (personal, move and item tables of the imported game; the ability count is the game's, 188 X/Y, 191 ORAS, 232 Sun/Moon, 233 Ultra) and the importer **skips what does not fit, saying what and why** ("move 670 (this game has up to 617)"), including each move inside a learnset. The same importer serves `.pkdata`, `.mvdata` and `.trdata`, so moves and trainers are covered; the options travel in `.gmdata` (values only) and the `.rnqs` preset, which already report a failure instead of crashing. **Measured with the user's real projects**: everything of their Ultra Moon into Pokémon X — Pokémon 34 160 values, 5692 applied, 21 112 already equal, **7356 skipped**; moves 9477 values, 632 applied, **1443 skipped** — and no crash. Test: `DataFromABiggerGame_SkipsWhatThisGameDoesNotHave`. |
| "Could not build the ROM: the save checksums are not valid" (2026-09-21, user) | **Two faults, one message.** (1) The ROM *was* built — the adaptation of the save runs after it — but the exception took the rest of the method with it, so the seed was not recorded, no history version was written and the project went on saying it had unbuilt changes, while the message blamed the build. Adapting the save is now its own `try`: the ROM is finished and the status says *the save was not adapted: …* after it. (2) The save was **not damaged**: measured on the user's file, it is what the game leaves after **Start over** — the archive is created again (backup of the old one kept, `save-backups/Citra/20260921-164256-start-over`, trainer Puchy, 6 h 51) and the new `main` has every checksum region invalid, no trainer, no play time and nothing in the party, because the player has not saved in game yet. `SaveUpdater.NotSavedYet`/`Problem` tell that apart from a damaged save, and both the build and the randomizer's *Your save* card say "the game has not saved this adventure yet" instead of an empty name and a zero. Test: `A_save_the_game_has_not_written_yet_is_told_apart_from_a_damaged_one`. |
| Mega Venusaur came out Normal/Fighting (2026-09-21, user: "check what it applies, I did not have those settings") | **The row number of the personal table is a different Pokémon in each game, and the import went by number.** Measured on the user's games: row 760 is **Mega Venusaur** in Pokémon X and **Bewear** in Ultra Moon, row 722 is a Deoxys form in X and **Rowlet** in Ultra Moon — so importing an Ultra Moon `.pkdata` into X wrote Bewear (Normal/Fighting, 120/125/80/55/60/60) onto Mega Venusaur, which is exactly what they saw in game, and their project still held it. A file now records **what Pokémon each row held** (`rows`: id → species and form, from `Model/Data/PersonalIndex`, which reads a game's own table: the species first, then each entry's `FormStatsIndex` and `FormeCount`), and the import puts the data on the row **that game** keeps that species and form in; a Pokémon the game does not have is skipped with its name. A file made before this (no map) only gets its **species rows** applied when the games differ, because a row above them is a form of a different Pokémon in each. **Trainers of another game are not imported at all**: a trainer number there is a different trainer here. Verified on the real games: Ultra Moon's Mega Venusaur (row 848, Grass/Poison 80/100/128/122/140/80) lands on X's row 760 and X's row 722 keeps its Deoxys data. Tests: `RowsAreTranslatedByPokemon_NotByNumber`, `WithoutARowMap_OnlyTheSpeciesRowsOfAnotherGameAreUsed`, `TrainersOfAnotherGameAreNotImported`. |
| "It still does not import the good ones" (2026-09-21, user) | **The file was the problem, and it was measured.** `buff.pkdata` says *game X* and was exported from the X project **after** the bad import, so it carries Bewear on row 760 and Rowlet on 722: importing it again puts the same thing back. Imported on a clean copy it applies 5492 values with nothing skipped — the import works, the file is the one that is wrong. What their X project holds is Ultra Moon's data (Venusaur 80/82/98/100/110/80 in both) with the form rows misplaced, so the way back is to export from the Ultra Moon project **with this version** (the file now carries the row map) and import that: measured on copies, Mega Venusaur then reads 11/3 80/100/128/122/140/80, Ultra Moon's own. Two things improved while checking: a **learnset is no longer dropped whole** because one of its moves does not exist here — the move is left out and the rest is applied (`DataLimits.TrimLearnset`; 656 learnsets kept that way instead of lost, applied 4380 → 5036) — and the import reports them apart (`PokemonDataImport.Adjusted`, "adaptados: N" in the status). |
| `.trdata` export, difficulty profiles | pending |

---

## 3. pk3DS — how to reuse it

Repo: https://github.com/kwsch/pk3DS · Last commit 2026-02-27 ("Update to .NET 10").
99 files in `pk3DS.Core`, 116 in `pk3DS.WinForms`.

### The problem to solve first

`pk3DS.Core.csproj` declares `<TargetFrameworks>net10.0-windows</TargetFrameworks>` and
`<UseWindowsForms>true</UseWindowsForms>`. **It is not a portable library.** Since the UI is
Avalonia (cross-platform), decoupling it is not optional.

**Solved** (see `docs/upstream-pk3ds.md`). The contamination was of two kinds:

1. **WinForms** — 6 files in `CTR/` took `RichTextBox`/`ProgressBar` for progress reporting:
   `BLZ.cs`, `CRO.cs`, `CTR.cs`, `NCCH.cs`, `NCSD.cs`, `RomFS.cs`. Replaced with
   `IProgress<string>` and `IProgress<ProgressState>`. `Properties/Resources.resx` only names
   WinForms `ResXFileRef`s and the .NET SDK compiles it untouched.
2. **`System.Drawing` and native code** — not found in the initial research. `System.Drawing`
   only works on Windows in modern .NET. Affects `ImageUtil.cs`, `CTR/SMDH.cs`,
   `CTR/Images/**` and `Structures/TypeChart.cs`; `CTR/ETC1.cs` also loads a native Windows
   `ETC1Lib.dll`. Graphics code: **excluded from compilation**, not deleted. Nothing in
   `Game/`, `Structures/` or `Randomizers/` depends on it.

What is really needed builds cleanly: `Game/`, `Structures/`, `Randomizers/`,
`CTR/GARC.cs`, `CTR/LZSS.cs`, `CTR/mini.cs`.

### What it already provides

- **Gen 6 editors:** personal, learnsets, evolutions, egg moves, moves, items, TM/HM, tutors,
  type chart, starters, static encounters, gifts, shops, Pickup, mega evolutions, Maison,
  title screen, `RSTE` (trainers), `XYWE` (wild), `OWSE` (scripts), text editor.
- **Randomizers in `pk3DS.Core/Randomizers/`:** species, moves, move info, learnsets, evolutions,
  egg moves, forms, personal, generic.

### Inherited trap

It identifies the game **by counting files in the `a/` folder**: 271 = X/Y, 299 = ORAS,
311 = SM, 333 = USUM. No hash, no header. With an odd dump it fails silently.

**Hardened** in `Pokemanager.Model/Dump/DumpInspector.cs`. Besides the count it checks:
- that the signature GARCs are VER_4 with their entry count (personal 800, move 618, levelup 799,
  evolution 799; measured on a real X dump);
- the SMDH title in `exefs/icon.bin`, the only thing that **tells X from Y** (the count cannot);
- that `code.bin` has no BLZ footer, i.e. it is decompressed.

It reports every problem at once.

pk3DS language index for X/Y (added to gametext 072 / storytext 080): 0 kana, 1 kanji,
2 English, 3 French, 4 Italian, 5 German, 6 Spanish, 7 Korean.

---

## 4. X/Y RomFS map — **verified**

Source: `pk3DS.Core/Game/GARCReference.cs`. Rule: number `NNN` → path `a/N/N/N`.

| No. | Path | Contents |
|---|---|---|
| 005 | `a/0/0/5` | movesprite |
| 012 | `a/0/1/2` | encdata (wild encounters) |
| 038 | `a/0/3/8` | trdata |
| 039 | `a/0/3/9` | trclass |
| 040 | `a/0/4/0` | trpoke |
| 041 | `a/0/4/1` | mapGR |
| 042 | `a/0/4/2` | mapMatrix |
| 072 + lang | `a/0/7/x` | gametext |
| 080 + lang | `a/0/8/x` | storytext |
| 104 | `a/1/0/4` | wallpaper |
| 165 | `a/1/6/5` | titlescreen |
| 203–206 | `a/2/0/3` … | maison |
| 212 | `a/2/1/2` | move |
| 213 | `a/2/1/3` | eggmove |
| 214 | `a/2/1/4` | levelup |
| 215 | `a/2/1/5` | evolution |
| 216 | `a/2/1/6` | megaevo |
| **218** | **`a/2/1/8`** | **personal** ← milestone 1 target |
| 220 | `a/2/2/0` | item |

### Formats

- GARC container **version `0x0400`** (`GARC.VER_4`) on X/Y and ORAS. Gen 7 uses `0x0600`.
- `PersonalInfoXY`: **`0x40` bytes** per entry. `a/2/1/8` has 800 files: 799 entries
  (species 0–721 and alternate forms from 722) and, as the **last** file, the
  **concatenation of the 799 entries** (verified on the real dump; pk3DS builds
  `PersonalTable` from it). When modifying an entry, that copy must be updated too.
- `a/2/1/2` (move) files are 36 bytes, although `Move6` only interprets 0x22.
- `Learnset6`: `int16` pairs (move, level) with a terminator.
- On X/Y each move is a separate GARC file. On ORAS and Gen 7 they are packed in a "mini" `WD`
  container — that code is **not** shared across generations.

### Outside the RomFS

**TMs and tutors live in `.code.bin` (ExeFS)**, which must be **decompressed** (BLZ) to edit.
Consequence: the app needs two user folders, `romfs` and `exefs`, both from a decrypted dump.

---

## 5. Azahar — **verified**

Repo: https://github.com/azahar-emu/azahar · very active.
Version in use: **2126.1.1**, build `azahar-windows-msvc` (portable).
Source for everything below: `src/core/file_sys/ncch_container.cpp`, `src/common/common_paths.h`,
`src/common/settings.h`, `dist/scripting/citra.py`.

### Title IDs

- Pokémon X → `0004000000055D00`
- Pokémon Y → `0004000000055E00`

### Where the user folder is

- **Portable** (the zip): `user` folder next to `azahar.exe` → `<dir>\user\load\mods\...`
- **Installed on Windows:** `%APPDATA%\Azahar\load\mods\...`

### Hook 1 — LayeredFS (RomFS)

```
<user>/load/mods/<ProgramID 16 hex>/romfs/
<user>/load/mods/<ProgramID 16 hex>/romfs_ext/    ← for files that do not exist in the original
```

File-level overlay: only modified GARCs are placed. `GetModId` masks the update bit, so the mod
also applies to the patched version of the game.

Blunt alternative: a `<rom>.romfs` file replaces the whole RomFS and disables LayeredFS.

### Hook 2 — ExeFS / code.bin

Two ways, and one is clearly better:

1. `load/mods/<TID>/exefs/code.bin` — full replacement. **Must be decompressed**: the override
   path is resolved *before* LZSS decompression of the original code.
2. `load/mods/<TID>/exefs/code.ips` or `code.bps` — patch over the **already decompressed** code.
   **This is the good way**: a few KB instead of a multi-MB binary per TM change.

### Hook 3 — memory RPC

- UDP, port **45987**.
- Operations: `ReadMemory`, `WriteMemory`, `ProcessList`, `SetGetProcess`.
- **At most 1024 data bytes per packet** → every large read must be chunked.
- `Settings::values.enable_rpc_server` starts as **`false`**. The user enables it by hand;
  this goes in the getting-started guide, not buried in a FAQ.
- Protocol documented by implementation in `scripting/citra.py` (included in the zip).
- Existing reference clients: [CitraRNG](https://github.com/Admiral-Fish/CitraRNG),
  [PKHeXRNG](https://github.com/kwsch/PKHeXRNG).

---

## 6. Risks

1. **There is no X/Y memory map to copy.** Azahar provides the channel, not the addresses. Party,
   current map and RNG state must be taken from CitraRNG and Project Pokémon ActionReplay codes,
   and **they are version-specific** (1.0 ≠ 1.5). The most hand-crafted part of the project.
2. **The RPC is an internal API, not a contract.** Isolate the client behind our own interface.
3. **1024 B/packet is not a stream.** Design with caching and 5–10 Hz polling, not 60 fps.
4. **The user must provide a decrypted dump** (romfs + exefs). The app cannot decrypt anything.
5. **Do not write "Citra" or "Azahar" into the architecture.** Call it "emulator" and put the
   connection behind a thin layer.

---

## 7. Milestone 1 — the edit → see it in game loop

The goal is not having an editor: it is closing the full loop once.

1. **[Done]** Fork pk3DS. Decouple `pk3DS.Core` from WinForms (`IProgress<T>` in the `CTR/` files),
   target `net10.0`. Verify it builds outside Windows.
2. **[Done]** Headless wrapper (`GameDump.Open`): open the `romfs` folder, count files in `a/`, build the
   `GameConfig` and call `Initialize(romfs, exefs, language)`.
3. **[Done]** Edit layer (`Model/Edits`, `EditorSession`, JSON project in `Model/Projects`): `{table, id, field, value}` model and applying it to the model.
4. **[Done]** (`Model/Build/ModBuilder`, also moves and learnsets) Read `a/2/1/8`, modify a species' base stats and repack with
   `GARC.PackGARC(byte[][], GARC.VER_4, padding)`.
5. **[Done, later superseded]** Write the result to `<user>/load/mods/0004000000055D00/romfs/a/2/1/8`. Since the
   redesign, edits go into the built `.cxi` instead.
6. Start the emulator and check the change in game.
7. **[Done, extended]** Avalonia 12 UI (`Pokemanager.App`): Pokémon (data + learnset),
   moves, Save and Build. Verified with headless screenshots on the real dump.

**Milestone 1 pending: step 6**, checking the change in game.

### Solution layout

```
Pokemanager.Formats     pk3DS.Core fork without WinForms — the only thing that knows GARC/LZ11/code.bin
Pokemanager.Model       game model, edit layer, build
Pokemanager.Randomizer  UPR ZX integration
Pokemanager.Save        save adaptation (PKHeX.Core)
Pokemanager.Bridge      emulator bridge (folders; later UDP 45987, launch/reload)
Pokemanager.App         Avalonia
Pokemanager.Tests
```

Note: with a single target game, do not abstract `Model` over pk3DS structures more than needed.
The one thing worth keeping neutral from day one is edit addressing.

---

## 8. First version scope

**Yes:** stats and types, moves, learnsets, evolutions, trainers, wild encounters, items, TMs.

**Not yet:** text, event scripts, graphics, maps, 3D models.

## 9. Still to find out

- Whether Azahar can reload the game from the command line or it must be closed and reopened.
- X/Y memory addresses for party, current map and RNG (see risk 1).
