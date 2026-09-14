## New in 1.1.0: play online with friends

- **Multiplayer rooms**, on the start screen: the host clicks **Create room** and sends the invite code (`PM-…`); friends
  paste it and **Join**. No server and no account — the apps connect directly, encrypted with the room's code.
- **Live**: everyone sees the other players' **party and boxes** (types, moves with PP, ability, nature, item, stats), sent
  again every time someone saves in the game. The host decides with two checkboxes whether parties and boxes are visible.
- **Nothing to set up on the router** in most homes: Pokemanager opens the port by itself (UPnP / NAT-PMP) and finds your
  public address. If a friend still cannot get in, their app shows an **answer code**: they send it to the host, the host
  clicks **Accept**, and both routers open the way. (If both of you are behind very strict carrier NAT, it may not connect.)
- **Profile** in *Settings*: your name, a color and a **trainer sprite** (search Pokémon Showdown's ~1500 sprites, downloaded
  when chosen) or your own picture. The other players see it in the room.
- The first time you open a room, **Windows Firewall** asks whether Pokemanager may use the network: allow it.
- Everyone in a room needs Pokemanager 1.1.0 or later.

## Install

1. Download **PokemanagerApp-win-Setup.exe** below and run it. It installs for your user (no administrator rights needed)
   and creates **Pokemanager** shortcuts on the desktop and in the Start menu.
2. Open Pokemanager, click **New project…**, choose a **decrypted** ROM of your game and click **Create project**.

Everything is included: the .NET runtime, a Java runtime for Universal Pokémon Randomizer ZX, UPR ZX itself and PKHeX.Core.
Prefer not to install? Unzip **PokemanagerApp-win-Portable.zip** and run `Pokemanager.App.exe`.

Windows may show a SmartScreen warning because the installer is not code-signed: choose *More info → Run anyway*.

## Supported games

Pokémon X, Y, Omega Ruby, Alpha Sapphire, Sun, Moon, Ultra Sun and Ultra Moon (decrypted `.3ds`, `.cci` or `.cxi`).
Sun and Moon are expected to work like Ultra Sun/Ultra Moon but have not been tested on a real ROM yet.

## What's in it

- **Randomizer** (UPR ZX with a reproducible seed and all its options), building a new ROM next to yours.
- **Save editor** in the style of PKHeX: Pokémon, bag, trainer, Pokédex — using the stats and abilities of the ROM you play.
- **Version history** of the project and the save, and **Pokémon data files** (`.pkdata`).
- **Locke tools**: lives, badges/trials read from the save and a reward roulette that puts items into the save.
- **Multiplayer**: rooms by invite code to see each other's party and boxes live, with your own profile and trainer sprite.
- English and Spanish interface.
