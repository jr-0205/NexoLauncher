# NEXO Competitive Roadmap

## Visión

NEXO debe ser un launcher/cliente nativo de Windows que combine:

- la solidez de un gestor de instancias y contenido moderno;
- rendimiento de juego optimizado y configurable;
- una experiencia visual rápida y coherente;
- aislamiento y recuperación de datos como requisito no negociable.

La meta no es copiar Lunar Client ni Modrinth App. NEXO debe implementar capacidades equivalentes con arquitectura, UX, código y marca propios.

## Principios de producto

1. **Nunca romper una instancia para optimizarla.** Toda mutación importante debe ser reversible o transaccional.
2. **Un perfil es una unidad de producto.** Minecraft, loader, mods, configuración, mundos, Java y rendimiento se administran desde el mismo lugar.
3. **Automático por defecto, avanzado cuando se necesita.** Java, RAM, versiones compatibles y optimizaciones deben resolverse sin exigir conocimiento técnico.
4. **Rendimiento medible.** Evitar promesas genéricas de FPS; registrar tiempos de preparación, errores, versión Java y configuración aplicada.
5. **Fuentes explícitas.** Todo contenido instalado debe recordar de dónde provino, qué versión fue instalada y quién lo administra.
6. **Sin lock-in.** Importar/exportar perfiles y modpacks debe seguir siendo posible.

## Pilar A — NEXO Boost

Estado: EN DESARROLLO en 0.5.2.

Objetivo: aumentar FPS y reducir stutter dentro del juego sin convertir NEXO en un modpack fijo.

- Preset automático por Minecraft + loader.
- Fabric: Sodium, Lithium, FerriteCore, ImmediatelyFast, Entity Culling, ModernFix cuando exista build compatible.
- NeoForge: equivalente compatible por versión.
- Forge: renderer compatible del ecosistema Forge más optimizaciones compatibles.
- Descargas desde Modrinth mediante API y hashes.
- No sobrescribir JARs del usuario.
- Manifiesto NEXO Boost con SHA-512.
- Desinstalación que sólo elimina archivos todavía idénticos a los instalados por NEXO.
- Detectar renderers preexistentes y evitar combinar Sodium/Embeddium/OptiFine/Rubidium/VulkanMod de forma insegura.

Siguiente nivel:

- perfiles `Compatible`, `Balanced`, `Maximum FPS`;
- configuración recomendada de vídeo por hardware;
- comparación antes/después usando FPS/frametime proporcionado voluntariamente por el usuario;
- actualización independiente de componentes Boost.

## Pilar B — Content Engine

Objetivo: competir con gestores de contenido dedicados.

Cada archivo administrado debe tener una entrada persistente:

```json
{
  "projectId": "...",
  "versionId": "...",
  "source": "modrinth",
  "file": "mods/example.jar",
  "sha512": "...",
  "enabled": true,
  "installedAt": "...",
  "managedBy": "user|modpack|nexo-boost"
}
```

Capacidades objetivo:

- ver mods instalados con nombre, icono, autor y versión;
- instalar, actualizar, desactivar y eliminar individualmente;
- selección múltiple y acciones masivas;
- identificar contenido del modpack base vs añadido por el usuario;
- actualización compatible con Minecraft + loader;
- changelog antes de actualizar;
- detectar conflictos y dependencias;
- rollback de actualización;
- exportación `.mrpack`.

## Pilar C — Snapshot & Recovery

Toda operación de riesgo debe crear un punto recuperable.

- snapshot antes de actualizar loader/modpack o conjunto grande de mods;
- manifest + archivos cambiados, no duplicar shared/;
- rollback desde UI;
- detectar cierre/interrupción durante una actualización;
- reparación de assets/libraries/versiones compartidas;
- diagnóstico de manifiestos corruptos y archivos faltantes.

## Pilar D — Accounts

- autenticación Microsoft/Xbox/Minecraft oficial;
- tokens en Windows Credential Manager;
- múltiples cuentas;
- selector rápido por perfil;
- offline sólo cuando sea técnicamente válido;
- nunca almacenar contraseña Microsoft.

## Pilar E — Play / Library UX

- vista Play como centro del launcher;
- grupos y filtros de perfiles;
- iconos y fondos personalizados;
- favoritos/recientes;
- estado Boost, loader, versión y actualizaciones visibles sin abrir detalles;
- creación por wizard;
- importar desde filesystem/Modrinth/CurseForge/export NEXO;
- onboarding para primer uso;
- teclado y accesibilidad completos.

## Pilar F — Diagnostics

NEXO debe poder responder por qué un juego no abre o funciona mal.

- tiempos de preparación de launch;
- Java elegido y motivo;
- RAM efectiva;
- mods incompatibles conocidos;
- últimas líneas relevantes del log;
- crash report asociado;
- botón `Diagnosticar`;
- exportar paquete de diagnóstico sin tokens ni datos sensibles.

## Pilar G — NEXO In-Game

Fase posterior. Debe vivir como proyecto/módulo separado del launcher para no contaminar el Core.

Objetivo inicial:

- menú NEXO dentro del juego;
- FPS y frametime;
- keystrokes/CPS;
- coordenadas;
- zoom;
- toggle sprint;
- HUD editable;
- perfiles de HUD por servidor/perfil;
- controles y keybinds propios.

Debe utilizar APIs públicas del loader seleccionado y código propio. No copiar módulos, assets ni implementación de otros clientes.

## Orden de ejecución

### 0.5.2 — Stabilize + Boost foundation

- almacenamiento GUID/shared;
- imports transaccionales;
- UI Quality Module;
- JVM/launch performance;
- NEXO Boost reversible.

### 0.5.3 — Content Engine

- índice persistente de contenido;
- enable/disable/delete/update;
- bulk actions;
- origen de cada mod;
- snapshots antes de update.

### 0.5.4 — Repair + Update Engine

- reparación Vanilla/shared;
- actualización de loaders;
- migración de contenido entre versiones cuando exista build compatible;
- rollback.

### 0.6 — Accounts + production readiness

- Microsoft auth completa;
- múltiples cuentas;
- updater del propio NEXO;
- crash/diagnostic UX;
- instalación empaquetada y firma.

### 0.7 — NEXO In-Game foundation

- companion mod propio;
- HUD y QoL inicial;
- integración segura por loader/version.

## Criterio competitivo

NEXO sólo debe considerarse competitivo cuando un usuario pueda:

1. instalarlo y entrar con su cuenta sin tocar archivos manualmente;
2. crear/importar múltiples perfiles sin colisiones;
3. instalar y mantener mods/modpacks desde la propia app;
4. activar una optimización de FPS compatible y reversible;
5. actualizar contenido con preview y rollback;
6. reparar una instalación dañada sin reinstalar todo;
7. entender un crash desde la UI;
8. mover/exportar su perfil sin lock-in;
9. usar el launcher de forma fluida con teclado y resoluciones comunes;
10. jugar con rendimiento consistente sin flags JVM peligrosos ni manipulación destructiva.
