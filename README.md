# NEXO Client

NEXO es un launcher nativo de Minecraft Java para Windows construido con C#/.NET 10 y WPF.

El objetivo del proyecto es mantener perfiles completamente aislados, reutilizar de forma segura los recursos pesados compartidos de Minecraft y ofrecer una experiencia de launcher rápida, clara y sin Electron, anuncios, overlays innecesarios ni telemetría.

## Estado: 0.5.2

La rama de estabilización 0.5.2 introduce una arquitectura de almacenamiento basada en identidad GUID, migraciones no destructivas, imports transaccionales, natives por lanzamiento, mejoras de UI y una primera capa de rendimiento para reducir el overhead que NEXO añade al cliente de Minecraft.

## Layout de datos

```text
%LOCALAPPDATA%\NexoLauncher\
├── shared\
│   ├── assets\
│   ├── libraries\
│   ├── versions\
│   └── runtimes\
│       └── java\
├── instances\
│   └── <GUID>\
│       ├── instance.json
│       ├── profile\
│       │   ├── artwork.json
│       │   ├── icon.*
│       │   └── background.*
│       ├── game\
│       │   ├── mods\
│       │   ├── config\
│       │   ├── saves\
│       │   ├── resourcepacks\
│       │   ├── shaderpacks\
│       │   ├── screenshots\
│       │   ├── logs\
│       │   └── crash-reports\
│       ├── runtime\
│       │   └── natives\
│       │       └── <launch-id>\
│       └── backups\
├── cache\
├── logs\
│   └── launcher\
└── launcher\
```

La versión de Minecraft no es la identidad de un perfil. Dos perfiles pueden usar la misma versión y el mismo loader sin compartir mods, configuración, mundos, opciones ni natives.

## Manifiesto de instancia

Los perfiles actuales utilizan `schemaVersion: 2` y almacenan únicamente configuración persistente. Rutas derivables como el classpath no se guardan.

Ejemplo reducido:

```json
{
  "schemaVersion": 2,
  "id": "8eddb6217a52483e8ba892017b899a80",
  "name": "Fabric 1.21",
  "minecraftVersion": "1.21.1",
  "loader": {
    "type": "fabric",
    "version": "0.16.10"
  },
  "java": {
    "mode": "automatic",
    "override": null
  },
  "memory": {
    "minMb": 512,
    "maxMb": 4096
  },
  "gameDirectory": "game"
}
```

Renombrar una instancia nunca mueve su carpeta física.

## Migración 0.5.1 → 0.5.2

NEXO migra de forma no destructiva:

- `versions`, `libraries`, `assets` y `runtime` antiguos hacia `shared/`;
- perfiles con rutas legibles `<Loader>/<Nombre>` hacia `instances/<GUID>`;
- manifiestos anteriores hacia el schema actual con backup;
- datos `game/` antiguos hacia una nueva instancia aislada cuando corresponde.

Los recursos compartidos existentes nunca se sobrescriben silenciosamente. Un duplicado heredado sólo se elimina automáticamente si su SHA-256 coincide con el recurso ya presente.

## Minecraft y loaders

Actualmente NEXO integra:

- Vanilla;
- Fabric;
- Forge;
- NeoForge.

Las bibliotecas, assets y versiones se comparten. Mods, configs, mundos y demás datos mutables viven dentro de cada instancia.

Los natives no se extraen globalmente durante la instalación. Cada lanzamiento obtiene un directorio privado bajo `runtime/natives/<launch-id>` y NEXO limpia directorios abandonados cuando puede demostrar que no pertenecen a un proceso vivo.

## Contenido y modpacks

### Modrinth

Los `.mrpack` oficiales se importan mediante staging transaccional:

1. se valida compatibilidad de Minecraft/loader;
2. se aplican `overrides` y `client-overrides` en staging;
3. se descargan archivos remotos por HTTPS;
4. se valida SHA-512 o SHA-1;
5. sólo después se publica el contenido sobre `game/`.

