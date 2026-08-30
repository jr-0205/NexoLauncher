# NEXA In-Game Adapters

Los adaptadores contienen únicamente integración que cambia entre familias de Minecraft/loader. El código de producto debe permanecer en `../core` siempre que sea posible.

## Familias actuales

- `fabric-1.19`: integración de la línea 1.19.x.
- `fabric-1.20.1`, `fabric-1.20.4`, `fabric-1.20.6`: se mantienen separados mientras las diferencias de GUI/render/input sigan siendo materiales.
- `fabric-1.21`: familia de referencia para 1.21.1/1.21.4/1.21.8. Durante la migración, su implementación física sigue en `fabric-1.21.1/src/client/java` y los targets compatibles la consumen como source companion.

`ingame/targets.json` es la autoridad para saber qué target usa cada adaptador y qué proyectos companion necesita el Compiler v2.

## Regla de dependencia

Permitido:

`target -> adapter -> core`

No permitido:

`core -> net.minecraft.*`

`core -> Fabric/Forge/NeoForge`

`core -> target concreto`

Cuando una API de Minecraft cambie, se crea o divide un adaptador; no se duplica el catálogo de módulos, perfiles, configuración, HUD o UI de NEXA.
