# Pokemanager

Editor + randomizador + tablero en vivo para Pokémon X/Y (3DS), sobre Azahar.

Este documento es el traspaso de una sesión de investigación previa. Todo lo marcado
como **verificado** se leyó directamente del código fuente de pk3DS y de Azahar, no de foros.

---

## 1. Qué es

Un intermediario entre tres cosas: el volcado del juego del usuario, el emulador, y el usuario.

**Regla que lo gobierna todo:** la aplicación nunca modifica el volcado. Es de solo lectura,
siempre. Todo lo que produce va a la carpeta de mods de Azahar, que puede borrarse entera
sin perder nada porque se regenera desde `volcado + proyecto`.

### Las capas (el orden importa y no es el obvio)

```
volcado original  →  randomización(semilla)  →  ediciones manuales  →  construir
```

El random baraja sobre el original; las ediciones manuales se aplican encima. Así la
edición manual siempre gana, y el resultado sigue siendo reproducible porque el random
solo depende de (volcado, semilla, configuración).

Consecuencias: cambiar la semilla no destruye las ediciones; borrar las ediciones devuelve
el random puro; el proyecto compartible pesa KBs y no contiene ni un byte del juego.

### Direccionamiento de las ediciones

Una edición es `{tabla, id, campo, valor}`. Neutro respecto al formato en disco. Es lo que
permite que la capa de ediciones sobreviva a cambios del randomizador y se lea en un diff.

---

## 2. Decisiones ya tomadas

| Decisión | Elección |
|---|---|
| Juego objetivo | Pokémon X / Y (Gen 6) |
| Base de código | Fork de `pk3DS.Core` (kwsch/pk3DS) |
| Lenguaje | C# / .NET 10 |
| Interfaz | Avalonia (escritorio, multiplataforma) |
| Emulador | Azahar 2126.1.1 |
| Primer hito | El ciclo editar → ver en el juego |

**Licencia: pk3DS es GPL-3.0.** Construir sobre él obliga a publicar este proyecto bajo
GPL-3 con el código fuente disponible. Decisión asumida.

### Reorientación del producto (2026-09-13) — manda sobre lo anterior

El usuario hoy juega así: **Universal Pokémon Randomizer ZX** (`PokeRandoZX.jar` 4.6.1, presets
`.rnqs` por grupo de amigos) genera la ROM, y **PKHeX** retoca la partida. Lo que quiere es **una
sola app que junte ambas cosas**, no un editor exhaustivo de datos de la ROM:

| Decisión | Elección |
|---|---|
| Motor de randomización | **UPR ZX por debajo**, no randomizador propio. Se invoca con Java (JDK 25 instalado) mediante un lanzador propio que llama a `Randomizer.randomize(archivo, log, semilla)`, porque el CLI de UPR (`cli -s -i -o -d -u -l`) **no permite fijar la semilla**. Salida LayeredFS (`-d`) directa a la carpeta de mods. |
| Semilla | Reproducible y cambiable desde la app. Avisar si hay partida en curso: los Pokémon ya capturados conservan especie, pero stats base/habilidades/learnsets salen de la ROM y cambian. |
| Editor de partida | **PKHeX.Core** (NuGet, GPL-3) sobre el archivo `main` del emulador, **con el emulador cerrado** y copia de seguridad automática antes de escribir. En vivo (RPC) queda para después. |
| Qué se edita de la partida | Pokémon de equipo y cajas, mochila, datos del entrenador, Pokédex. |
| Validación | Dos niveles: **«seguro para el juego»** (IDs existentes, rangos, checksums) bloquea al guardar; **legalidad de PKHeX** solo avisa, porque compara con el juego original y marcaría como ilegal lo que el random hace válido. |
| Editor manual de la ROM ya hecho | Se mantiene como pestaña **avanzada**, aplicado encima del resultado randomizado. |
| Orden | **Fase A: randomizer** → Fase B: editor de partida → después, tablero en vivo. |

