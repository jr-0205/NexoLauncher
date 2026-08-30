# NEXO Performance Module

Estado: primera fase integrada en NEXO 0.5.2.

## Objetivo

Reducir trabajo que el launcher puede imponer al cliente de Minecraft sin aplicar flags experimentales, modificar mundos ni sobrescribir opciones gráficas del usuario.

## Cambios automáticos

### Logging desacoplado del frame loop

Minecraft mantiene stdout/stderr capturados para diagnóstico, pero NEXO ya no fuerza un `Flush()` al disco por cada línea. Las líneas se mantienen en buffer y se vacían aproximadamente una vez por segundo, además de realizar un flush final al terminar el proceso.

Esto reduce I/O síncrono especialmente en Forge/NeoForge/Fabric con mods que escriben mucho al log.

### Heap inicial adaptativo

NEXO mantiene `Xmx` como el límite configurado por el usuario/instancia, pero calcula `Xms` como aproximadamente una cuarta parte del máximo, con límites de 512 MiB y 2 GiB.

Ejemplos:

- Xmx 2 GiB -> Xms 512 MiB
- Xmx 4 GiB -> Xms 1 GiB
- Xmx 8 GiB -> Xms 2 GiB

Así se evita tanto el crecimiento excesivamente frecuente desde 512 MiB en perfiles grandes como reservar todo `Xmx` desde el inicio.

### JVM moderna

Para Java 17 o superior, y sólo cuando el perfil no selecciona explícitamente otro garbage collector, NEXO aplica un perfil conservador de G1:

- `-XX:+UseG1GC`
- `-XX:MaxGCPauseMillis=100`
- `-XX:+ParallelRefProcEnabled`

Los argumentos explícitos de loader/instancia/usuario tienen prioridad. NEXO no agrega flags experimentales ni ajustes de servidor tipo Aikar.

### Prioridad de proceso

En Windows NEXO intenta ejecutar Java con prioridad `AboveNormal`. Si Windows no permite modificarla, el lanzamiento continúa con la prioridad normal; nunca se aborta Minecraft por este ajuste.

No se usa prioridad `High` ni `Realtime` porque pueden degradar la capacidad de respuesta del sistema.

## Diagnóstico

Cada log de lanzamiento ahora registra:

- Java utilizado;
- resumen del perfil de rendimiento;
- tiempo de preparación de natives;
- tiempo de creación del classpath;
- tiempo total previo a `Process.Start`;
- prioridad realmente aplicada al proceso.

Esto permite separar dos problemas distintos:

1. **arranque lento**: preparación, Java, loader o carga de mods;
2. **FPS/tirones dentro del juego**: render, generación de chunks, mods, GPU/CPU o configuración del cliente.

## Límites de esta fase

Un launcher no puede transformar por sí solo el renderer de Minecraft. Para mejoras grandes de FPS, NEXO debe ofrecer en una fase posterior un flujo opcional de optimización por instancia que instale únicamente componentes compatibles con el loader y la versión seleccionados, con confirmación explícita y posibilidad de reversión.

Ese flujo no debe modificar automáticamente perfiles existentes sin consentimiento.
