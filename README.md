# Pokemanager

Randomizer and save editor for Pokémon X/Y (3DS), meant to be played on a 3DS emulator (Citra, Azahar).
It brings together in one application what is usually done with Universal Pokémon Randomizer ZX (the ROM)
and PKHeX (the save file).

The application never modifies your base ROM or dump: randomized ROMs are created as new files next to it
and can always be regenerated from `dump + project`. The repository contains no game data; each user
provides their own decrypted dump.

> Status: **randomizer**, **save editor**, **version history** and **Pokémon data files** working. Live
> dashboard still to do.

The interface is in **English** by default; Spanish can be selected in **Settings → Language**.

## Requirements

- .NET 10 SDK and Java 11 or later.
- A decrypted Pokémon X or Y dump: the `.3ds` plus its extracted `romfs` and `exefs` folders
  (`code.bin` decompressed), all in the same folder.
- Universal Pokémon Randomizer ZX and PKHeX.Core **are bundled** (see [tools/README.md](tools/README.md)).

## Usage

```
dotnet run --project src/Pokemanager.App
```

0. **Start screen:** your projects on the left. Selecting one shows, read only, its ROM and the team of its save
   (sprite, name, level, held item); clicking a Pokémon shows its stats, IVs, EVs, moves and ability. **Manage** (on each
   project, on the preview and on the Pokémon) opens the full editor described below.
   For Nuzlocke-style runs the preview also shows the **8 gym badges** read from the save (with the game's own badge images, taken from your dump) and the **lives** left
   (− / + to lose or recover one). Clicking an earned badge spins its **roulette** once: items and money go straight into
   the save (emulator closed; a history version and a backup are made first), an extra life adds to the count. A prize won
   while the emulator was open stays pending on the badge (!) until it is claimed.
1. **New project:** dump folder and where to save the project `.json`. Game names (species, moves, items) are shown in
   the interface language; Pokémon sprites are read from your own dump.
   Projects you create or open appear under **Recent projects** on the start page.
2. **Settings…:** interface language, emulator (by default the one you used most recently), base ROM,
   bundled tools, cache and save backups.
3. **Randomizer → Options:** every UPR ZX option, grouped as in UPR. A `.rnqs` preset can be
   imported/exported.
4. **Randomizer → Build:** seed and ROM name. **Randomize and build ROM**:
   - creates `<base ROM folder>/<name>.cxi` (and its log); the base ROM is never touched;
   - after the first build you can **replace the previous ROM** (default) or create a new one, so files do
     not pile up;
   - if there is a save and the option is checked, it **adapts the save to the new ROM**: every Pokémon gets
     the ability it should have and the party stats are recalculated, with a backup first and verification
     afterwards. The emulator of that save must be closed. **Adapt the save now** does the same without
     rebuilding.
5. Open the `.cxi` in the emulator.
6. **Save file:** a PKHeX-style editor for the emulator save (close the emulator to write):
   - party and boxes: species, form, nickname, level, nature, ability, held item, gender, shiny, friendship, ball,
     original trainer, moves and PP Ups, IVs and EVs; create Pokémon in empty slots or release them;
   - bag pockets and Pokédex seen/caught;
   - trainer, as in PKHeX: name, money, Battle Points, badges, Mega Evolution unlock, Vivillon pattern, boxes unlocked,
     play time, adventure start and Hall of Fame dates, PR Video phrases, game records, Battle Maison streaks, map
     position, and shortcuts to unlock all O-Powers, Friend Safari slots, fashion items and Super Training stages or
     fill Poké Puffs;
   - stats, abilities, growth rates, gender ratios and PP come from **the ROM you play** (the randomized one);
   - "safe for the game" problems (out-of-range values, more than 510 EVs, duplicated moves…) block writing; PKHeX
     legality is only informative, since a randomized game always looks illegal to it;
   - writing makes a backup, refuses if the game saved the file meanwhile, and verifies the result.
7. **History:** a version is kept every time the ROM is built, before restoring or importing, and before writing save
   edits — the configuration that rebuilds the ROM plus a copy of the save. **Restore** puts a version back, rebuilds the
   ROM and adapts your current save (or puts that version's save back). Building with a different seed or options while a
   save exists asks first. Stored next to the project in `<project>.history/`.
8. **Locke:** number of lives and lives lost, the roulette prizes (item, money, extra life or nothing; the weight sets
   each chance and slice size) and the badges already spun, with **Allow again** to spin a badge once more.
9. **Advanced:** manual Pokémon and move edits on top of the randomization; they are included in the built ROM.
   **Export/Import Pokémon data** saves them as a `.pkdata` file — the counterpart of a `.rnqs` for base stats, types,
   abilities, learnsets and moves — either your changes only or every value.

Same options + same seed = same game: a ROM made earlier with UPR ZX can be reproduced from its preset and
the seed in its log.

## Derived from pk3DS

**This project derives from [pk3DS](https://github.com/kwsch/pk3DS), by Kaphotics (kwsch) and
contributors**, published under GPL-3.0.

`src/Pokemanager.Formats/` is `pk3DS.Core`, imported with `git subtree` and modified so that it
builds as a cross-platform `net10.0` library without WinForms. See [NOTICE](NOTICE) and
[docs/upstream-pk3ds.md](docs/upstream-pk3ds.md) for the changes and how to pull upstream updates.

## License

GPL-3.0, inherited from pk3DS. See [LICENSE.md](LICENSE.md).

## Layout

```
src/Pokemanager.Formats     pk3DS.Core without WinForms: GARC, LZ11, BLZ, Gen 6 structures
src/Pokemanager.Model       dump (Dump/), data and romfs layers (Data/), edits (Edits/, Editing/),
                            project (Projects/), edit build (Build/)
src/Pokemanager.Randomizer  UPR ZX: locate Java and the jar, fixed-seed launcher, options, cache, ROM build
src/Pokemanager.Save        save adaptation with PKHeX.Core
src/Pokemanager.Bridge      emulator: user folder, mods folder and save file
src/Pokemanager.App         Avalonia interface (strings in Resources/Strings.resx, Spanish in Strings.es.resx)
tests/Pokemanager.Tests     tests (xUnit v3)
```

## Build and test

```
dotnet build
dotnet test --solution Pokemanager.slnx
```

Tests use Microsoft Testing Platform (`global.json`) and xUnit v3. Tests that need real data are
**skipped** (not failed) when these variables are missing:

| Variable | What it is |
|---|---|
| `POKEMANAGER_DUMP` | Folder with the `.3ds`, `romfs` and `exefs` |
| `POKEMANAGER_UPR_PRESET` | A `.rnqs` preset |
| `POKEMANAGER_SAVE` | A Pokémon X/Y `main` save file (only a temporary copy is read) |

```
$env:POKEMANAGER_DUMP = "G:\pokemanager-dump"        # PowerShell
$env:POKEMANAGER_UPR_PRESET = "D:\citra\roms\alex.rnqs"
dotnet test --solution Pokemanager.slnx
```

## Translations

Every user-facing string lives in a `Resources/Strings.resx` file per project (App, Model, Randomizer, Save)
with its Spanish counterpart in `Strings.es.resx`. `LocalizationTests` checks that each Spanish file
translates every key and keeps the same `{0}` placeholders.
