# Pokemanager

Randomizador y editor de partida para Pokémon X/Y (3DS), pensado para usarse con un emulador de
3DS mediante LayeredFS. Junta en una sola aplicación lo que hoy se hace con Universal Pokémon
Randomizer ZX (la ROM) y PKHeX (la partida).

La aplicación no modifica nunca el volcado del juego: todo lo que genera va a la carpeta de
mods del emulador y se puede regenerar a partir de `volcado + proyecto`. El repositorio no
contiene datos del juego; cada usuario aporta su propio volcado descifrado.

> Estado: **fase A (randomizer) hecha**. Fase B (editor de partida con PKHeX.Core) pendiente.

## Requisitos

- SDK de .NET 10.
- Volcado descifrado de Pokémon X o Y: el `.3ds` y sus carpetas `romfs` y `exefs` extraídas
  (`code.bin` descomprimido), todo en la misma carpeta.
- Para randomizar: [Universal Pokémon Randomizer ZX](https://github.com/Ajarmar/universal-pokemon-randomizer-zx)
  (`PokeRandoZX.jar`) y Java 11 o superior.

## Uso

```
dotnet run --project src/Pokemanager.App
```

1. **Nuevo proyecto:** carpeta del volcado, idioma, carpeta de usuario del emulador (se detecta
   sola) y dónde guardar el proyecto `.json`.
2. **Randomizer:** marca «Randomizar», elige un preset `.rnqs` de UPR ZX (se guarda dentro del
   proyecto) y una semilla. **Randomizar e instalar** ejecuta UPR ZX y escribe el resultado en
   `<emulador>/load/mods/<TitleID>`. La misma semilla con el mismo preset da siempre el mismo juego.
   El log de UPR se consulta por secciones (iniciales, entrenadores, salvajes…) con búsqueda.
3. **Avanzado:** retoques manuales de Pokémon (stats, tipos, habilidades, learnsets…) y movimientos,
   aplicados **encima** del random. En naranja, lo que difiere de la base.
4. **Instalar en el emulador** (Ctrl+B) vuelve a instalar random + retoques. Reinicia el juego.

> El mod de `load/mods/<TitleID>` se aplica a **cualquier** ROM de ese juego que abras en el
> emulador, incluidos `.cxi` ya randomizados con UPR por separado.

## Deriva de pk3DS

**Este proyecto deriva de [pk3DS](https://github.com/kwsch/pk3DS), de Kaphotics (kwsch) y
colaboradores**, publicado bajo GPL-3.0.

`src/Pokemanager.Formats/` es `pk3DS.Core`, importado con `git subtree` y modificado para que
compile como librería `net10.0` multiplataforma, sin WinForms. Ver [NOTICE](NOTICE) y
[docs/upstream-pk3ds.md](docs/upstream-pk3ds.md) para el detalle de los cambios y cómo traer
actualizaciones de upstream.

Universal Pokémon Randomizer ZX (GPL-3.0) no se incluye: se usa el jar que tenga el usuario.

## Licencia

GPL-3.0, heredada de pk3DS. Ver [LICENSE.md](LICENSE.md).

## Estructura

```
src/Pokemanager.Formats     pk3DS.Core sin WinForms: GARC, LZ11, BLZ, estructuras Gen 6
src/Pokemanager.Model       volcado (Dump/), datos y capas de romfs (Data/), ediciones (Edits/, Editing/),
                            proyecto (Projects/), construcción e instalación del mod (Build/)
src/Pokemanager.Randomizer  UPR ZX: localizar Java y el jar, lanzador con semilla fija, caché
src/Pokemanager.Bridge      emulador: carpeta de usuario, de mods y del guardado
src/Pokemanager.App         interfaz Avalonia
tests/Pokemanager.Tests     pruebas (xUnit v3)
```

## Compilar y probar

```
dotnet build
dotnet test
```

Las pruebas usan Microsoft Testing Platform (`global.json`) y xUnit v3. Las que necesitan datos
reales se **omiten** (no fallan) si faltan estas variables:

| Variable | Qué es |
|---|---|
| `POKEMANAGER_DUMP` | Carpeta con el `.3ds`, `romfs` y `exefs` |
| `POKEMANAGER_UPR_JAR` | `PokeRandoZX.jar` |
| `POKEMANAGER_UPR_PRESET` | Un preset `.rnqs` |

```
$env:POKEMANAGER_DUMP = "G:\pokemanager-dump"        # PowerShell
$env:POKEMANAGER_UPR_JAR = "D:\citra\roms\PokeRandoZX.jar"
$env:POKEMANAGER_UPR_PRESET = "D:\citra\roms\alex.rnqs"
dotnet test
```
