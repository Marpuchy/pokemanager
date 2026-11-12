# Encounters tab and NPC trades — research (2026-11-12)

Measured on the user's Ultra Sun base ROM (laptop, `PokemonUSBase.3ds`) and the Expansion's executable.

## Wild encounters, Generation 7 (verified on Ultra Sun)

- Encounter archive `a/0/8/2` (Sun/Ultra Sun) / `a/0/8/3` (Moon/Ultra Moon): 3696 files, **11 per area**, 337 areas;
  the encounter tables are file `9 + 11·area`, LZ11 around an `EA` mini pack; **74 areas have tables**. Each table:
  4-byte header, then the day half and the night half, 0x164 bytes each (min level, max level, 10 rates, 8 rows × 10
  slots of `u32` = species | form << 11 — row 0 normal, 1-7 SOS —, 6 extra SOS at 0x14C). UPR's randomization output
  carries the whole randomized archive (483 MB) in the cache, so the tab can read the run without building anything.
- **Zone → name**: `a/0/7/7` file 0 (LZ11) = 380 zones × 0x54; **u16 at 0x1C = location name index**; file 1 = 380 ×
  u16 = the zone's **world**. `a/0/9/1` = 303 worlds, each LZ11 → `WD` mini, part 0 = 3 sections (u32 offsets at 0):
  the third is a list of `(zone u16, area u16)` pairs. All 380 zones map to an area, every area with tables has zones.
- Location names: game text **file 72** in Ultra Sun (`Route 1` at 6 and 8, `Melemele Sea` 16, `Ten Carat Hill` 34).
  Sun/Moon not checked (pk3DS: 67).

## Static archive (Ultra Sun `a/1/5/9`, imported)

File 0 gifts (0x14, the three starters first), file 1 static encounters (0x38: legendaries, totems, allies),
**file 4 = in-game trades: 7 × 0x34** (`EncounterTrade7`: given species 0x0, form 0x4, level 0x5, … requested species
0x2C).

## Trades that accept any Pokémon — not found yet

Seven candidate reads of `[x, #0x2C]` near 0x34-sized records in `code.bin`; none is the trade check. The comparison is
probably in the field scripts or a CRO. Needs reverse engineering **and in-game tests** (a test ROM per attempt), per
game (X/Y and ORAS keep trades elsewhere).
