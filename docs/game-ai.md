# Trainer AI in the 3DS games — analysis and difficulty models

Research note for version 3.0 (branch `feature/game-ai`). Goal of the version: let the player choose how hard the
adventure is, by changing what the game itself does — starting with the trainer AI.

Everything marked **verified** was read from the real Pokémon X dump (`D:\pokemanager-dump`, romfs + exefs) with a
throw-away harness, or from source we have (our `Pokemanager.Formats`, upstream pk3DS at `D:\pokemanager-ref\pk3DS`,
the bundled `PokeRandoZX.jar`). Anything else is marked as a guess and has a way to check it in section 7.

---

## 1. Where the AI actually lives

Two separate things, and only one of them is data:

| Piece | Where | Editable by us today |
|---|---|---|
| **Which AI a trainer uses** | one byte in the trainer's record, in the `trdata` GARC | not yet (we neither import nor write `trdata`) |
| **What each AI does** | the battle engine, a **CRO module in the romfs**: Gen 6 `DllBattle.cro`, Gen 7 `Battle.cro` | no (needs reverse engineering) |

**Verified:** the engine is not in `code.bin`. UPR ZX's own offset files name it: `File<Battle>=<DllBattle.cro, …>` for
X/Y and ORAS, `File<Battle>=<Battle.cro, …>` for Gen 7 (`com/dabomstew/pkrandom/config/gen6_offsets.ini`,
`gen7_offsets.ini` inside `tools/upr/PokeRandoZX.jar`). `DllBattle.cro` is in the romfs root of the real X dump,
1 040 384 bytes. CRO modules are hashed in a `.crr`, so a patched CRO has to be re-hashed — pk3DS carries the code for
that (`CTR/CRO.cs`, `E_HashCRR`), which we compile but never use.

Archives per family, from `GARCReference` (`GameTitle.Layout()` does not list them yet):

| Family | trdata | trpoke | trclass |
|---|---|---|---|
| X/Y | `a/0/3/8` | `a/0/4/0` | `a/0/3/9` |
| ORAS | `a/0/3/6` | `a/0/3/8` | `a/0/3/7` |
| SM | `a/1/0/5` | `a/1/0/6` | `a/1/0/4` |
| USUM | `a/1/0/6` | `a/1/0/7` | `a/1/0/5` |

### The record (Generation 6, `TrainerData6`)

`format` (bit 0 = the entry carries its own moves, bit 1 = it carries a held item), `class`, `battleType`, `numPokemon`,
**4 bag items**, **AI byte**, 3 unused bytes, `healer`, `money`, `prize`. Each team member: an **IVs byte**, a packed
byte (ability slot, gender), level, species, form, and optionally item and 4 moves. No EVs, no nature.

### The record (Generation 7, `TrainerData7` / `TrainerPoke7`)

0x14 bytes: class, `Mode` (Singles/Doubles/Multi), count, 4 bag items, **AI byte at 0x0C**, a flag, money. Each member is
0x20 bytes and much richer: gender, ability slot, **nature**, **6 EVs**, **6 IVs** (5 bits each) + shiny, level, species,
form, item, 4 moves.

pk3DS names the eight AI bits (Gen 7 editor, `Structures/Gen7/TrainerAI.cs`, checkbox labels in `SMTE.Designer.cs`):

| Bit | Name |
|---|---|
| 0 | Basic |
| 1 | Strong |
| 2 | Expert |
| 3 | Doubles |
| 4 | NoWhiteout |
| 5 | BattleRoyal |
| 6 | PokeChange |
| 7 | UseItem |

The names come from pk3DS, not from the engine; what each one changes in a battle is **not documented anywhere public**
(searched: the pk3DS pull request that added the bits, the pk3DS issue asking for a "max AI" button, PokéCommunity).
The community's working answer is "7 is what the important battles use".

---

## 2. What the AI byte holds in a real game — **verified on Pokémon X**