Si una descarga o hash falla, el perfil final no queda parcialmente importado.

### CurseForge

NEXO reconoce exports oficiales de CurseForge. Los overrides físicos pueden importarse sin credenciales.

Los archivos remotos requieren una **API key de desarrollador CurseForge**, no las credenciales de inicio de sesión de un usuario. Para desarrollo se admite `CURSEFORGE_API_KEY`. Una integración distribuida debe usar una arquitectura aprobada que no exponga la clave dentro del ejecutable cliente.

## NEXO Performance

La primera fase del módulo de rendimiento está integrada en 0.5.2:

- `Xms` adaptativo según el `Xmx` configurado;
- tuning conservador de G1 para Java 17+ cuando el perfil no selecciona otro collector;
- prioridad `AboveNormal` en Windows con fallback seguro;
- captura de stdout/stderr con buffering y flush periódico en lugar de escribir físicamente cada línea de log;
- medición del tiempo de preparación de natives, classpath y lanzamiento.

El launcher no modifica automáticamente la configuración gráfica ni instala mods de optimización sin consentimiento. El detalle técnico está en `docs/NEXO-PERFORMANCE.md`.

## UI Quality Module

`src/NexoLauncher.App/UI/` contiene la primera capa del design system de NEXO:

- tokens semánticos de color;
- marca vectorial propia;
- estilos reutilizables;
- estados hover/pressed/focus/disabled;
- biblioteca con búsqueda, continuación rápida y artwork por perfil;
- wizard guiado `Información → Versión del juego → Apariencia`;
- compatibilidad gradual con estilos legacy;
- primera adaptación responsive del shell principal.

El roadmap visual se documenta en `docs/NEXO-UI-QUALITY.md`.

## Java

Java se selecciona automáticamente según la versión de Minecraft. NEXO detecta runtimes locales, conserva un cache validado y evita arrancar una versión con un major incompatible.

Las instancias pueden definir un override de Java sin convertir ese override en una configuración global obligatoria.

## Compilar y ejecutar

Requiere el SDK fijado por `global.json`.

```powershell
dotnet restore NexoLauncher.slnx
dotnet build NexoLauncher.slnx
dotnet run --project tests/NexoLauncher.Core.Tests
dotnet run --project src/NexoLauncher.App
```

El harness imprime dinámicamente el total de checks aprobados.

## Créditos

NEXO Client es creado y mantenido por [jr-0205](https://github.com/jr-0205).

La aplicación incluye en **Configuración → Acerca de NEXO** accesos externos al perfil del creador y a la página oficial de descarga de ChatGPT: <https://chatgpt.com/download/>.

NEXO es un proyecto independiente. No está afiliado, patrocinado ni respaldado por OpenAI, Mojang/Microsoft, Modrinth o Lunar Client. Las marcas y servicios de terceros pertenecen a sus respectivos propietarios.

## Principios del proyecto

- integridad de datos antes que conveniencia;
- perfiles aislados físicamente por GUID;
- recursos costosos/imutables compartidos;
- operaciones destructivas limitadas al perfil seleccionado;
- imports y escrituras críticas transaccionales;
- migraciones recuperables y no destructivas;
- rutas críticas resueltas por abstracciones centrales;
- argumentos de lanzamiento tokenizados;
- extracción ZIP protegida contra path traversal;
- ningún merge a `main` durante estabilización sin autorización explícita.

## Pendiente después de 0.5.2

Entre las líneas de trabajo posteriores están:

- autenticación Microsoft oficial completa;
- integración de catálogo CurseForge mediante una arquitectura de credenciales aprobada;
- reparación integral de instalaciones compartidas dañadas o incompletas;
- un flujo opcional de optimización por instancia con componentes compatibles por loader/versión;
- seguir separando responsabilidades del `MainWindow` hacia servicios/view models;
- decidir la política UI para lanzamientos simultáneos aunque el filesystem ya esté diseñado para aislarlos.
