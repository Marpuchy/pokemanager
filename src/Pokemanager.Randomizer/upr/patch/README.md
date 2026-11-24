# UPR ZX patch for species-expansion mods

Four classes of Universal Pokémon Randomizer ZX **4.6.1** (GPL-3, https://github.com/Ajarmar/universal-pokemon-randomizer-zx,
tag `v4.6.1`), changed so it can randomize a Generation 7 ROM with more species than the game ships with (the USUM
Expansion Mod: 1025 species, 304 extra forms, 920 moves). The compiled classes are in `tools/upr/patch/` and the
runner puts that folder **before** `PokeRandoZX.jar` on the class path, so they replace the jar's own.

| File | Change |
|---|---|
| `Gen7Constants.java` | `getPokemonCount`, `getFormeCount`, `getMoveCount` return `-Dpokemanager.pokemonCount/formeCount/moveCount` when given (the application reads them from the ROM); without them, the game's own numbers. |
| `GlobalConstants.java` | `bannedRandomMoves` / `bannedForDamagingMove` sized 4096 instead of 827 (indexed by move number). |
| `Gen7RomHandler.java` | `setShopPrices` keeps the price of an item with no balanced price (an item the mod added) instead of failing. |
| `Gen7RomHandler.java` (2) | Moves the ROM has **no data** for (the Expansion lists the Let's Go and Max moves, 729-741 and 743-774, with every byte 0: 0 PP, no name in game) are written as a usable move picked from the slot itself when the randomizer chose one for a learnset, an egg move, a TM, a tutor or a trainer. They are not banned, so the randomizer's own draws, and the rest of a run with the same seed, stay the same. |
| `Gen7RomHandler.java` (3) | "One to one in each area" maps each **route** as a whole: UPR ZX maps per encounter table, and a route has several (grass, day/night, SOS, fishing), so Ultra Sun's Route 2 went from 10 Pokémon to 38. While `area1to1Encounters` runs, `getEncounters` hands over one set per area (the same `Encounter` objects) and `setEncounters` writes the tables as they were read. Measured on the user's Ultra Sun and preset: Route 2 keeps 10, Route 3 8, Route 5 13-14 (two forms of one species). |
| `NCCH.java` | `rebuildRomfs` copies an unchanged file in 16 MB pieces instead of reading it whole: the model archive is over 1 GB, and next to a randomization held in memory the packer ran out of memory. |

A ROM without a mod randomizes exactly as with the plain jar: no property is passed and nothing else changed.

Rebuild after changing a source (JDK 17+):

```
javac --release 17 -nowarn -cp tools/upr/patch;tools/upr/PokeRandoZX.jar -d tools/upr/patch src/Pokemanager.Randomizer/upr/patch/Gen7Constants.java src/Pokemanager.Randomizer/upr/patch/GlobalConstants.java src/Pokemanager.Randomizer/upr/patch/Gen7RomHandler.java src/Pokemanager.Randomizer/upr/patch/NCCH.java
```

Measured on the user's Ultra Moon with the Expansion Mod (2026-11-03): with the plain jar the randomization fails or
ignores everything past species 807; with this patch it finishes, and Gen 8/9 species and moves appear in the wild
tables, trainer teams and movesets.
