# Pokemanager

Randomizer and save editor for Pokémon X/Y (3DS), meant to be played on a 3DS emulator (Citra, Azahar).
It brings together in one application what is usually done with Universal Pokémon Randomizer ZX (the ROM)
and PKHeX (the save file).

The application never modifies your base ROM or dump: randomized ROMs are created as new files next to it
and can always be regenerated from `dump + project`. The repository contains no game data; each user
provides their own decrypted dump.

> Status: **randomizer complete** (editable options, new ROM, save adapted to it). Full save editor
> (phase B) still to do.

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

1. **New project:** dump folder, game text language and where to save the project `.json`.
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
6. **Advanced:** manual Pokémon and move edits on top of the randomization; they are included in the built ROM.

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
