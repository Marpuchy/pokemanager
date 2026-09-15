## New in 1.2.0: battles, the in-game link and a new editor look

The last version before the 2D games.

### Battles between players
- In a multiplayer room, **challenge another player** to a battle with the Pokémon of your **own saves** — and the stats,
  types and moves of **each player's own ROM**, randomized or not. Battles run on Pokémon Showdown's simulator inside
  Pokemanager, with Showdown's battle screen.
- **Rules chosen before each battle**: mechanics generation (6 or 7), level (as in the save, 50 or 100), full HP/PP, team
  preview, clauses (Sleep, Species, Item, OHKO, Evasion, Baton Pass) and megas/Z-moves. A team that breaks them is refused
  with the reasons.
- Everything is bundled (Node.js and the simulator). The battle screen needs an internet connection.

### Play inside the game
- Players with games of the **same generation** (X/Y with Omega Ruby/Alpha Sapphire, Sun/Moon with Ultra Sun/Ultra Moon)
  get a **Citra multiplayer room** opened by the host's app, tunnelled through the Pokemanager room: nothing to set up on
  the router. "Play in the emulator room" starts Citra already connected. Needs a Citra build that includes `citra-room`.

### Editor
- **Undo / redo** for the project and the save (Ctrl+Z / Ctrl+Y), with a long history.
- **Move data files** (`.mvdata`) apart from Pokémon data files, and **editable move descriptions** that show in every
  language of the game.
- **Save editor redesign**: PKHeX-style boxes with the game's wallpapers, party cards in the type colours, items and balls
  with icons, stats / IV-EV / **contest conditions** in tabs, and a **Pokédex page** with this version's entry.
- Advanced tabs: type and category icons, **modified entries highlighted**, and **abilities hidden by default** so you can
  check a randomized ROM's stats without spoilers.
- Locke: roulette with prize icons; lives start at 10.
- Many visual fixes (scroll bars, spinners, date pickers, tabs, Gen 7 sprites).

Everyone in a room needs Pokemanager 1.2.0 to battle or share the emulator room.

## Install

1. Download **PokemanagerApp-win-Setup.exe** below and run it. It installs for your user (no administrator rights needed)
   and creates **Pokemanager** shortcuts on the desktop and in the Start menu.
2. Open Pokemanager, click **New project…**, choose a **decrypted** ROM of your game and click **Create project**.

Everything is included: the .NET runtime, a Java runtime for Universal Pokémon Randomizer ZX, UPR ZX itself, PKHeX.Core,
and Node.js with Pokémon Showdown's simulator for battles.
Prefer not to install? Unzip **PokemanagerApp-win-Portable.zip** and run `Pokemanager.App.exe`.

Windows may show a SmartScreen warning because the installer is not code-signed: choose *More info → Run anyway*.
The first time you open a room, **Windows Firewall** asks whether Pokemanager may use the network: allow it.

## Supported games

Pokémon X, Y, Omega Ruby, Alpha Sapphire, Sun, Moon, Ultra Sun and Ultra Moon (decrypted `.3ds`, `.cci` or `.cxi`).
Sun and Moon are expected to work like Ultra Sun/Ultra Moon but have not been tested on a real ROM yet.

## What's in it

- **Randomizer** (UPR ZX with a reproducible seed and all its options), building a new ROM next to yours.
- **Save editor** in the style of PKHeX: Pokémon, bag, trainer, Pokédex — using the stats and abilities of the ROM you play.
- **Version history** of the project and the save, **undo/redo**, and **Pokémon and move data files** (`.pkdata`, `.mvdata`).
- **Locke tools**: lives, badges/trials read from the save and a reward roulette that puts items into the save.
- **Multiplayer**: rooms by invite code to see each other's party and boxes live, **battles** with your own ROMs, and a
  shared emulator room for in-game link play.
- English and Spanish interface.
