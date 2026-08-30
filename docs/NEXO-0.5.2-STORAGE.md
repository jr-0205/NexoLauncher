# NEXO 0.5.2 · Storage Stabilization

Este documento registra las invariantes del layout introducido en NEXO 0.5.2.

## Invariantes

- Una versión de Minecraft no es una instancia.
- La identidad física de una instancia es su GUID permanente.
- El nombre visible es metadata y puede cambiar sin mover el directorio físico.
- `shared/` contiene recursos reutilizables: assets, libraries, versions y runtimes.
- `instances/<GUID>/game` contiene todos los datos mutables del usuario y es el `gameDirectory` real.
- `instances/<GUID>/runtime/natives/<launch-guid>` pertenece a un solo lanzamiento.
- Borrar una instancia nunca borra recursos compartidos.
- Ninguna ruta derivada de metadata, ZIPs o modpacks puede escapar de su raíz autorizada.

## Migración desde 0.5.1

1. Se migran `assets`, `libraries`, `versions` y `runtime` históricos hacia `shared/` sin sobrescribir silenciosamente conflictos.
2. Se leen perfiles del layout `<Loader>/<Nombre>/instance.json`.
3. El perfil se mueve mediante `.migration/` al directorio canónico `instances/<GUID>`.
4. El manifest anterior se respalda en `backups/`.
5. Se escribe el manifest de schema actual de forma atómica.
6. Una migración interrumpida se recupera al abrir de nuevo el repositorio.

Los datos de juego heredados se copian hacia una nueva instancia cuando sea necesario; el origen no se modifica durante la importación.

## Importación de modpacks

Los imports que pueden modificar múltiples archivos se preparan en `runtime/import-staging/<transaction-guid>/game`.

- `.mrpack`: descarga `files[]`, valida SHA-512/SHA-1 y aplica `overrides`/`client-overrides`.
- CurseForge: valida manifest, descarga referencias permitidas y aplica overrides.
- `.lcpack` y ZIPs con overrides: extraen únicamente rutas autorizadas.

La publicación usa journal + backups de rollback. Si una importación falla antes de publicarse, `game/` permanece sin cambios; si se interrumpe durante la publicación, NEXO intenta recuperar el estado anterior antes de descartar los backups.

## CurseForge

El login de usuario de CurseForge no entrega una API key para launchers de terceros. Durante desarrollo se acepta `CURSEFORGE_API_KEY` como credencial de desarrollador aprobada. Una distribución pública de NEXO no debe incrustar esa key en el ejecutable; la instalación directa desde catálogo requiere una integración de terceros aprobada que mantenga la credencial fuera del cliente.

## Verificación

```powershell
dotnet restore NexoLauncher.slnx
dotnet build NexoLauncher.slnx -c Release
dotnet run --project tests/NexoLauncher.Core.Tests -c Release --no-build
```

El workflow de CI usa Windows y el SDK fijado por `global.json` (`10.0.202`).
