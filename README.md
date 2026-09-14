<p align="center">
  <img src="src/Pokemanager.App/Assets/pokemanager.png" width="96" alt="Pokemanager icon" />
</p>

<h1 align="center">Pokemanager</h1>

<p align="center">
  Randomizer, save editor and Nuzlocke companion for the Pokémon games of the Nintendo 3DS.<br/>
  <b>X · Y · Omega Ruby · Alpha Sapphire · Sun · Moon · Ultra Sun · Ultra Moon</b>
</p>

<p align="center">
  <a href="https://github.com/Marpuchy/pokemanager/releases/latest"><img alt="Latest release" src="https://img.shields.io/github/v/release/Marpuchy/pokemanager?label=download"></a>
  <a href="https://github.com/Marpuchy/pokemanager/actions/workflows/ci.yml"><img alt="CI" src="https://github.com/Marpuchy/pokemanager/actions/workflows/ci.yml/badge.svg?branch=main"></a>
  <a href="LICENSE.md"><img alt="License: GPL-3.0" src="https://img.shields.io/badge/license-GPL--3.0-blue"></a>
</p>

Pokemanager brings together in one application what is usually done with **Universal Pokémon Randomizer ZX** (the ROM)
and **PKHeX** (the save file), for games played on a 3DS emulator such as Citra or Azahar.

- **Randomize** with every UPR ZX option and a reproducible seed; the result is a new ROM next to yours.
- **Edit your save** like in PKHeX — Pokémon, bag, trainer, Pokédex — using the stats and abilities of the ROM you play.
- **Adapt a save in progress** when the ROM changes (new seed, new options), with backups and verification.
- **Keep a history** of versions to go back when a build goes wrong, and share Pokémon data as `.pkdata` files.
- **Locke tools:** lives, badges or island trials read from the save, and a reward roulette that puts items in your bag.
- Interface in **English** and **Spanish**.

The application never modifies your ROM, and the repository contains no game data: each user provides their own
**decrypted** ROM.

## Download and install (Windows)

1. Download **`PokemanagerApp-win-Setup.exe`** from the [latest release](https://github.com/Marpuchy/pokemanager/releases/latest).
2. Run it. It installs for your user (no administrator rights) and adds **Pokemanager** to the desktop and Start menu.
3. Open Pokemanager → **New project…** → choose your ROM → **Create project**.

Everything is bundled: .NET, a Java runtime for UPR ZX, UPR ZX itself and PKHeX.Core. Windows SmartScreen may warn because
the installer is not code-signed (*More info → Run anyway*). A portable `.zip` is published next to the installer, and
Pokemanager can be uninstalled from *Settings → Apps* like any other program.

Your data lives outside the program folder, so updating or uninstalling keeps it:

| What | Where |
|---|---|
| Projects | `Documents\Pokemanager\Projects` |
| Game data copied from your ROMs (20–50 MB each) | `Documents\Pokemanager\Games` |
| Save backups and randomizer cache | `%LOCALAPPDATA%\Pokemanager` |
| Settings | `%APPDATA%\Pokemanager\settings.json` |

## Usage

0. **Start screen:** your projects on the left. Selecting one shows, read only, its ROM and the team of its save
   (sprite, name, level, held item); clicking a Pokémon shows its stats, IVs, EVs, moves and ability. **Manage** (on each
   project, on the preview and on the Pokémon) opens the full editor described below.
   For Nuzlocke-style runs the preview also shows the progression read from the save — the **8 gym badges** in X/Y and
   ORAS, the **grand trials** and the island challenge in Sun/Moon and Ultra Sun/Ultra Moon, with the game's own images —
   and the **lives** left
   (− / + to lose or recover one). Clicking an earned badge spins its **roulette** once: items and money go straight into
   the save (emulator closed; a history version and a backup are made first), an extra life adds to the count. A prize won
   while the emulator was open stays pending on the badge (!) until it is claimed.
1. **New project:** choose the ROM. The game is recognized from the ROM itself, only the data Pokemanager reads is copied
   (20–50 MB) to `Documents\Pokemanager\Games\<ROM name>`, and the project is saved in `Documents\Pokemanager\Projects`.
   Everything else adapts to the game: data formats, save editor options, badges or trials, sprites and the randomizer
   options (totem Pokémon in Generation 7). Game names (species, moves, items) are shown in the interface language.
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

## Build from source

Requirements: .NET 10 SDK and Java 11 or later (a JDK 17+ to build the installer).

```
dotnet run --project src/Pokemanager.App
```

### Tests


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
| `POKEMANAGER_TEST_ROMS` | Decrypted ROMs of any supported games, separated by `;` (imported into a temporary folder) |

```
$env:POKEMANAGER_DUMP = "G:\pokemanager-dump"        # PowerShell
$env:POKEMANAGER_UPR_PRESET = "D:\citra\roms\alex.rnqs"
dotnet test --solution Pokemanager.slnx
```

### Installer

```
./build/build-installer.ps1 -Version 1.0.0
```

Publishes the app self-contained, compiles the UPR ZX launcher, builds a trimmed Java runtime with `jlink` and packs a
per-user installer with [Velopack](https://velopack.io) into `artifacts/installer`. Pushing a `v*` tag runs the same
script on GitHub Actions and publishes the release.

## Branches

- **`main`** — stable: what the latest release is built from.
- **`dev`** — ongoing work, merged into `main` for each release.

## Translations

Every user-facing string lives in a `Resources/Strings.resx` file per project (App, Model, Randomizer, Save)
with its Spanish counterpart in `Strings.es.resx`. `LocalizationTests` checks that each Spanish file
translates every key and keeps the same `{0}` placeholders.