785 files in `trdata`, 784 real trainers. The byte is a **bitfield**, exactly as in Gen 7, and only bits 0, 1, 2 and 7
are ever used:

| Value | Bits | Trainers | Who |
|---|---|---|---|
| `0x00` | — | 30 | the first Youngsters and Lasses of the game |
| `0x01` | Basic | 195 | ordinary trainers, Team Flare grunts |
| `0x03` | Basic+Strong | 40 | Artists, Black Belts, Battle Girls |
| `0x05` | Basic+Expert | 128 | Viola (first gym), rival Calem in the early fights |
| `0x07` | Basic+Strong+Expert | 288 | **every other gym leader, all four Elite Four, the Champion**, Lysandre, Ace and Sky Trainers |
| `0x80` | bit 7 | 3 | Twins, a Team Flare pair |
| `0x81` | bit 7+Basic | 10 | paired trainers, some grunts |
| `0x83` | bit 7+Basic+Strong | 4 | Brains & Brawn, Artist Family |
| `0x85` | bit 7+Basic+Expert | 29 | rival Calem (mid game), Rangers |
| `0x87` | bit 7 + the three | 57 | Battle Chateau owners, the late rival |

Two things follow immediately:

1. **The ceiling is low.** `0x07` is the best AI the game ever gives itself in a single battle. "Harder AI" cannot mean
   "more flags" beyond that — the top of the scale is already in use by the sixteen battles that matter.
2. **The floor is deep.** `0x00` exists and is used, so "easier" is a real, native setting: 561 of the 784 trainers can
   be dropped to a dumber AI using values the game itself ships.

Bit 7 is the unclear one. It appears on 103 trainers; 48 of them fight double or multi battles, but 55 do not (the
rival's single battles carry it). It is **not** simply "Doubles" (17 double/multi trainers do not have it) and it does
not correlate with carrying bag items (4 of 103). Gen 7's name for that bit is *UseItem*, yet in X the leaders that
visibly use Hyper Potions have `0x07`, without it. Unresolved — see section 7.

Battle types in X: 719 single, 31 of type 1, 18 of type 2, 16 of type 3.

---

## 3. The rest of the difficulty is in the same records — **verified**

The AI byte is one lever among several, and not the strongest one.

**The IVs byte.** One byte, 0–255, that the engine turns into IVs; pk3DS reads it as `IVs = value / 8` (so 255 → 31).
Real distribution over the 1 634 trainer Pokémon of X:

| Byte | ≈IVs | Count | Who |
|---|---|---|---|
| 0 | 0 | 686 | ordinary trainers |
| 10–80 | 1–10 | 300 | mid-route trainers, Battle Chateau |
| 100–140 | 12–17 | 107 | Team Flare, Veterans, the rival's friends |
| **150** | **18** | **343** | **gym leaders, Elite Four, rival Calem** |
| 180 | 22 | 12 | Lysandre and the Team Flare Boss |
| **200** | **25** | **74** | **Champion Diantha** |
| 240–250 | 30–31 | 49 | a few Chateau nobles and one leader entry |

So even the Champion's team sits at IV ≈25 with **zero EVs and no nature control** in Gen 6. There is a lot of room.

**The trainer's bag.** 59 trainers carry items and the engine uses them mid-battle: Full Restore ×46 (2 each for the
Elite Four, **4 for Diantha**), Hyper Potion ×21 (2 each for most leaders), Super Potion ×10, Full Heal ×7,
Max Potion ×4, X Defense / X Sp. Atk / X Sp. Def / X Attack ×6 (Scientists and one leader), Potion ×1 (Viola).

**Moves.** Only **269 of 784** trainers define their own four moves. The rest get the last four moves of their level-up
learnset, computed by the game — which is why randomized trainers are so often harmless.

**The rest:** 40 trainers give a team member a held item, 7 have the `healer` flag, the money multiplier runs 0–60, and
team size averages 1.5 for filler trainers against 2.3 for the `0x07` group. The three unused bytes are **0 in every
trainer of X**.

