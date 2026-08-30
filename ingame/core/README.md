# NEXA In-Game Core

`core/` contiene la lógica que debe permanecer independiente de las clases de Minecraft y de un loader concreto.

Reglas del módulo:

- No importar `net.minecraft.*`, Fabric, Forge ni NeoForge.
- No leer directamente archivos del launcher.
- Definir modelos, catálogo de módulos, perfiles de rendimiento, HUD, configuración y contratos de plataforma.
- Las llamadas que dependen de Minecraft deben entrar por adaptadores (`NexaPlatform` y contratos derivados).
- El compilador v2 incorpora este árbol al workspace temporal de cada target.

Los targets existentes siguen actuando como adaptadores de transición hasta que todo acceso directo a Minecraft quede extraído de ellos.
