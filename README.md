# NEXO Client

Launcher nativo y ligero para Minecraft Java en Windows, construido con C#/.NET 10 y WPF. NEXO no usa Electron, navegador integrado, anuncios, overlays ni telemetría propia.

La experiencia visual puede tomar referencias de nivel de pulido de otros launchers, pero NEXO mantiene código, arquitectura, identidad y componentes propios.

## Estado actual · NEXO 0.5.2

La línea 0.5.2 estabiliza la arquitectura interna antes de continuar agregando subsistemas mayores.

- Vanilla, Fabric, Forge y NeoForge integrados mediante providers de loader;
- múltiples perfiles de una misma versión y loader sin compartir datos mutables;
- identidad física permanente por GUID: el nombre visible nunca forma parte de la ruta de una instancia;
- recursos pesados reutilizables centralizados bajo `shared`;
- `game` privado por instancia para mods, config, mundos, opciones, logs y demás datos escritos por Minecraft;
- `instance.json` versionado (`schemaVersion`) como fuente persistente de configuración del perfil;
- migración automática del layout 0.5.1 hacia GUIDs sin destruir los datos del perfil;
- backups del manifest antes de transformar un perfil heredado;
- creación de perfiles mediante staging y publicación final;
- escritura atómica de manifests y launcher settings;
- Java Manager con detección de múltiples runtimes y selección automática por versión;
- Java override únicamente por instancia;
- RAM recomendada y límite seguro basados en memoria física;
- descargas oficiales de Minecraft por HTTPS con validación SHA-1 cuando Mojang proporciona hash;
- ZIP extraction protegida contra path traversal;
- natives extraídos por lanzamiento en un directorio privado y efímero, no en una carpeta global mutable;
- logs de proceso con nombre único por lanzamiento;
- editor de instancia y eliminación destructiva con confirmación explícita;
- duplicación completa de perfiles con nuevo GUID (menú contextual o `Ctrl+D` en la biblioteca);
- Content Manager para mods, resource packs, shaders, datapacks y archivos de configuración;
- catálogo Modrinth con filtros de versión/loader, dependencias requeridas y SHA-512;
- importación `.mrpack` oficial: descarga `files[]`, respeta `env.client`, aplica `overrides`/`client-overrides` y verifica SHA-512/SHA-1;
- importación de exports CurseForge con `manifest.json`, validación de versión/loader, hashes y overrides;
- visor de lanzamiento con PID, tiempo, log y detención del proceso;
- **UI Quality Module** con design tokens, componentes WPF centralizados, foco visible, estados coherentes, remapeo gradual de estilos legacy y primer breakpoint responsive;
- harness de regresión cuyo total se calcula dinámicamente para evitar documentación desactualizada;
- workflow de CI para Windows + .NET SDK `10.0.202` preparado en `.github/workflows/ci.yml`.

La especificación del módulo visual está en `docs/NEXO-UI-QUALITY.md`.

## Regla de arquitectura

> Compartir lo inmutable y costoso. Aislar todo lo mutable y perteneciente al usuario.

Una versión de Minecraft **no** es una instancia.

Ejemplos válidos en paralelo dentro de la biblioteca:

```text
Vanilla       -> Minecraft 1.21.1
Cobblemon     -> Minecraft 1.21.1 + Fabric
Survival      -> Minecraft 1.21.1
Testing       -> Minecraft 1.21.1 + NeoForge
Modpack A     -> Minecraft 1.21.1 + Fabric
Modpack B     -> Minecraft 1.21.1 + Fabric
```

Los perfiles pueden compartir Minecraft base, libraries, assets y runtimes Java. Nunca comparten accidentalmente `mods`, `config`, `saves`, `options.txt`, `servers.dat`, resource packs, shaders, logs del juego ni cualquier otro archivo escrito dentro de su `gameDirectory`.

## Árbol de datos

```text
%LOCALAPPDATA%\NexoLauncher\
├── shared\
│   ├── assets\
│   │   ├── indexes\
│   │   └── objects\
│   ├── libraries\
│   ├── versions\
│   │   └── <minecraft-version>\
│   └── runtimes\
│       └── java\
├── instances\
│   └── <INSTANCE-GUID>\
│       ├── instance.json
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
│       │       └── <launch-guid>\
│       └── backups\
├── cache\
├── logs\
│   └── launcher\
└── launcher\
```

