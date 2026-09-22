## New in 2.1: difficulty, a level cap that follows your badges, and the app in two halves

- **Difficulty profiles**: one choice in Options → Trainers — **Relaxed, Normal, Challenge, Nightmare** — writes over every
  trainer of the game what you would otherwise set class by class: the AI they use, their IVs, the healing items their bag
  carries, the level of every team and, from Challenge up, a Mega Stone on the last Pokémon of the important battles. It
  knows who each trainer is (boss, important, ordinary) even in a ROM with every name randomized, and **Normal takes the
  profile back off**. Changing it is one step of Ctrl+Z.
- **Level cap by badges and Z-crystals**: the game stops giving experience past a cap, and the cap rises on its own when
  you earn the next badge (Generation 6) or the next trial's Z-crystal (Ultra Sun/Ultra Moon) — the app reads your save
  when the game saves it. The current cap is shown in the editor's bar and on the start screen, and its whole plan, taken
  from the levels of the game's own bosses, can be changed step by step.
- **Nothing gets past the cap**: a Rare Candy ignores the cap in game, so when you close the emulator the app brings any
  level that went over it back down (and drops the experience banked at the cap, which is what the cap is for). A Pokémon
  caught or given to you above the cap keeps its level. It can be switched off in the level cap card.
- **Level modifiers**: raise or lower by a percentage the levels of the trainers, the wild Pokémon and the fixed
  encounters, gifts and totems, on top of whatever your ROM already has.
- **Shops and items**: free Rare Candies in the Poké Marts, the game's Mega Stones on sale from the start (Generation 7
  only sells them after the story), the Mega Stones in the randomizer's pool and an option to allow every item of the game.
- **Shiny**: every Pokémon that is not shiny-locked can come out shiny, written into the ROM when it is built.
- **The starter scene tells the truth**: the randomizer only rewrote that text in English, so every other language named
  the starters the game no longer gives. It is rewritten in all of them now, with the right types.

### The application

- **Two halves**: *Juego (ROM)*, everything that is built into the game, and *Partida*, everything you touch while playing
  (Pokémon, bag, trainer, Pokédex and the Locke). The application's own options live **inside the randomizer's option
  groups**, next to the ones about the same subject, so nothing is offered twice.
- **One file per section, in one place**: the *Datos* tab exports and imports Pokémon (`.pkdata`), moves (`.mvdata`),
  **trainers (`.trdata`)** and **rules and shops (`.gmdata`)**, to carry your work between ROMs.
- **Data from another game**: a file now says what Pokémon each of its rows held, so importing an Ultra Sun file into
  Pokémon X puts Mega Venusaur's data on Mega Venusaur. What the game does not have — a species, a move, an ability — is
  skipped and reported instead of written as a number the game cannot look up.
- **"Modified" says what changed**: Stats, Abilities, Moves… instead of a blanket word, and each advanced tab has a
  **Put all back** button. An edit that says what the ROM already says is no longer counted as a change.
- **Trainers**: who each one is (rival, friend, gym leader, kahuna…) taken from the ids, groups by difficulty, IVs 0-31 in
  both generations, mega evolution from a level, and the whole class or group edited at once.
- **The trainer picture is the game's**, read from the screen where your adventure starts.
- Typing inside a dropdown finds a Pokémon by any word of its name; the bag's item picker has a search box.
- **Start over** and **open every box** in Advanced: Game; a project can be deleted (never your ROM or your save).

### Fixed

- **Generation 7 saves kept their signature**: the app wrote Sun/Moon and Ultra Sun/Ultra Moon saves without it, and the
  game called them corrupted.
- **"Fatal error" in Citra**: the header the randomizer leaves after packing promised more than the file held.
- **Start over** now removes the whole save data, not only one file, so the game starts a clean adventure.
- A Pokémon in a box no longer reads as level 100 when the cap is on, and the build no longer fails when the save cannot
  be adapted (a new adventure the game has not saved yet).
- Applying something to every trainer or putting thousands of edits back no longer freezes the application.

## New in 2.0: the trainer card of the games, Z-moves and a clearer editor

- **Trainer card like the game's**: the trainer page opens with your card as the game draws it — your picture framed on
  the right, one line per field (name, ID No., Pokédex, money, Battle Points, play time, adventure start, Hall of Fame)
  and your badges, or your island trials, in their slots below.
- **Every trial has a picture** in Sun/Moon and Ultra Sun/Ultra Moon: the games only carry seals for four of the five, so
  the trials now show **the Z-crystal of their type, taken from your own ROM**. The card also has the eighteen type
  crystals, lit up for the ones your bag holds.
- **Exclusive Z-moves can be edited** (Generation 7): in Advanced → Pokémon, a Pokémon with its own Z-move shows the
  crystal, the move it asks for and the Z-move it gives. Point Decidium Z at a move your Decidueye actually knows instead
  of losing the crystal to a randomized learnset.
- **The app says when the ROM has to be built again**: changing Pokémon data, moves or anything else the ROM carries puts
  a notice over the tabs with a *Build ROM now* button. Locke settings never ask for it — the roulette is outside the game.
- **Roulette**: a **free-text prize** for what the app cannot write into the save ("an evolution item of your choice"),
  and an **Already added** button for prizes you put in the game yourself. Z-crystals show their icon as a prize now.
- **History moved into Settings**, so the editor has one tab less.

## In 1.2.1: one look everywhere, a real bag and the trainer card

- **Pokémon look the same everywhere**: the party cards, the box (with the game's wallpaper, ◀ ▶ arrows) and the Pokémon
  sheet of the save editor are now used by the project preview and by multiplayer rooms too — moves with type and category
  icons, stats with bars, held item and Poké Ball icons.
- **Bag like the games'**: pockets on top, a plain list of the pocket, and the selected item with its icon, **the game's
  description** and its item and quantity.
- **Trainer card** on the trainer page, in the game's color: your profile picture, name, ID, money, Pokédex, play time,
  adventure start, Hall of Fame and the badges or seals with their images. Badges also show their image in the list.
- Start screen: **Back to the project** closes the multiplayer panel (the room keeps running).
- Tab strips never wrap into a second row; the randomizer option groups are side tabs.
- Rooms now also share each box's wallpaper, the move categories and the Poké Ball (older versions still connect).

## In 1.2.0: battles, the in-game link and a new editor look

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
