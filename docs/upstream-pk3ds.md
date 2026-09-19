# pk3DS upstream in `src/Pokemanager.Formats`

`src/Pokemanager.Formats/` is the `pk3DS.Core/` folder of
[kwsch/pk3DS](https://github.com/kwsch/pk3DS), imported with `git subtree --squash`.

| | |
|---|---|
| Imported upstream commit | `6daaca934ca2284a73ab743bf89c848c57cd9de1` (2026-02-27, "Update to .NET 10") |
| `pk3DS.Core` split commit | `70e3c69915e1a689eadf43ad254ac4e8ac21ac1f` |
| License | GPL-3.0 |

## Local changes from upstream

They are kept in their own commits, separate from the import ones, so that `git log` and
`git diff` against upstream show only our changes.

1. **`pk3DS.Core.csproj` → `Pokemanager.Formats.csproj`.** Target `net10.0` (was
   `net10.0-windows` with `UseWindowsForms`). `RootNamespace` is still `pk3DS.Core`: namespaces
   are not renamed, to avoid conflicts on every merge.
2. **Progress reporting without WinForms** in `CTR/BLZ.cs`, `CRO.cs`, `CTR.cs`, `NCCH.cs`, `NCSD.cs` and
   `RomFS.cs`:
   - `RichTextBox` → `IProgress<string>` (one log line per call).
   - `ProgressBar` → `IProgress<ProgressState>` (`CTR/ProgressState.cs`, new file).
   - The original parameter names (`TB_Progress`, `PB_Show`) are kept to keep the diff small.
     `null` = no reporting.
3. **Excluded from compilation** (`<Compile Remove>` in the csproj; the files stay on disk):
   `ImageUtil.cs`, `CTR/ETC1.cs`, `CTR/SMDH.cs`, `CTR/Images/**`, `Structures/TypeChart.cs`.
   They depend on `System.Drawing` (Windows-only in modern .NET) or on the native Windows
   `ETC1Lib.dll`. They are graphics code; nothing in `Game/`, `Structures/` or `Randomizers/` depends on them.
4. **`GARC.MemGARC.Data` changed from `internal` to `public`** (`CTR/GARC.cs`, a single token). It holds
   the bytes of the packed GARC, which Model needs to write output files. Upstream it was only
   reachable from inside the library.
5. **Bug fixed in `CTR/NCSD.cs` (`ExtractCXIfromNCSD`).** The loop copied 10 units of 0x200 bytes per
   iteration for `ncchSize` iterations, producing a `game.cxi` 10 times too large (~17 GB for Pokémon X)
   filled with garbage past EOF. It now copies the exact size. Candidate for an upstream PR.

6. **Bug fixed in `Structures/Gen6/TrainerData6.cs` (`Write`).** For ORAS it wrote a literal `0` where the record has a
   field the constructor reads into `uORAS`, so reading and writing an ORAS trainer changed the file. It now writes the
   field back, and every trainer of a real dump round-trips byte for byte (test
   `RealDumpTrainerTests.EveryTrainer_RoundTripsByteForByte`). Candidate for an upstream PR.
7. **`CTR/LZSS.cs` gains `Decompress(byte[])` and `Compress(byte[])`**, in-memory wrappers around the stream methods
   (the public entry points only took file paths). Used for the Generation 7 wild encounter areas (`GameLevels`).

`Properties/Resources.resx` is untouched: its entries are `ResXFileRef`s that the .NET SDK
compiles without WinForms, and `Exheader` and the `.3ds` build in `CTR.cs` need them.

## Pulling upstream changes

`git subtree split` needs the path to exist at HEAD of the repository where it runs, so the
split is done in a separate pk3DS clone (for example `../pokemanager-ref/pk3DS`). The split is
deterministic: the same history produces the same hashes, which is why later merges find the
common point with the previous import.

```sh
# 1. In the pk3DS clone
cd ../pokemanager-ref/pk3DS
git pull origin master
git branch -f pk3ds-core-split "$(git subtree split --prefix=pk3DS.Core master)"

# 2. In this repository (clean working tree)
cd ../../pokemanager
git subtree pull --squash --prefix=src/Pokemanager.Formats ../pokemanager-ref/pk3DS pk3ds-core-split
```

After merging, update the table above and the commit in `NOTICE`.

What may conflict:

- **The 6 files in `CTR/`** if upstream changes their progress signatures.
- **The csproj**: if upstream touches `pk3DS.Core.csproj`, git sees a change to a file that was
  renamed here. Review by hand and carry over what matters.
- **New files** using `System.Drawing` or WinForms will break the build: add them to
  `<Compile Remove>` or adapt them.
