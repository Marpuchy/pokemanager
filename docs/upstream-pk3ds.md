# pk3DS upstream en `src/Pokemanager.Formats`

`src/Pokemanager.Formats/` es la carpeta `pk3DS.Core/` de
[kwsch/pk3DS](https://github.com/kwsch/pk3DS), importada con `git subtree --squash`.

| | |
|---|---|
| Commit de upstream importado | `6daaca934ca2284a73ab743bf89c848c57cd9de1` (2026-02-27, "Update to .NET 10") |
| Commit del split de `pk3DS.Core` | `70e3c69915e1a689eadf43ad254ac4e8ac21ac1f` |
| Licencia | GPL-3.0 |

## Cambios locales respecto a upstream

Se mantienen en commits propios, separados de los de importación, para que `git log` y
`git diff` contra upstream muestren solo lo nuestro.

1. **`pk3DS.Core.csproj` → `Pokemanager.Formats.csproj`.** Target `net10.0` (antes
   `net10.0-windows` con `UseWindowsForms`). `RootNamespace` sigue siendo `pk3DS.Core`: no se
   renombran namespaces para no generar conflictos en cada fusión.
2. **Progreso sin WinForms** en `CTR/BLZ.cs`, `CRO.cs`, `CTR.cs`, `NCCH.cs`, `NCSD.cs` y
   `RomFS.cs`:
   - `RichTextBox` → `IProgress<string>` (una línea de registro por llamada).
   - `ProgressBar` → `IProgress<ProgressState>` (`CTR/ProgressState.cs`, archivo nuevo).
   - Los nombres de parámetro originales (`TB_Progress`, `PB_Show`) se conservan para
     minimizar el diff. `null` = sin informe.
3. **Excluido de compilación** (`<Compile Remove>` en el csproj; los archivos siguen en disco):
   `ImageUtil.cs`, `CTR/ETC1.cs`, `CTR/SMDH.cs`, `CTR/Images/**`, `Structures/TypeChart.cs`.
   Dependen de `System.Drawing` (solo Windows en .NET moderno) o de la `ETC1Lib.dll` nativa
   de Windows. Son gráficos; nada de `Game/`, `Structures/` ni `Randomizers/` depende de ellos.
4. **`GARC.MemGARC.Data` pasa de `internal` a `public`** (`CTR/GARC.cs`, un solo token). Son los
   bytes del GARC empaquetado: los necesita Model para escribir en la carpeta de mods del
   emulador. En upstream solo eran accesibles desde dentro de la librería.

5. **Fallo corregido en `CTR/NCSD.cs` (`ExtractCXIfromNCSD`).** El bucle copiaba 10 unidades
   de 0x200 bytes por vuelta durante `ncchSize` vueltas: generaba un `game.cxi` 10 veces mayor
   (~17 GB con Pokémon X) relleno de basura tras el EOF. Ahora copia el tamaño exacto. Candidato
   a enviarse como PR a upstream.

`Properties/Resources.resx` no se ha tocado: sus entradas son `ResXFileRef` que el SDK de .NET
compila sin WinForms, y las necesitan `Exheader` y la construcción de `.3ds` en `CTR.cs`.

## Traer cambios de upstream

`git subtree split` necesita que la ruta exista en el HEAD del repo donde se ejecuta, así que el
split se hace en un clon de pk3DS aparte (por ejemplo `../pokemanager-ref/pk3DS`). El split es
determinista: el mismo historial produce los mismos hashes, y por eso las fusiones posteriores
encuentran el punto común con la importación anterior.

```sh
# 1. En el clon de pk3DS
cd ../pokemanager-ref/pk3DS
git pull origin master
git branch -f pk3ds-core-split "$(git subtree split --prefix=pk3DS.Core master)"

# 2. En este repo (árbol de trabajo limpio)
cd ../../pokemanager
git subtree pull --squash --prefix=src/Pokemanager.Formats ../pokemanager-ref/pk3DS pk3ds-core-split
```

Tras fusionar, actualizar la tabla de arriba y el commit de `NOTICE`.

Qué puede chocar:

- **Los 6 archivos de `CTR/`** si upstream cambia sus firmas de progreso.
- **El csproj**: si upstream toca `pk3DS.Core.csproj`, git lo verá como modificación de un
  archivo que aquí se renombró. Revisar a mano y trasladar lo relevante.
- **Archivos nuevos** que usen `System.Drawing` o WinForms romperán la compilación: añadirlos al
  `<Compile Remove>` o adaptarlos.
