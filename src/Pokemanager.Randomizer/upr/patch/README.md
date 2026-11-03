# UPR ZX patch for species-expansion mods

Three classes of Universal Pokémon Randomizer ZX **4.6.1** (GPL-3, https://github.com/Ajarmar/universal-pokemon-randomizer-zx,
tag `v4.6.1`), changed so it can randomize a Generation 7 ROM with more species than the game ships with (the USUM
Expansion Mod: 1025 species, 304 extra forms, 920 moves). The compiled classes are in `tools/upr/patch/` and the
runner puts that folder **before** `PokeRandoZX.jar` on the class path, so they replace the jar's own.

| File | Change |
|---|---|
| `Gen7Constants.java` | `getPokemonCount`, `getFormeCount`, `getMoveCount` return `-Dpokemanager.pokemonCount/formeCount/moveCount` when given (the application reads them from the ROM); without them, the game's own numbers. |
| `GlobalConstants.java` | `bannedRandomMoves` / `bannedForDamagingMove` sized 4096 instead of 827 (indexed by move number). |
| `Gen7RomHandler.java` | `setShopPrices` keeps the price of an item with no balanced price (an item the mod added) instead of failing. |

A ROM without a mod randomizes exactly as with the plain jar: no property is passed and nothing else changed.

Rebuild after changing a source (JDK 17+):

```
javac --release 17 -nowarn -cp tools/upr/patch;tools/upr/PokeRandoZX.jar -d tools/upr/patch src/Pokemanager.Randomizer/upr/patch/Gen7Constants.java src/Pokemanager.Randomizer/upr/patch/GlobalConstants.java src/Pokemanager.Randomizer/upr/patch/Gen7RomHandler.java
```

Measured on the user's Ultra Moon with the Expansion Mod (2026-11-03): with the plain jar the randomization fails or
ignores everything past species 807; with this patch it finishes, and Gen 8/9 species and moves appear in the wild
tables, trainer teams and movesets.
