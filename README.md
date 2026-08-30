# NEXA Client

NEXA Client es un launcher nativo de Minecraft Java para Windows, construido sobre .NET 10 con un host WPF/WebView2 y una interfaz React. El proyecto prioriza perfiles aislados, control explícito del contenido instalado y una experiencia rápida sin Electron, anuncios ni telemetría.

## NEXA Client 1.0.0

**Primera versión instalable pública para Windows x64.**

[⬇️ Descargar NEXA Client 1.0.0 para Windows x64](https://github.com/jr-0205/NexoLauncher/releases/download/v1.0.0/NEXA-Client-Setup-1.0.0-win-x64.exe)

El instalador es autocontenido: no requiere que el usuario instale previamente el runtime de .NET. Se genera con `tools/build-installer.ps1` e Inno Setup 6.

> Si GitHub todavía está procesando la primera publicación, abre la sección **Releases** del repositorio y selecciona `NEXA Client 1.0.0`.

## Qué incluye 1.0.0

- perfiles físicamente aislados por GUID;
- soporte para Vanilla, Fabric, Forge y NeoForge;
- selección automática de Java según la versión de Minecraft;
- memoria RAM configurable por perfil con límites seguros;
- importación de modpacks y gestión de contenido instalado;
- iconos reales de mods y packs cuando el archivo proporciona metadata compatible;
- NEXA Boost y presets de rendimiento;
- NEXA In-Game con compilador v2 y adaptadores por versión;
- consola/logs en vivo por perfil;
- artwork configurable por perfil con posición y zoom persistentes;
- UI React integrada en un host nativo de Windows;
- branding oficial NEXA integrado en launcher y recursos del cliente;
- instalador Windows x64 y desinstalación mediante Inno Setup.

## Arquitectura

```text
NEXA Client
├── NexaLauncher.Desktop       # Host WPF/WebView2 de Windows
├── NexaLauncher.UI            # Interfaz React + TypeScript
├── NexoLauncher.Application   # Casos de uso
├── NexoLauncher.Domain        # Modelo de dominio
├── NexoLauncher.Infrastructure
├── NexoLauncher.Minecraft     # Instalación y lanzamiento de Minecraft
├── NexoLauncher.Java          # Detección/selección de Java
└── ingame/                    # NEXA In-Game + Compiler v2
```

Los nombres internos `NexoLauncher` se conservan temporalmente donde cambiarlos rompería compatibilidad o rutas existentes. El branding visible del producto es **NEXA Client**.

## Datos y perfiles

Los datos de ejecución viven en:

```text
%LOCALAPPDATA%\NexoLauncher\
├── shared\
│   ├── assets\
│   ├── libraries\
│   ├── versions\
│   └── runtimes\java\
├── instances\
│   └── <GUID>\
│       ├── instance.json
│       ├── profile\
│       ├── game\
│       │   ├── mods\
│       │   ├── config\
│       │   ├── saves\
│       │   ├── resourcepacks\
│       │   ├── shaderpacks\
│       │   ├── screenshots\
│       │   ├── logs\
│       │   └── crash-reports\
│       ├── runtime\natives\
│       └── backups\
├── cache\
└── logs\launcher\
```

Dos perfiles pueden usar exactamente la misma versión de Minecraft y el mismo loader sin compartir mods, configuraciones, mundos ni otros datos mutables.

## Minecraft y loaders

NEXA puede preparar perfiles para:

- Vanilla;
- Fabric;
- Forge;
- NeoForge.

Assets, libraries y versiones pesadas pueden compartirse de forma controlada. Los datos que pertenecen al jugador permanecen dentro de cada instancia.

## Contenido

La sección **Contenido** permite inspeccionar y administrar mods, resource packs, shader packs y otros archivos del perfil seleccionado. NEXA intenta leer los iconos incluidos en metadata de Fabric, Quilt, Forge/NeoForge y `pack.png`; cuando no existe un icono válido usa un fallback seguro.

Las operaciones destructivas se limitan al perfil seleccionado y requieren confirmación cuando corresponde.

## NEXA In-Game

NEXA In-Game es el componente opcional que se ejecuta dentro de Minecraft. El proyecto utiliza un core común y adaptadores específicos por versión. El **Compiler v2** compila en un workspace temporal, delega en Gradle por target, calcula hashes y publica artefactos en el catálogo local antes de que puedan instalarse en un perfil.

Los perfiles nunca ejecutan Gradle directamente durante el lanzamiento de Minecraft.

## Java

Java es automático globalmente. Cada instancia puede definir un override explícito cuando sea necesario. El selector valida el major requerido por la familia de Minecraft y evita utilizar silenciosamente una versión incompatible.

## Compilar el launcher

Requiere el SDK definido por `global.json` y Node.js para la interfaz React.

```powershell
cd src\NexaLauncher.UI
npm ci
npm run build
cd ..\..

dotnet build NexoLauncher.slnx
dotnet run --project src\NexaLauncher.Desktop
```

## Generar el instalador 1.0.0

Con Inno Setup 6 instalado:

```powershell
.\tools\build-installer.ps1 -Configuration Release -Runtime win-x64
```

El script:

1. restaura y compila React;
2. ejecuta las pruebas de la solución;
3. publica `NexaLauncher.Desktop` como aplicación autocontenida `win-x64`;
4. genera `NEXA-Client-Setup-1.0.0-win-x64.exe` y muestra su SHA-256.

Los outputs locales de publicación e instalador están ignorados por Git. La distribución pública se realiza mediante **GitHub Releases**.

## Principios del proyecto

- integridad de datos antes que conveniencia;
- perfiles aislados físicamente;
- recursos costosos e inmutables compartidos de forma controlada;
- operaciones destructivas limitadas al perfil seleccionado;
- descargas y escrituras críticas verificadas;
- migraciones recuperables y no destructivas;
- argumentos de lanzamiento tokenizados;
- extracción ZIP protegida contra path traversal;
- sin telemetría, anuncios ni overlays innecesarios.

## Créditos

NEXA Client es creado y mantenido por [jr-0205](https://github.com/jr-0205).

NEXA es un proyecto independiente. No está afiliado, patrocinado ni respaldado por Mojang/Microsoft, Modrinth, CurseForge, Lunar Client u OpenAI. Las marcas y servicios de terceros pertenecen a sus respectivos propietarios.