Partida de X en Azahar: `%APPDATA%\Azahar\sdmc\Nintendo 3DS\000…0\000…0\title\00040000\00055d00\data\00000001\main` (415 232 B).
UPR ZX y PKHeX del usuario: `D:\citra\roms\`.

**Fase A hecha** (`Pokemanager.Randomizer`, pestaña Randomizer). **Verificado** con UPR ZX 4.6.1,
preset `alex.rnqs` y el volcado real:
- `upr/PokemanagerUpr.java` se ejecuta con el lanzador de código fuente de Java (`java -cp jar X.java`).
  Repite `CliRandomizer.performDirectRandomization` (Gen6RomHandler, `Settings.read`, `tweakForRom`,
  bundle `com/dabomstew/pkrandom/newgui/Bundle`, `new Randomizer(settings, handler, bundle, true)`).
- Salida `-d`: `<salida>/0004000000055D00/{romfs/…, code.bin}`, 24 archivos, ~27 MB, ~9 s. `code.bin`
  sale **descomprimido**; se instala en `exefs/code.bin`. UPR también toca `.cro` del romfs.
- **Misma semilla → archivos idénticos byte a byte.** El log solo difiere en «Time elapsed».
- El log está en secciones `--Título--` (15 con ese preset).

Pendiente de la fase A: **comprobarlo dentro del juego**. Idea para la siguiente: importar
semilla + «Settings String» desde un log de UPR, para reproducir un `.cxi` ya randomizado con el
que haya una partida en curso.

---

## 3. pk3DS — cómo reutilizarlo

Repo: https://github.com/kwsch/pk3DS · Último commit 27-feb-2026 ("Update to .NET 10").
99 archivos en `pk3DS.Core`, 116 en `pk3DS.WinForms`.

### El problema a resolver primero

`pk3DS.Core.csproj` declara `<TargetFrameworks>net10.0-windows</TargetFrameworks>` y
`<UseWindowsForms>true</UseWindowsForms>`. **No es una librería portable.** Como la interfaz
va a ser Avalonia (multiplataforma), desacoplarlo no es opcional.

**Resuelto** (ver `docs/upstream-pk3ds.md`). La contaminación era de dos tipos:

1. **WinForms** — 6 archivos de `CTR/` aceptaban `RichTextBox`/`ProgressBar` como reporte de
   progreso: `BLZ.cs`, `CRO.cs`, `CTR.cs`, `NCCH.cs`, `NCSD.cs`, `RomFS.cs`. Sustituidos por
   `IProgress<string>` e `IProgress<ProgressState>`. `Properties/Resources.resx` solo nombra
   `ResXFileRef` de WinForms y el SDK de .NET lo compila sin tocarlo.
2. **`System.Drawing` y código nativo** — no aparecía en la investigación inicial. `System.Drawing`
   solo funciona en Windows en .NET moderno. Afecta a `ImageUtil.cs`, `CTR/SMDH.cs`,
   `CTR/Images/**` y `Structures/TypeChart.cs`; `CTR/ETC1.cs` además carga una `ETC1Lib.dll`
   nativa de Windows. Son gráficos: **excluidos de la compilación**, no borrados. Nada de
   `Game/`, `Structures/` ni `Randomizers/` depende de ellos.

Lo que de verdad se necesita compila limpio: `Game/`, `Structures/`, `Randomizers/`,
`CTR/GARC.cs`, `CTR/LZSS.cs`, `CTR/mini.cs`.

### Lo que ya trae hecho

- **Editores Gen 6:** personal, learnsets, evoluciones, movimientos huevo, movimientos,
  objetos, TM/HM, tutores, tabla de tipos, iniciales, encuentros estáticos, regalos,
  tiendas, Pickup, megaevoluciones, Maison, pantalla de título, `RSTE` (entrenadores),
  `XYWE` (salvajes), `OWSE` (scripts), editor de textos.
- **Randomizadores en `pk3DS.Core/Randomizers/`:** especies, movimientos, info de
  movimientos, learnsets, evoluciones, movimientos huevo, formas, personal, genérico.

### Trampa heredada

Identifica el juego **contando archivos en la carpeta `a/`**: 271 = X/Y, 299 = ORAS,
311 = SM, 333 = USUM. Sin hash ni cabecera. Con un dump raro falla en silencio.

**Reforzado** en `Pokemanager.Model/Dump/DumpInspector.cs`. Además del conteo comprueba:
- que los GARC de firma sean VER_4 con su número de entradas (personal 800, move 618, levelup 799,
  evolution 799; medidas sobre un volcado real de X);
- el título SMDH de `exefs/icon.bin`, que es lo único que **distingue X de Y** (el conteo no puede);
- que `code.bin` no tenga pie BLZ, es decir, que esté descomprimido.

Informa de todos los problemas a la vez.

Índice de idioma de pk3DS en X/Y (se suma a gametext 072 / storytext 080): 0 kana, 1 kanji,
2 inglés, 3 francés, 4 italiano, 5 alemán, 6 español, 7 coreano.

---

## 4. Mapa del RomFS de X/Y — **verificado**

Fuente: `pk3DS.Core/Game/GARCReference.cs`. Regla: el número `NNN` → ruta `a/N/N/N`.

| Núm | Ruta | Contenido |
|---|---|---|
| 005 | `a/0/0/5` | movesprite |
| 012 | `a/0/1/2` | encdata (encuentros salvajes) |
| 038 | `a/0/3/8` | trdata |
| 039 | `a/0/3/9` | trclass |
| 040 | `a/0/4/0` | trpoke |
| 041 | `a/0/4/1` | mapGR |
| 042 | `a/0/4/2` | mapMatrix |
| 072 + idioma | `a/0/7/x` | gametext |
| 080 + idioma | `a/0/8/x` | storytext |
| 104 | `a/1/0/4` | wallpaper |
| 165 | `a/1/6/5` | titlescreen |
| 203–206 | `a/2/0/3` … | maison |
| 212 | `a/2/1/2` | move |
| 213 | `a/2/1/3` | eggmove |
| 214 | `a/2/1/4` | levelup |
| 215 | `a/2/1/5` | evolution |
| 216 | `a/2/1/6` | megaevo |
| **218** | **`a/2/1/8`** | **personal** ← objetivo del hito 1 |
| 220 | `a/2/2/0` | item |

### Formatos

- Contenedor GARC **versión `0x0400`** (`GARC.VER_4`) en X/Y y ORAS. Gen 7 usa `0x0600`.
- `PersonalInfoXY`: **`0x40` bytes** por entrada. `a/2/1/8` tiene 800 archivos: 799 entradas
  (especies 0–721 y formas alternativas desde 722) y, como **último** archivo, la
  **concatenación de las 799 entradas** (verificado con el volcado real; pk3DS construye
  `PersonalTable` desde él). Al modificar una entrada hay que actualizar también esa copia.
- Archivos de `a/2/1/2` (move) de 36 bytes, aunque `Move6` solo interpreta 0x22.
- `Learnset6`: pares `int16` (movimiento, nivel) con terminador.
- En X/Y cada movimiento es un archivo suelto del GARC. En ORAS y Gen 7 van empaquetados
  en un contenedor "mini" `WD` — ese código **no** se comparte entre generaciones.

### Fuera del RomFS

**Las MT y los tutores viven en `.code.bin` (ExeFS)**, y tiene que estar **descomprimido**
(BLZ) para editarlo. Consecuencia: la app necesita dos carpetas del usuario, `romfs` y
`exefs`, ambas de un dump descifrado.

---

## 5. Azahar — **verificado**

Repo: https://github.com/azahar-emu/azahar · muy activo.
Versión en uso: **2126.1.1**, build `azahar-windows-msvc` (portable).
Fuente de todo lo de abajo: `src/core/file_sys/ncch_container.cpp`, `src/common/common_paths.h`,
`src/common/settings.h`, `dist/scripting/citra.py`.

### Title IDs

- Pokémon X → `0004000000055D00`
- Pokémon Y → `0004000000055E00`

### Dónde está la carpeta de usuario

- **Portable** (el zip): carpeta `user` junto a `azahar.exe` → `<dir>\user\load\mods\...`
- **Instalado en Windows:** `%APPDATA%\Azahar\load\mods\...`

### Gancho 1 — LayeredFS (RomFS)

```
<user>/load/mods/<ProgramID 16 hex>/romfs/
<user>/load/mods/<ProgramID 16 hex>/romfs_ext/    ← para archivos que no existen en el original
```

Overlay a nivel de archivo: solo se colocan los GARC modificados. `GetModId` enmascara el
bit de actualización, así que el mod se aplica también sobre la versión parcheada del juego.

Alternativa bruta: un archivo `<rom>.romfs` reemplaza el RomFS entero y desactiva LayeredFS.

### Gancho 2 — ExeFS / code.bin

Dos vías, y una es claramente mejor:

1. `load/mods/<TID>/exefs/code.bin` — reemplazo completo. **Debe ir descomprimido**: la ruta
   de override se resuelve *antes* de la descompresión LZSS del código original.
2. `load/mods/<TID>/exefs/code.ips` o `code.bps` — parche sobre el código **ya descomprimido**.
   **Esta es la vía buena**: unos KB en lugar de un binario de varios MB por cada cambio de MT.

### Gancho 3 — RPC de memoria

- UDP, puerto **45987**.
- Operaciones: `ReadMemory`, `WriteMemory`, `ProcessList`, `SetGetProcess`.
- **Máximo 1024 bytes de datos por paquete** → toda lectura grande hay que trocearla.
- `Settings::values.enable_rpc_server` arranca en **`false`**. El usuario lo activa a mano;
  esto va en la guía de primeros pasos, no enterrado en un FAQ.
- Protocolo documentado por implementación en `scripting/citra.py` (viene dentro del zip).
- Clientes existentes de referencia: [CitraRNG](https://github.com/Admiral-Fish/CitraRNG),
  [PKHeXRNG](https://github.com/kwsch/PKHeXRNG).

---

## 6. Riesgos

1. **No existe un mapa de memoria de X/Y que se pueda copiar.** Azahar da el canal, no las
   direcciones. Party, mapa actual y estado del RNG hay que sacarlos de CitraRNG y de códigos
   ActionReplay de Project Pokémon, y **son específicas de versión** (1.0 ≠ 1.5). Es la parte
   más artesanal del proyecto.
2. **El RPC es API interna, no un contrato.** Aislar el cliente detrás de una interfaz propia.
3. **1024 B/paquete no es un stream.** Diseñar con caché y sondeo a 5–10 Hz, no a 60 fps.
4. **El usuario debe aportar un dump descifrado** (romfs + exefs). La app no puede descifrar nada.
5. **No escribir "Citra" ni "Azahar" en la arquitectura.** Llamarlo "emulador" y meter la
   conexión detrás de una capa fina.

---

## 7. Hito 1 — el ciclo editar → ver en el juego

El objetivo no es tener un editor: es cerrar el circuito completo una vez.

1. **[Hecho]** Fork de pk3DS. Desacoplar `pk3DS.Core` de WinForms (`IProgress<T>` en los 7 archivos de
   `CTR/`), target `net10.0`. Verificar que compila fuera de Windows.
2. **[Hecho]** Envoltura headless (`GameDump.Open`): abrir la carpeta `romfs`, contar archivos en `a/`, construir el
   `GameConfig` y llamar a `Initialize(romfs, exefs, idioma)`.
3. **[Hecho]** Capa de ediciones (`Model/Edits`, `EditorSession`, proyecto JSON en `Model/Projects`): modelo `{tabla, id, campo, valor}` y su aplicación sobre el modelo.
4. **[Hecho]** (`Model/Build/ModBuilder`, también move y learnsets) Leer `a/2/1/8`, modificar los stats base de una especie y reempaquetar con
   `GARC.PackGARC(byte[][], GARC.VER_4, padding)`.
5. **[Hecho]** **Escribir el resultado en `<user>/load/mods/0004000000055D00/romfs/a/2/1/8`** — no encima
   del romfs original. Este paso define la arquitectura de toda la aplicación.
6. Arrancar Azahar y comprobar el cambio dentro del juego.
7. **[Hecho, ampliado]** Interfaz en Avalonia 12 (`Pokemanager.App`): Pokémon (datos + learnset),
   movimientos, Guardar y Generar mod. Verificada con capturas headless sobre el volcado real.

**Pendiente del hito 1: el paso 6**, comprobar el cambio dentro del juego en Azahar.

### Estructura de solución propuesta

```
Pokemanager.Formats   fork de pk3DS.Core sin WinForms — lo único que sabe de GARC/LZ11/code.bin
Pokemanager.Model     modelo del juego, capa de ediciones, randomización, construcción
Pokemanager.Bridge    puente con el emulador (UDP 45987, lanzar/recargar)
Pokemanager.App       Avalonia
Pokemanager.Tests
```

Nota: con un solo juego objetivo, no abstraer `Model` respecto a las estructuras de pk3DS más
de lo necesario. Lo único que sí conviene mantener neutro desde el día uno es el
direccionamiento de las ediciones.

---

## 8. Alcance de la primera versión

**Sí:** stats y tipos, movimientos, learnsets, evoluciones, entrenadores, encuentros salvajes,
objetos, MT.

**Todavía no:** textos, scripts de eventos, gráficos, mapas, modelos 3D.

## 9. Pendiente de averiguar

- Si Azahar permite recargar el juego por línea de órdenes o hay que cerrar y reabrir.
- Direcciones de memoria de X/Y para party, mapa actual y RNG (ver riesgo 1).
