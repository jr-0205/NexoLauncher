using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using NexoLauncher.Application.Instances;
using NexoLauncher.Core.Installation;
using NexoLauncher.Domain.Instances;
using NexoLauncher.Infrastructure.Content;
using NexoLauncher.Infrastructure.Instances;
using NexoLauncher.Java.Detection;
using NexoLauncher.Java.Selection;

namespace NexaLauncher.Desktop;

/// <summary>
/// Mantiene las capacidades nuevas de la migración React fuera del bridge principal.
/// Sólo consume servicios C# existentes: React nunca obtiene acceso directo al disco.
/// </summary>
internal sealed class NexaDesktopMessageRouter
{
    private const string ArtworkLayoutName = "artwork-layout.json";
    private readonly CoreWebView2 webView;
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromMinutes(20) };
    private readonly JsonInstanceRepository instances;
    private readonly InstanceManager instanceManager;
    private readonly ModrinthContentClient catalog;
    private readonly NexoBoostService boost;
    private readonly NexoBoostVisualPackService boostVisual;
    private readonly NexoBoostPresetService boostPreset = new();
    private readonly NexoInGameArtifactService inGame;
    private readonly NexoInGameBuildService inGameBuilds;
    private readonly JavaRuntimeDetector javaDetector = new(new JavaRuntimeInspector());
    private readonly SemaphoreSlim mutationLock = new(1, 1);
    private readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public NexaDesktopMessageRouter(NexoPaths paths, CoreWebView2 webView)
    {
        this.webView = webView;
        instances = new JsonInstanceRepository(paths.Instances);
        instanceManager = new InstanceManager(instances);
        catalog = new ModrinthContentClient(http);
        boost = new NexoBoostService(catalog);
        boostVisual = new NexoBoostVisualPackService(catalog);
        inGameBuilds = new NexoInGameBuildService(http, paths);

        var developmentRoot = FindDevelopmentArtifactRoot();
        var localBuildRoot = PrepareLocalBuildArtifactRoot(paths, developmentRoot);
        inGame = new NexoInGameArtifactService(http, catalog, paths, localBuildRoot);
    }

    public async Task<bool> TryHandleAsync(CoreWebView2WebMessageReceivedEventArgs eventArgs)
    {
        DesktopRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<DesktopRequest>(eventArgs.WebMessageAsJson, json);
        }
        catch (JsonException)
        {
            return false;
        }

        if (request is null || string.IsNullOrWhiteSpace(request.Method)) return false;
        if (!request.Method.StartsWith("boost.", StringComparison.Ordinal) &&
            !request.Method.StartsWith("ingame.", StringComparison.Ordinal) &&
            !request.Method.StartsWith("artwork.", StringComparison.Ordinal)) return false;

        try
        {
            object result = request.Method switch
            {
                "boost.status" => await BoostStatusAsync(request.Payload),
                "boost.apply" => await ApplyBoostAsync(request.Payload),
                "boost.remove" => await RemoveBoostAsync(request.Payload),
                "ingame.status" => await InGameStatusAsync(request.Payload),
                "ingame.install" => await InstallInGameAsync(request.Payload),
                "ingame.builds.status" => await InGameBuildStatusAsync(),
                "ingame.builds.generate" => await GenerateInGameBuildsAsync(),
                "ingame.builds.openFolder" => OpenInGameBuildFolder(),
                "artwork.list" => await ArtworkListAsync(),
                "artwork.update" => await UpdateArtworkAsync(request.Payload),
                _ => throw new NotSupportedException($"El método '{request.Method}' todavía no está disponible en NEXA.")
            };
            Post(new DesktopResponse(request.Id, true, result, null));
        }
        catch (Exception exception)
        {
            Post(new DesktopResponse(request.Id, false, null, exception.Message));
        }

        return true;
    }

    private async Task<object> BoostStatusAsync(JsonElement payload)
    {
        var id = InstanceId.Parse(Read<ProfileIdRequest>(payload).Id);
        var profile = await instanceManager.GetAsync(id) ?? throw new InvalidOperationException("El perfil ya no existe.");
        var game = instances.GetPaths(id).Game;
        var components = boost.Recommend(profile.Loader);
        return new
        {
            supported = components.Count > 0,
            applied = boost.IsApplied(game),
            visualApplied = boostVisual.IsApplied(game),
            profileId = id.ToString(),
            minecraftVersion = profile.MinecraftVersion,
            loader = profile.Loader.ToString(),
            components = components.Select(component => new
            {
                id = component.ProjectId,
                name = component.Name,
                purpose = component.Purpose
            }).ToArray()
        };
    }

    private async Task<object> ApplyBoostAsync(JsonElement payload)
    {
        var id = InstanceId.Parse(Read<ProfileIdRequest>(payload).Id);
        await mutationLock.WaitAsync();
        try
        {
            var profile = await instanceManager.GetAsync(id) ?? throw new InvalidOperationException("El perfil ya no existe.");
            var game = instances.GetPaths(id).Game;
            var components = boost.Recommend(profile.Loader);
            if (components.Count == 0)
                throw new NotSupportedException("NEXA Boost requiere Fabric, Forge o NeoForge. Por seguridad no convierte perfiles Vanilla.");

            PostEvent("operation.progress", new { stage = "Preparando NEXA Boost", completed = 0, total = 0 });

            NexoBoostApplyResult? baseResult = null;
            var alreadyApplied = boost.IsApplied(game);
            if (!alreadyApplied)
            {
                PostEvent("operation.progress", new { stage = "Instalando optimizaciones compatibles", completed = 0, total = 0 });
                baseResult = await boost.ApplyAsync(profile, game);
            }

            PostEvent("operation.progress", new { stage = "Preparando optimización visual", completed = 0, total = 0 });
            var visual = await boostVisual.ApplyAsync(profile, game);
            PostEvent("operation.progress", new { stage = "Aplicando preset Equilibrado", completed = 0, total = 0 });
            var preset = await boostPreset.ApplyAsync(game, NexoBoostPreset.Balanced);
            PostEvent("operation.progress", new { stage = "NEXA Boost listo", completed = 1, total = 1, percentage = 100 });

            return new
            {
                applied = true,
                reapplied = alreadyApplied,
                filesInstalled = (baseResult?.FilesInstalled ?? 0) + visual.FilesInstalled,
                installedFiles = (baseResult?.InstalledFiles ?? []).Concat(visual.InstalledFiles).ToArray(),
                skippedComponents = baseResult?.SkippedComponents ?? [],
                presetChanges = preset.Changes,
                particleCoreConfigured = preset.ParticleCoreConfigured,
                note = visual.Note
            };
        }
        finally
        {
            mutationLock.Release();
        }
    }

    private async Task<object> RemoveBoostAsync(JsonElement payload)
    {
        var id = InstanceId.Parse(Read<ProfileIdRequest>(payload).Id);
        await mutationLock.WaitAsync();
        try
        {
            _ = await instanceManager.GetAsync(id) ?? throw new InvalidOperationException("El perfil ya no existe.");
            var game = instances.GetPaths(id).Game;
            if (!boost.IsApplied(game)) return new { applied = false, filesRemoved = 0, valuesRestored = 0, preserved = Array.Empty<string>() };

            PostEvent("operation.progress", new { stage = "Restaurando ajustes de NEXA Boost", completed = 0, total = 0 });
            var preset = await boostPreset.RestoreAsync(game);
            var visual = await boostVisual.RemoveAsync(game);
            var baseResult = await boost.RemoveAsync(game);
            var preserved = preset.PreservedValues.Concat(visual.PreservedFiles).Concat(baseResult.PreservedFiles).ToArray();
            PostEvent("operation.progress", new { stage = "NEXA Boost desactivado", completed = 1, total = 1, percentage = 100 });

            return new
            {
                applied = false,
                filesRemoved = visual.FilesRemoved + baseResult.FilesRemoved,
                valuesRestored = preset.ValuesRestored,
                preserved
            };
        }
        finally
        {
            mutationLock.Release();
        }
    }

    private async Task<object> InGameStatusAsync(JsonElement payload)
    {
        var id = InstanceId.Parse(Read<ProfileIdRequest>(payload).Id);
        var profile = await instanceManager.GetAsync(id) ?? throw new InvalidOperationException("El perfil ya no existe.");
        var game = instances.GetPaths(id).Game;
        var installedFile = FindInstalledInGameJar(game);

        NexoInGameArtifactCatalog artifactCatalog;
        try
        {
            artifactCatalog = await inGame.LoadCatalogAsync();
        }
        catch when (installedFile is not null)
        {
            return new
            {
                installed = true,
                available = false,
                profileId = id.ToString(),
                minecraftVersion = profile.MinecraftVersion,
                loader = profile.Loader.ToString(),
                version = (string?)null,
                fileName = installedFile,
                catalogStatus = "installed",
                message = "NEXA In-Game está instalado. No se pudo comprobar el catálogo de actualizaciones."
            };
        }

        var published = NexoInGameArtifactService.SelectPublishedArtifact(artifactCatalog, profile);
        var registered = artifactCatalog.Artifacts.FirstOrDefault(candidate =>
            string.Equals(candidate.MinecraftVersion, profile.MinecraftVersion, StringComparison.Ordinal) &&
            string.Equals(candidate.Loader, profile.Loader.ToString(), StringComparison.OrdinalIgnoreCase));

        if (installedFile is not null)
        {
            return new
            {
                installed = true,
                available = published is not null,
                profileId = id.ToString(),
                minecraftVersion = profile.MinecraftVersion,
                loader = profile.Loader.ToString(),
                version = published?.NexoInGameVersion ?? registered?.NexoInGameVersion,
                fileName = installedFile,
                catalogStatus = "installed",
                message = "NEXA In-Game está instalado. Shift derecho abrirá el Control Center dentro de Minecraft."
            };
        }

        if (published is not null)
        {
            return new
            {
                installed = false,
                available = true,
                profileId = id.ToString(),
                minecraftVersion = profile.MinecraftVersion,
                loader = profile.Loader.ToString(),
                version = published.NexoInGameVersion,
                fileName = published.FileName,
                catalogStatus = "published",
                message = $"NEXA In-Game {published.NexoInGameVersion} está listo para Minecraft {profile.MinecraftVersion} + {profile.Loader}."
            };
        }

        if (registered is not null)
        {
            return new
            {
                installed = false,
                available = false,
                profileId = id.ToString(),
                minecraftVersion = profile.MinecraftVersion,
                loader = profile.Loader.ToString(),
                version = registered.NexoInGameVersion,
                fileName = registered.FileName,
                catalogStatus = "planned",
                message = $"La build de NEXA In-Game para Minecraft {profile.MinecraftVersion} + {profile.Loader} está registrada, pero todavía no se ha publicado."
            };
        }

        return new
        {
            installed = false,
            available = false,
            profileId = id.ToString(),
            minecraftVersion = profile.MinecraftVersion,
            loader = profile.Loader.ToString(),
            version = (string?)null,
            fileName = (string?)null,
            catalogStatus = "unavailable",
            message = $"Aún no hay una build de NEXA In-Game para Minecraft {profile.MinecraftVersion} + {profile.Loader}. El launcher la resolverá automáticamente cuando se publique."
        };
    }

    private async Task<object> InstallInGameAsync(JsonElement payload)
    {
        var id = InstanceId.Parse(Read<ProfileIdRequest>(payload).Id);
        await mutationLock.WaitAsync();
        try
        {
            var profile = await instanceManager.GetAsync(id) ?? throw new InvalidOperationException("El perfil ya no existe.");
            var game = instances.GetPaths(id).Game;
            var artifact = await inGame.FindPublishedArtifactAsync(profile);
            if (artifact is null)
                throw new NotSupportedException($"Todavía no existe una build publicada de NEXA In-Game para Minecraft {profile.MinecraftVersion} + {profile.Loader}.");

            PostEvent("operation.progress", new { stage = "Preparando NEXA In-Game", completed = 0, total = 0 });
            var progress = new Progress<string>(stage =>
                PostEvent("operation.progress", new { stage, completed = 0, total = 0 }));
            var result = await inGame.InstallAsync(profile, game, progress);
            PostEvent("operation.progress", new { stage = "NEXA In-Game listo · Right Shift", completed = 1, total = 1, percentage = 100 });

            return new
            {
                installed = true,
                version = result.Version,
                fileName = result.FileName,
                usedCache = result.UsedCache,
                dependenciesInstalled = result.DependenciesInstalled
            };
        }
        finally
        {
            mutationLock.Release();
        }
    }

    private async Task<object> InGameBuildStatusAsync()
    {
        var repositoryRoot = FindRepositoryRoot();
        IReadOnlyList<NexoInGameBuildTarget> targets = [];
        string? sourceError = null;
        if (repositoryRoot is not null)
        {
            try
            {
                targets = inGameBuilds.DiscoverTargets(repositoryRoot);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                sourceError = exception.Message;
            }
        }

        var localCatalog = await LoadLocalBuildCatalogAsync();
        var entries = new List<object>();
        var targetKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var target in targets)
        {
            var key = BuildKey(target.MinecraftVersion, target.Loader);
            targetKeys.Add(key);
            var artifact = localCatalog?.Artifacts.FirstOrDefault(candidate =>
                string.Equals(candidate.MinecraftVersion, target.MinecraftVersion, StringComparison.Ordinal) &&
                string.Equals(candidate.Loader, target.Loader, StringComparison.OrdinalIgnoreCase));
            entries.Add(BuildEntry(target, artifact));
        }

        foreach (var artifact in localCatalog?.Artifacts ?? [])
        {
            if (targetKeys.Contains(BuildKey(artifact.MinecraftVersion, artifact.Loader))) continue;
            entries.Add(BuildEntry(null, artifact));
        }

        var publishedCount = localCatalog?.Artifacts.Count(artifact =>
            string.Equals(artifact.Status, "published", StringComparison.OrdinalIgnoreCase) &&
            ArtifactExists(artifact)) ?? 0;

        return new
        {
            sourceAvailable = repositoryRoot is not null && sourceError is null,
            sourceError,
            repositoryRoot,
            outputRoot = inGameBuilds.OutputRoot,
            targetCount = targets.Count,
            publishedCount,
            pendingCount = Math.Max(0, targets.Count - publishedCount),
            targets = targets.Select(target => new
            {
                minecraftVersion = target.MinecraftVersion,
                loader = target.Loader,
                nexaInGameVersion = target.NexoInGameVersion,
                javaMajor = target.JavaMajor,
                gradleVersion = target.GradleVersion,
                fileName = target.FileName
            }).ToArray(),
            builds = entries,
            lastPublishedAt = localCatalog?.Artifacts
                .Where(artifact => string.Equals(artifact.Status, "published", StringComparison.OrdinalIgnoreCase))
                .MaxBy(artifact => artifact.PublishedAt)?.PublishedAt
        };
    }

    private async Task<object> GenerateInGameBuildsAsync()
    {
        await mutationLock.WaitAsync();
        try
        {
            var repositoryRoot = FindRepositoryRoot()
                ?? throw new DirectoryNotFoundException("No se encontró el checkout de NexoLauncher con ingame/. El generador sólo está disponible desde una build de desarrollo del repositorio.");
            var targets = inGameBuilds.DiscoverTargets(repositoryRoot);
            if (targets.Count == 0)
                throw new InvalidOperationException("No se encontraron proyectos compilables de NEXA In-Game.");

            PostEvent("operation.progress", new { stage = "Detectando runtimes Java para builds NEXA", completed = 0, total = 0 });
            var runtimes = await javaDetector.DetectAsync();
            var requiredMajors = targets.Select(target => target.JavaMajor).Distinct().OrderBy(value => value).ToArray();
            var javaByMajor = requiredMajors
                .Select(major => (Major: major, Runtime: JavaRuntimeSelector.Select(runtimes, major)))
                .Where(item => item.Runtime is not null)
                .ToDictionary(item => item.Major, item => item.Runtime!.JavaExecutable);
            var missing = requiredMajors.Where(major => !javaByMajor.ContainsKey(major)).ToArray();
            if (missing.Length > 0)
                throw new InvalidOperationException("Falta un runtime para compilar NEXA In-Game: " + string.Join(", ", missing.Select(major => $"Java {major}")) + ".");

            var progress = new Progress<string>(stage =>
                PostEvent("operation.progress", new { stage, completed = 0, total = 0 }));
            var result = await inGameBuilds.BuildAllAsync(
                repositoryRoot,
                major => javaByMajor.TryGetValue(major, out var executable) ? executable : null,
                progress);

            var published = result.Artifacts.Count(artifact =>
                string.Equals(artifact.Status, "published", StringComparison.OrdinalIgnoreCase));
            PostEvent("operation.progress", new
            {
                stage = result.Failures.Count == 0
                    ? $"NEXA In-Game listo · {published} builds publicadas localmente"
                    : $"NEXA In-Game · {published} listas, {result.Failures.Count} con error",
                completed = 1,
                total = 1,
                percentage = 100
            });

            return new
            {
                publishedCount = published,
                failureCount = result.Failures.Count,
                failures = result.Failures.Select(failure => new
                {
                    minecraftVersion = failure.MinecraftVersion,
                    loader = failure.Loader,
                    message = failure.Message
                }).ToArray(),
                library = await InGameBuildStatusAsync()
            };
        }
        finally
        {
            mutationLock.Release();
        }
    }

    private object OpenInGameBuildFolder()
    {
        Directory.CreateDirectory(inGameBuilds.OutputRoot);
        var startInfo = new ProcessStartInfo
        {
            FileName = "explorer.exe",
            UseShellExecute = true
        };
        startInfo.ArgumentList.Add(inGameBuilds.OutputRoot);
        Process.Start(startInfo);
        return new { opened = true, path = inGameBuilds.OutputRoot };
    }

    private async Task<NexoInGameArtifactCatalog?> LoadLocalBuildCatalogAsync()
    {
        var path = Path.Combine(inGameBuilds.OutputRoot, "catalog.json");
        if (!File.Exists(path)) return null;
        try
        {
            await using var stream = File.OpenRead(path);
            var value = await JsonSerializer.DeserializeAsync<NexoInGameArtifactCatalog>(stream, json);
            return value?.SchemaVersion == NexoInGameArtifactService.CatalogSchema ? value : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private object BuildEntry(NexoInGameBuildTarget? target, NexoInGameArtifact? artifact)
    {
        var relativePath = artifact?.RelativePath;
        var exists = artifact is not null && ArtifactExists(artifact);
        var status = artifact is null
            ? "missing"
            : string.Equals(artifact.Status, "published", StringComparison.OrdinalIgnoreCase) && !exists
                ? "missing"
                : artifact.Status.ToLowerInvariant();
        long sizeBytes = 0;
        if (exists && !string.IsNullOrWhiteSpace(relativePath))
        {
            try
            {
                var path = Path.Combine(inGameBuilds.OutputRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
                sizeBytes = new FileInfo(path).Length;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException) { }
        }

        return new
        {
            minecraftVersion = target?.MinecraftVersion ?? artifact!.MinecraftVersion,
            loader = target?.Loader ?? artifact!.Loader,
            nexaInGameVersion = target?.NexoInGameVersion ?? artifact!.NexoInGameVersion,
            javaMajor = target?.JavaMajor,
            gradleVersion = target?.GradleVersion,
            fileName = artifact?.FileName ?? target?.FileName,
            relativePath,
            status,
            exists,
            sizeBytes,
            sha256 = artifact?.Sha256,
            publishedAt = artifact?.PublishedAt
        };
    }

    private bool ArtifactExists(NexoInGameArtifact artifact)
    {
        if (string.IsNullOrWhiteSpace(artifact.RelativePath)) return false;
        try
        {
            var root = Path.GetFullPath(inGameBuilds.OutputRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var candidate = Path.GetFullPath(Path.Combine(root, artifact.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
            return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(candidate);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private static string BuildKey(string minecraftVersion, string loader) => $"{loader.Trim().ToLowerInvariant()}::{minecraftVersion.Trim()}";

    private static string? FindInstalledInGameJar(string gameDirectory)
    {
        var mods = Path.Combine(gameDirectory, "mods");
        if (!Directory.Exists(mods)) return null;
        return Directory.EnumerateFiles(mods, "nexo-ingame*.jar", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));
    }

    private static string? FindRepositoryRoot()
    {
        foreach (var root in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            DirectoryInfo? directory;
            try { directory = new DirectoryInfo(Path.GetFullPath(root)); }
            catch { continue; }

            for (var depth = 0; directory is not null && depth < 12; depth++, directory = directory.Parent)
            {
                if (!Directory.Exists(Path.Combine(directory.FullName, "ingame"))) continue;
                if (!File.Exists(Path.Combine(directory.FullName, "artifacts", "nexo-ingame", "catalog.json"))) continue;
                return directory.FullName;
            }
        }

        return null;
    }

    private static string? FindDevelopmentArtifactRoot()
    {
        var repositoryRoot = FindRepositoryRoot();
        return repositoryRoot is null ? null : Path.Combine(repositoryRoot, "artifacts", "nexo-ingame");
    }

    private static string PrepareLocalBuildArtifactRoot(NexoPaths paths, string? developmentRoot)
    {
        var localRoot = Path.GetFullPath(Path.Combine(paths.Launcher, "nexo-ingame"));
        var localCatalog = Path.Combine(localRoot, "catalog.json");
        if (File.Exists(localCatalog) || developmentRoot is null) return localRoot;

        var developmentCatalog = Path.Combine(developmentRoot, "catalog.json");
        if (!File.Exists(developmentCatalog)) return localRoot;
        try
        {
            Directory.CreateDirectory(localRoot);
            File.Copy(developmentCatalog, localCatalog, overwrite: false);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return localRoot;
    }

    private async Task<object> ArtworkListAsync()
    {
        var profiles = await instances.ListAsync();
        return profiles.Select(profile => new
        {
            id = profile.Id.ToString(),
            artwork = ReadArtworkLayout(profile.Id)
        }).ToArray();
    }

    private async Task<object> UpdateArtworkAsync(JsonElement payload)
    {
        var request = Read<ArtworkRequest>(payload);
        var id = InstanceId.Parse(request.Id);
        await mutationLock.WaitAsync();
        try
        {
            _ = await instanceManager.GetAsync(id) ?? throw new InvalidOperationException("El perfil ya no existe.");
            var placement = new ArtworkPlacement(
                request.IconPositionX,
                request.IconPositionY,
                request.IconFit,
                request.IconZoom,
                request.BackgroundPositionX,
                request.BackgroundPositionY,
                request.BackgroundFit,
                request.BackgroundZoom).Normalize();
            await WriteArtworkLayoutAsync(id, placement);
            return new { id = id.ToString(), artwork = placement };
        }
        finally
        {
            mutationLock.Release();
        }
    }

    private ArtworkPlacement ReadArtworkLayout(InstanceId id)
    {
        var path = ArtworkLayoutPath(id, ensureDirectory: false);
        if (!File.Exists(path)) return ArtworkPlacement.Default;
        try
        {
            return (JsonSerializer.Deserialize<ArtworkPlacement>(File.ReadAllBytes(path), json) ?? ArtworkPlacement.Default).Normalize();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            return ArtworkPlacement.Default;
        }
    }

    private async Task WriteArtworkLayoutAsync(InstanceId id, ArtworkPlacement placement)
    {
        var path = ArtworkLayoutPath(id, ensureDirectory: true);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllBytesAsync(temporary, JsonSerializer.SerializeToUtf8Bytes(placement, json));
            using (var stream = new FileStream(temporary, FileMode.Open, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.WriteThrough))
                stream.Flush(flushToDisk: true);
            File.Move(temporary, path, true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private string ArtworkLayoutPath(InstanceId id, bool ensureDirectory)
    {
        var root = instances.GetInstanceDirectory(id);
        var profile = Path.Combine(root, "profile");
        if (Directory.Exists(profile) && new DirectoryInfo(profile).Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidDataException("profile/ no puede ser un enlace o junction para guardar el encuadre.");
        if (ensureDirectory) Directory.CreateDirectory(profile);
        return Path.Combine(profile, ArtworkLayoutName);
    }

    private static T Read<T>(JsonElement payload) =>
        payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            ? throw new InvalidDataException("La solicitud no contiene datos.")
            : payload.Deserialize<T>(new JsonSerializerOptions(JsonSerializerDefaults.Web))
              ?? throw new InvalidDataException("La solicitud no pudo interpretarse.");

    private void Post(object value) => webView.PostWebMessageAsJson(JsonSerializer.Serialize(value, json));
    private void PostEvent(string name, object payload) => Post(new { @event = name, payload });

    private sealed record DesktopRequest(string Id, string Method, JsonElement Payload);
    private sealed record DesktopResponse(string Id, bool Ok, object? Result, string? Error);
    private sealed record ProfileIdRequest(string Id);
    private sealed record ArtworkRequest(
        string Id,
        double IconPositionX,
        double IconPositionY,
        string IconFit,
        double IconZoom,
        double BackgroundPositionX,
        double BackgroundPositionY,
        string BackgroundFit,
        double BackgroundZoom);

    private sealed record ArtworkPlacement(
        double IconPositionX,
        double IconPositionY,
        string IconFit,
        double IconZoom,
        double BackgroundPositionX,
        double BackgroundPositionY,
        string BackgroundFit,
        double BackgroundZoom)
    {
        public static ArtworkPlacement Default { get; } = new(50, 50, "contain", 100, 50, 50, "cover", 100);

        public ArtworkPlacement Normalize() => this with
        {
            IconPositionX = Math.Clamp(IconPositionX, 0, 100),
            IconPositionY = Math.Clamp(IconPositionY, 0, 100),
            IconFit = NormalizeFit(IconFit, "contain"),
            IconZoom = NormalizeZoom(IconZoom),
            BackgroundPositionX = Math.Clamp(BackgroundPositionX, 0, 100),
            BackgroundPositionY = Math.Clamp(BackgroundPositionY, 0, 100),
            BackgroundFit = NormalizeFit(BackgroundFit, "cover"),
            BackgroundZoom = NormalizeZoom(BackgroundZoom)
        };

        private static double NormalizeZoom(double value) => value <= 0 ? 100 : Math.Clamp(value, 50, 300);

        private static string NormalizeFit(string? value, string fallback) =>
            string.Equals(value, "cover", StringComparison.OrdinalIgnoreCase) ? "cover" :
            string.Equals(value, "contain", StringComparison.OrdinalIgnoreCase) ? "contain" : fallback;
    }
}
