# NEXO Client

Launcher nativo y ligero para Minecraft Java en Windows. Está construido con C#/.NET 10 y WPF: no usa Electron, navegador integrado, anuncios, overlays ni telemetría propia.

La experiencia visual toma como referencia el nivel de pulido de clientes como Lunar Client, pero NEXO mantiene código, arquitectura, identidad y componentes propios.

## Estado actual · Loader Runtime 0.5

- interfaz WPF nativa con biblioteca, nueva instalación y configuración global;
- perfiles/instancias independientes con `instance.json`;
- catálogo de versiones estables desde el manifiesto oficial de Mojang;
- caché local del manifiesto y metadatos de versión para reducir red y acelerar aperturas;
- instalación Vanilla aislada en `%LOCALAPPDATA%\NexoLauncher`;
- descarga paralela de cliente, bibliotecas, assets y nativos;
- verificación SHA-1 de los archivos publicados por Mojang;
- extracción ZIP segura contra path traversal;
- Java Manager con versión, proveedor, arquitectura y compatibilidad;
- detección de múltiples instalaciones Java en `JAVA_HOME`, `PATH` y ubicaciones relevantes de Program Files;
- inspección de runtimes con paralelismo acotado para mantener el launcher ligero;
- timeout y terminación segura de runtimes defectuosos durante la inspección;
- caché de runtimes Java válida durante 24 horas;
- selección automática del Java correcto para cada versión de Minecraft;
- prioridad para `javaVersion.majorVersion` publicado por Mojang y fallback histórico para releases antiguas;
- no existe un Java global obligatorio: NEXO mantiene todos los runtimes detectados y escoge Java 8, 16, 17, 21, etc. según la instancia que se inicie;
- los overrides manuales de Java quedan reservados para una instancia concreta;
- selector visual de runtimes y búsqueda manual de `java.exe`/`javaw.exe`;
- configuración global para RAM, perfil local y comportamiento del launcher;
- RAM recomendada y límite seguro basados en memoria física de Windows;
- cierre configurable del launcher al iniciar Minecraft;
- usuario local mientras la autenticación Microsoft sigue fuera del flujo de producción.
- arquitectura de loaders mediante `ILoaderProvider`;
- proveedores Vanilla y Fabric separados del `MainWindow`;
- catálogo y perfiles desde la API oficial de Fabric;
- selección y persistencia de versión de Fabric por instancia;
- `LaunchPlan` con `KnotClient`, bibliotecas adicionales y argumentos del loader;
- directorio de juego realmente aislado por GUID de instancia;
- editor de instancia para nombre, RAM, Java, ventana, pantalla completa y argumentos JVM;
- eliminación segura de packs completos desde la biblioteca, siempre con confirmación explícita;
- catálogo de versiones Forge y NeoForge desde sus repositorios Maven oficiales;
- ejecución aislada de los instaladores oficiales para conservar sus procesadores y parches;
- perfiles Forge/NeoForge importados a la arquitectura común de `LaunchPlan`;

## Selección automática de Java

El flujo normal no requiere configurar un Java predeterminado:

```text
Minecraft seleccionado
        ↓
Requisito Java de Mojang
        ↓
Catálogo de runtimes detectados
        ↓
Selección automática por major version
        ↓
Validación x64 + javaw.exe
        ↓
Launch
```

Ejemplo: si el equipo tiene Java 8, 17 y 21 instalados, una instancia antigua puede arrancar con Java 8, una versión intermedia con Java 17 y una versión moderna con Java 21 sin cambiar ajustes globales.

## Datos locales

```text
%LOCALAPPDATA%\NexoLauncher
├── settings.json
├── cache\
├── instances\
├── libraries\
├── assets\
├── runtime\
└── logs\
```

Los archivos de configuración se escriben mediante archivo temporal y movimiento atómico cuando corresponde. NEXO no almacena contraseñas.

## Ejecutar

```powershell
dotnet run --project src/NexoLauncher.App
```

## Verificar

```powershell
dotnet build NexoLauncher.slnx
dotnet run --project tests/NexoLauncher.Core.Tests
```

El harness actual contiene 33 comprobaciones de memoria, configuración, instancias, eliminación aislada de packs, descargas, ZIP seguro, reglas de Minecraft, Java, caché de runtimes, loaders y selección automática por versión.

## Alcance actual

Vanilla, Fabric, Forge y NeoForge forman parte de la línea unificada de NEXO 0.5. Content Manager, autenticación oficial Microsoft/Xbox, Mission Control y el módulo in-game de NEXO se desarrollarán como subsistemas separados, no como lógica añadida al `MainWindow`.

La autenticación local no equivale a una cuenta comprada y no sustituye la autenticación oficial de Minecraft.
