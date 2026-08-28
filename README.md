# Nexo Launcher

Launcher personal, nativo y ligero para Minecraft en Windows. Está construido con C#/.NET 10 y WPF: no usa Electron, navegador integrado, anuncios, overlays ni telemetría propia.

## Primera versión funcional

- interfaz WPF nativa;
- catálogo de versiones estables obtenido del manifiesto oficial de Mojang;
- instalación Vanilla aislada en `%LOCALAPPDATA%\NexoLauncher`;
- descarga paralela de cliente, bibliotecas, assets y nativos;
- verificación SHA-1 de los archivos publicados por Mojang;
- detección de Java mediante `JAVA_HOME` y `PATH`, con selector manual de `javaw.exe`;
- configuración de RAM entre 1 y 8 GB;
- arranque con usuario local;
- cierre completo del launcher después de iniciar Java.

## Ejecutar

```powershell
dotnet run --project src/NexoLauncher.App
```

Para versiones modernas selecciona un Java 21 de 64 bits. La aplicación no descarga Java todavía.

## Verificar

```powershell
dotnet build NexoLauncher.slnx
dotnet run --project tests/NexoLauncher.Core.Tests
```

## Alcance actual

El usuario local permite jugar en un jugador y entrar a servidores configurados sin autenticación. No equivale a una cuenta comprada. La autenticación oficial de Microsoft/Xbox, la descarga automática de Java y los adaptadores Fabric/Forge/NeoForge son los siguientes hitos; aún no están implementados.
