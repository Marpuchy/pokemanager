# Bundled tools

Pokemanager ships with these tools; users do not have to install them or pick their paths.
On build, this folder is copied next to the application binaries (`<app>/tools/`).

| Folder | Tool | Version | License | Source |
|---|---|---|---|---|
| `upr/` | Universal Pokémon Randomizer ZX (`PokeRandoZX.jar`) | 4.6.1 | GPL-3.0 | https://github.com/Ajarmar/universal-pokemon-randomizer-zx |

**PKHeX** is not in this folder: its core, [PKHeX.Core](https://github.com/kwsch/PKHeX) (GPL-3.0), is used
as a NuGet package inside `Pokemanager.Save`, so it is compiled into the application.

**Java** (11 or later) is still required by UPR ZX; for now the one installed on the system is used.
To do: bundle a trimmed runtime with `jlink` when preparing the installer.

To update UPR ZX: replace `upr/PokeRandoZX.jar`, update the version in this table and run the tests with
`POKEMANAGER_DUMP` set (the `PokemanagerUpr.java` launcher uses UPR internal APIs).
