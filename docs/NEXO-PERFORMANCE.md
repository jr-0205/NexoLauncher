# NEXO Performance Module

Estado: primera fase integrada en NEXO 0.5.2.

## Objetivo

Reducir trabajo que el launcher puede imponer al cliente de Minecraft sin aplicar flags experimentales, modificar mundos ni degradar automáticamente toda la calidad visual.

## Cambios automáticos del launcher

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

## NEXO Boost

NEXO Boost instala, por instancia, optimizaciones compatibles con la versión exacta de Minecraft y el loader. Las descargas se resuelven desde Modrinth y se publican mediante staging/rollback.

Los archivos administrados por Boost se registran por SHA-512. Al desactivar Boost, NEXO sólo elimina un archivo si continúa siendo exactamente el que instaló; un mod actualizado o modificado por el usuario se conserva.

### Preset Equilibrado

`NEXO Boost · Equilibrado (recomendado)` es el perfil visual predeterminado. Su objetivo es aumentar FPS sin convertir Minecraft en un preset de gráficos mínimos.

Conserva deliberadamente:

- modo gráfico actual;
- ambient occlusion;
- sombras de entidades;
- nubes;
- mipmaps;
- partículas globales en `ALL`;
- barrido de espada, críticos, indicador de daño, golpe encantado, tótem y corazones al 100%.

Sólo limita cuando el valor actual excede el techo equilibrado:

- render distance: máximo 12 chunks;
- simulation distance: máximo 8 chunks;
- entity distance scaling: máximo 0.85;
- biome blend radius: máximo 2.

El preset añade un módulo visual basado en Particle Core cuando existe una build compatible. Particle Core optimiza el pipeline de partículas y permite reducción por tipo. Cuando su archivo de configuración ya existe, NEXO reduce selectivamente partículas ambientales como goteos, lluvia, partículas submarinas, ceniza y esporas, sin reducir las señales de combate.

La primera ejecución de Particle Core genera su configuración. Por eso, después del primer inicio con Boost, `Ctrl+B` o volver a elegir `NEXO Boost · Equilibrado` vuelve a aplicar el preset y afina automáticamente ese archivo.

### Reversibilidad visual

El preset registra únicamente los valores que administra. Al desactivar Boost:

- un valor de `options.txt` se restaura sólo si todavía conserva el valor que NEXO aplicó;
- una opción de Particle Core se restaura sólo si no fue modificada después;
- cualquier cambio manual posterior del usuario se preserva.

## Diagnóstico

Cada log de lanzamiento registra:

- Java utilizado;
- resumen del perfil de rendimiento;
- tiempo de preparación de natives;
- tiempo de creación del classpath;
- tiempo total previo a `Process.Start`;
- prioridad realmente aplicada al proceso.

Esto permite separar dos problemas distintos:

1. **arranque lento**: preparación, Java, loader o carga de mods;
2. **FPS/tirones dentro del juego**: render, generación de chunks, mods, GPU/CPU o configuración del cliente.

El objetivo de NEXO Performance es mejorar el segundo caso sin destruir la experiencia visual ni comprometer la integridad de la instancia.
