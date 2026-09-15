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
