# Pokemanager

Editor, randomizador y tablero en vivo para Pokémon X/Y (3DS), pensado para usarse con un
emulador de 3DS mediante LayeredFS.

La aplicación no modifica nunca el volcado del juego: todo lo que genera va a la carpeta de
mods del emulador y se puede regenerar a partir de `volcado + proyecto`. El repositorio no
contiene datos del juego; cada usuario aporta su propio volcado descifrado.

> Estado: hito 1 casi completo. Editor gráfico de Pokémon (stats, tipos, habilidades, crianza,
> learnsets) y movimientos, con proyecto de ediciones y generación del mod. Falta comprobarlo en el juego.

## Uso

```
dotnet run --project src/Pokemanager.App
```

1. **Nuevo proyecto:** carpeta del volcado (con `romfs` y `exefs`), idioma, carpeta de usuario del
   emulador (se detecta sola) y dónde guardar el proyecto `.json`.
2. Edita. Los campos que difieren del juego original salen en naranja, y la lista marca con ●
   las entradas modificadas. Pasa el ratón por un campo para ver su valor original.
3. **Guardar** (Ctrl+S) escribe el proyecto. **Generar mod** (Ctrl+B) guarda y escribe los GARC
   modificados en `<emulador>/load/mods/<TitleID>/romfs`. Reinicia el juego en el emulador.

## Deriva de pk3DS

**Este proyecto deriva de [pk3DS](https://github.com/kwsch/pk3DS), de Kaphotics (kwsch) y
colaboradores**, publicado bajo GPL-3.0.

`src/Pokemanager.Formats/` es `pk3DS.Core`, importado con `git subtree` y modificado para que
compile como librería `net10.0` multiplataforma, sin WinForms. Ver [NOTICE](NOTICE) y
[docs/upstream-pk3ds.md](docs/upstream-pk3ds.md) para el detalle de los cambios y cómo traer
actualizaciones de upstream.

## Licencia

GPL-3.0, heredada de pk3DS. Ver [LICENSE.md](LICENSE.md).

## Estructura

```
src/Pokemanager.Formats   pk3DS.Core sin WinForms: GARC, LZ11, BLZ, estructuras Gen 6
src/Pokemanager.Model     volcado (Dump/), datos (Data/), ediciones (Edits/, Editing/), proyecto, construcción del mod (Build/)
src/Pokemanager.Bridge    emulador: carpeta de usuario y de mods
src/Pokemanager.App       interfaz Avalonia
tests/Pokemanager.Tests   pruebas (xUnit)
```

## Compilar

Requiere el SDK de .NET 10.

```
dotnet build
dotnet test
```

Las pruebas usan Microsoft Testing Platform (`global.json`) y xUnit v3.

### Pruebas con un volcado real

Algunas pruebas necesitan un volcado descifrado de Pokémon X. Se omiten, no fallan, si no está
disponible. Para ejecutarlas, apunta `POKEMANAGER_DUMP` a la carpeta que contiene `romfs` y `exefs`
(`code.bin` descomprimido):

```
$env:POKEMANAGER_DUMP = "G:\pokemanager-dump"   # PowerShell
dotnet test
```
