# Herramientas incluidas

Pokemanager se distribuye con estas herramientas; el usuario no tiene que instalarlas ni elegir su ruta.
Al compilar, esta carpeta se copia junto a los binarios de la aplicación (`<app>/tools/`).

| Carpeta | Herramienta | Versión | Licencia | Origen |
|---|---|---|---|---|
| `upr/` | Universal Pokémon Randomizer ZX (`PokeRandoZX.jar`) | 4.6.1 | GPL-3.0 | https://github.com/Ajarmar/universal-pokemon-randomizer-zx |

**PKHeX** no está en esta carpeta: se usa su núcleo, [PKHeX.Core](https://github.com/kwsch/PKHeX) (GPL-3.0),
como paquete NuGet dentro de `Pokemanager.Save`, así que va compilado dentro de la aplicación.

**Java** (11 o superior) sigue siendo necesario para UPR ZX y de momento se usa el instalado en el sistema.
Pendiente: incluir un runtime reducido con `jlink` al preparar el instalador.

Para actualizar UPR ZX: sustituir `upr/PokeRandoZX.jar`, actualizar la versión de esta tabla y pasar las
pruebas con `POKEMANAGER_DUMP` definida (el lanzador `PokemanagerUpr.java` usa API interna de UPR).