**Generation 7 adds** per-Pokémon EVs, nature and full 5-bit IVs, so the same ideas go much further there.

---

## 4. The levers, ranked by cost

| # | Lever | Where | Cost | Ceiling |
|---|---|---|---|---|
| L1 | AI flags per trainer | `trdata` | low | limited: `0x07` is the game's own maximum |
| L2 | Team quality: level, IVs byte, moves, held item, evolution stage, team size | `trpoke` + `trdata` | low | very high |
| L3 | The trainer's healing bag | `trdata` | low | high (a Full Restore loop is brutal) |
| L4 | Gen 7 only: EVs, natures, ability slot | `trpoke` | low | high |
| L5 | Patch the battle engine (`DllBattle.cro` / `Battle.cro`) | romfs CRO | **high** (RE + per-version offsets + CRR rehash) | the only way to change *how* the AI thinks |
| L6 | Scale to the player, using the save we already read | our app | medium | unique to us |

One property makes all of L1–L4 safe: **trainers do not exist in the save file**. Changing them cannot break a run the
way changing base stats or abilities does (`SaveUpdater` exists precisely for that). A difficulty change needs a ROM
rebuild, never a save adaptation.

---

## 5. Proposed models

Five, ordered by what they give per unit of work. They are not exclusive; A–C share the same plumbing (D).

### Model A — Difficulty profiles (rule-based, deterministic)

One choice in the app: **Relaxed · Normal · Challenge · Nightmare** (plus *Custom*). Not a pile of per-trainer edits: a
rule stored in the project (like `RandomizationSettings` and `LockeSettings`) and applied at build time, so the project
stays a few KB and the result is reproducible from `dump + seed + profile`.

Trainers are classified first — **boss** (leaders, Elite Four, Champion, rival, villain-team bosses), **important**
(named trainers with `0x07`/`0x05`, a big money multiplier or 4+ team members) and **regular** — because the same rule
should not hit a Youngster and Diantha alike. Gen 7 has pk3DS's `ImportantTrainers_*` lists; Gen 6 has none, so the
classifier uses trainer class + AI + money + team size, which section 2 shows separates them cleanly.

| Profile | Regular | Important | Boss |
|---|---|---|---|
| Relaxed | AI `0x00`, IVs 0, no bag | AI `0x01`, IVs ≤10, no bag | AI `0x01`, IVs 50, halve the bag |
| Normal | untouched — the original values | | |
| Challenge | AI `0x03`, IVs 100 | AI `0x07`, IVs 150, own moves | AI `0x07`, IVs 200, own moves, full bag |
| Nightmare | AI `0x07`, IVs 150 | AI `0x07`, IVs 255, own moves, +1 member | AI `0x07`, IVs 255, best moves, 6 members, 4 Full Restores, and in Gen 7 EVs and natures |

"Own moves" means switching the entry to custom moves and filling them from the ROM's learnset and TM list **as the ROM
currently is**, which is what makes this work on a randomized game: a randomized Pyroar with its level-up moves is a
joke; the same Pyroar with the four best moves it can actually learn is not.

### Model B — Level curve

Independent of A, because players ask for it separately: a multiplier or offset on trainer levels, a different value for
bosses, and an option to force fully-evolved teams above a level. UPR already offers a flat `TrainersLevelModifier`;
ours is worth having only if it is curve-aware (early game untouched, late game steeper) and if it can be changed
without re-randomizing — which UPR's cannot.

### Model C — Adaptive rival (the one only we can do)

We read the player's save. At any rebuild we know the party's levels, the badge or trial count and how far the story has
gone, so trainers can be rewritten *relative to the player*: "bosses always 2 levels above your highest", "no trainer
below your average −5". The rebuild is already something the user does; this only adds a step that looks at the save
first. Rebuilding mid-run is the operation we already guard with the `Guard_*` warnings, and trainer changes are safe
(section 4), so the warning can be softer here.