Renombrar `Cobblemon` a `Cobblemon principal` solo actualiza metadata. La carpeta GUID no cambia.

## `instance.json`

El manifest actual usa schema versionado y rutas relativas. Conceptualmente:

```json
{
  "schemaVersion": 2,
  "id": "GUID",
  "name": "Cobblemon",
  "minecraftVersion": "1.21.1",
  "loader": {
    "type": "fabric",
    "version": "0.16.14"
  },
  "java": {
    "mode": "automatic",
    "override": null
  },
  "memory": {
    "minMb": 512,
    "maxMb": 6144
  },
  "gameDirectory": "game"
}
```

Classpath, Java seleccionado automáticamente, rutas finales de libraries y argumentos finales son estado derivado y se reconstruyen al iniciar.

## CurseForge

CurseForge distingue una cuenta de usuario de una credencial de su API para aplicaciones de terceros. NEXO **no** intenta obtener una API key mediante el login normal del usuario y no debe incrustar una key privada dentro del ejecutable distribuido.

Durante desarrollo, los exports oficiales de CurseForge que contienen referencias remotas pueden probarse configurando:

```powershell
$env:CURSEFORGE_API_KEY="<developer-api-key>"
```

Los packs que solo contienen `overrides` físicos pueden importarse sin esa key. Para búsqueda/instalación directa desde el catálogo CurseForge en una versión distribuida de NEXO se requiere una integración de terceros aprobada que mantenga la credencial fuera del cliente.

Además, la API de terceros respeta el control de distribución de cada proyecto: un archivo que su autor no permita distribuir mediante terceros no debe ser eludido por NEXO.

## Modrinth

La importación `.mrpack` ya no se limita a copiar archivos incluidos físicamente. NEXO interpreta `modrinth.index.json`, comprueba Minecraft/loader, descarga las entradas remotas compatibles con cliente, aplica overrides y valida hashes antes de publicar cada archivo dentro del `gameDirectory` de la instancia.

## Java

El flujo normal no necesita un Java global obligatorio:

```text
Instancia
  -> metadata oficial de Minecraft
  -> requisito Java
  -> catálogo local de runtimes
  -> Java compatible
  -> launch
```

Cuando Mojang publica `javaVersion.majorVersion`, esa metadata tiene prioridad. Las tablas internas se usan solamente como fallback histórico.

## Ejecutar

```powershell
dotnet run --project src/NexoLauncher.App
```

## Verificar

El repositorio fija .NET SDK `10.0.202` mediante `global.json`.

```powershell
dotnet restore NexoLauncher.slnx
dotnet build NexoLauncher.slnx -c Release
dotnet run --project tests/NexoLauncher.Core.Tests -c Release --no-build
```

El harness comprueba, entre otros casos, perfiles con la misma versión/loader/nombre, renombrado sin movimiento físico, aislamiento de mods/config/saves/options, borrado sin tocar recursos compartidos, copia de perfil, manifests versionados, migraciones, staging abandonado, ZIP traversal, Java, loaders, `.lcpack`, `.mrpack` y CurseForge.

## Alcance pendiente

La rama 0.5.2 se concentra en integridad de datos y estabilización. Continúan como trabajo posterior:

- autenticación Microsoft/Xbox/Minecraft integrada al flujo normal;
- backend/integración de producción aprobada para catálogo CurseForge sin exponer credenciales;
- diagnóstico y reparación automática más amplia de instalaciones incompletas;
- evolución del UI Quality Module: biblioteca, flujo de instalación, Content Hub, Settings/Accounts y eliminación progresiva de estilos locales legacy;
- política/UI futura para múltiples Minecraft simultáneos (el filesystem ya no depende de un único directorio mutable);
- Mission Control y módulo in-game como subsistemas separados.

La autenticación local actual no equivale a una cuenta comprada y no sustituye la autenticación oficial de Minecraft.
