# Pokemanager

Editor, randomizador y tablero en vivo para Pokémon X/Y (3DS), pensado para usarse con un
emulador de 3DS mediante LayeredFS.

La aplicación no modifica nunca el volcado del juego: todo lo que genera va a la carpeta de
mods del emulador y se puede regenerar a partir de `volcado + proyecto`. El repositorio no
contiene datos del juego; cada usuario aporta su propio volcado descifrado.

> Estado: hito 1, pasos 1 y 2. Capa de formatos portable y apertura/validación del volcado.

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
src/Pokemanager.Model     modelo del juego: abrir y validar el volcado (Dump/); ediciones y randomización, pendiente
src/Pokemanager.Bridge    puente con el emulador (vacío)
src/Pokemanager.App       interfaz (vacío)
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