### Model D — Per-trainer editor (the plumbing, needed by A–C anyway)

A "Trainers" tab in Advanced, the same shape as Pokémon and Moves: list on the left, the trainer's card on the right
(class, name, AI checkboxes with what each bit is believed to do, bag, battle type, money, and the team with level, IVs,
item, ability and moves). Edits go through `EditorSession.Set` like everything else, so they stay `{table, id, field,
value}` and survive re-randomization. Export and import as a `.trdata` file of the same family as `.pkdata` / `.mvdata`.

This is also the only model that can ship without deciding anything about the AI itself.

### Model E — Patch the engine (research spike, not a 3.0 promise)

The only way to make the AI genuinely smarter: switch out of bad matchups, stop wasting setup turns, predict. It means
disassembling `DllBattle.cro` / `Battle.cro`, finding the move-scoring routine, and shipping byte patches keyed to each
CRO's hash per game and version, plus a CRR rehash. Two smaller wins may fall out of the same spike at a fraction of the
cost:

- flipping a **single constant** — for example the random factor that makes the AI pick a worse move — instead of
  rewriting logic;
- confirming what Basic / Strong / Expert / bit 7 actually do, which would turn Model A's table from a guess into a
  design.

**Recommendation for 3.0:** D first (it unblocks everything and is useful on its own), then A, then B. C as an option
inside A. E as a time-boxed spike whose result decides whether 3.1 carries a real AI patch.

---

## 6. What the application is missing today

1. `GameImporter` does not copy `trdata` / `trpoke` / `trclass`, and `GameLayout` does not name them. Trainer *names* and
   *classes* are already imported (they are part of the game text). `GameImporter.Complete` already exists to fill in
   folders imported by an older version, so existing projects would pick the new files up when opened.
2. `GameTables` has no trainer table, and `ModBuilder` writes personal, moves, learnsets and text only — it needs to
   repack `trdata` and `trpoke` (plain GARCs, the same `ReadGarc` / `BuildGarc` path; in Gen 6 the `trpoke` entry size
   varies with the trainer's `format` flags, which `TrainerData6.Write` already handles).
3. The pipeline gains a stage. Today: `dump → randomization → manual edits → build`. With difficulty:
   **`dump → randomization → difficulty rules → manual edits → build`** — the rules run after UPR (which rewrites whole
   teams) and before manual edits, so a hand-edited trainer still wins. Same rule as the rest of the app.
4. Nothing in `Pokemanager.Save` changes: trainers are not in the save.

---

## 7. Open questions, and how to answer each

| Question | How |
|---|---|
| What do Basic / Strong / Expert actually change? | Build two ROMs differing only in one bit on one gym leader and fight it with a fixed party, watching the choices. Slow but definitive. |
| What is bit 7 in Gen 6? | The same A/B test on a rival battle (single, carries the bit) and on a leader with a full bag: does it start using items? |
| Is `IVs = byte / 8` right? | Set a trainer's byte to 255 and to 0 and compare the damage taken, or find the division inside `DllBattle.cro`. |
| Does the AI need a flag to use the bag? | Give a Youngster four Full Restores with AI `0x00`, then with `0x07`. |
| Does Citra load a patched CRO without fixing the CRR? | Change one byte of `DllBattle.cro`, rebuild, boot. This answers how expensive Model E is. |
| Do UPR's trainer options rewrite the AI byte? | It rewrites teams; the AI byte was not among its trainer settings. Check by diffing `trdata` before and after a randomization. |

---

## Harness used

A throw-away console app in the session scratchpad referencing `Pokemanager.Model`, reading
`D:\pokemanager-dump\romfs\a\0\3\8` and `a/0/4/0` through `GARC.MemGARC` and `TrainerData6`, with names from
`GameDump.Open(…).Config.GetText(TextName.TrainerNames | TrainerClasses)`. It only reads. If the numbers in this
document ever need redoing, it is twenty lines.
