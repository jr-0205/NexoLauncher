# NEXO Client

Launcher nativo y ligero para Minecraft Java en Windows. Está construido con C#/.NET 10 y WPF: no usa Electron, navegador integrado, anuncios, overlays ni telemetría propia.

La experiencia visual toma como referencia el nivel de pulido de clientes como Lunar Client, pero NEXO mantiene código, arquitectura, identidad y componentes propios.

## Estado actual · Runtime Experience 0.3

- interfaz WPF nativa con biblioteca, nueva instalación y configuración global;
- perfiles/instancias independientes con `instance.json`;
- catálogo de versiones estables desde el manifiesto oficial de Mojang;
- caché local del manifiesto y metadatos de versión para reducir red y acelerar aperturas;
- instalación Vanilla aislada en `%LOCALAPPDATA%\NexoLauncher`;
- descarga paralela de cliente, bibliotecas, assets y nativos;
- verificación SHA-1 de los archivos publicados por Mojang;
- extracción ZIP segura contra path traversal;
- Java Manager con versión, proveedor, arquitectura y compatibilidad;
- detección Java limitada a rutas relevantes, sin escaneo recursivo pesado de Program Files;
- caché de runtimes Java válida durante 24 horas;
- selección automática del Java compatible con `javaVersion.majorVersion` de Mojang;
- selector visual de runtimes y búsqueda manual de `java.exe`/`javaw.exe`;
- configuración global con herencia `Global Default -> Instance Override`;
- RAM recomendada y límite seguro basados en memoria física de Windows;
- cierre configurable del launcher después de iniciar Minecraft;
- usuario local mientras la autenticación Microsoft sigue fuera del flujo de producción.

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

El harness actual contiene 20 comprobaciones de memoria, configuración, instancias, descargas, ZIP seguro, reglas de Minecraft, Java y caché de runtimes.

## Alcance actual

Vanilla es el proveedor funcional actual. Fabric, Forge, NeoForge, Content Manager, autenticación oficial Microsoft/Xbox, Mission Control y el módulo in-game de NEXO se desarrollarán como subsistemas separados, no como lógica añadida al `MainWindow`.

La autenticación local no equivale a una cuenta comprada y no sustituye la autenticación oficial de Minecraft.
